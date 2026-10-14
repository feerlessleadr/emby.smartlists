using System.Text.Json;
using Emby.Plugin.SmartLists.Api.Controllers;
using Emby.Plugin.SmartLists.Configuration;
using Emby.Plugin.SmartLists.Notifications;

namespace Emby.Plugin.SmartLists.Tests.Notifications;

public class NotificationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "smartlists-notif-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private NotificationSwitches _switches = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private NotificationAggregator NewAggregator() => new(() => _switches, () => _now);

    private string LogPath => Path.Combine(_folder, "events.json");

    private RefreshOutcome Outcome(string id, bool success = false, string? error = "Boom", string trigger = "Scheduled") => new()
    {
        ListId = id,
        ListName = "List " + id,
        ListType = "Playlist",
        Trigger = trigger,
        Success = success,
        Error = success ? null : error,
        StartUtc = _now.AddSeconds(-3),
        EndUtc = _now,
    };

    private IReadOnlyList<PendingEvent> After(NotificationAggregator a, TimeSpan wait, bool idle = true)
    {
        _now += wait;
        return a.Tick(idle);
    }

    // ---- RefreshFailed ----

    [Fact]
    public void Failure_IsSentOnceAfterTheBatchWindow()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Empty(a.Tick(true)); // not yet: other lists may fail together

        var events = After(a, NotificationAggregator.FailureBatchWindow);
        var e = Assert.Single(events);
        Assert.Equal(EventNames.RefreshFailed, e.Event);
        Assert.Equal(1, e.Fields["Count"]);
        Assert.Empty(After(a, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void SeveralListsFailingTogetherMakeOneEventWithTheFirstFive()
    {
        var a = NewAggregator();
        for (var i = 0; i < 8; i++)
        {
            a.OnRefreshCompleted(Outcome("list" + i));
        }

        var e = Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
        Assert.Equal(8, e.Fields["Count"]);
        var lists = Assert.IsType<List<Dictionary<string, object?>>>(e.Fields["Lists"]);
        Assert.Equal(5, lists.Count);
        Assert.Equal(new[] { "ListId", "ListName", "ListType", "Error", "Trigger" }, lists[0].Keys.ToArray());
    }

    [Fact]
    public void ThePhoneHearsAboutOneListOnlyOncePerHour()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));

        _now += TimeSpan.FromMinutes(30);
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Empty(After(a, NotificationAggregator.FailureBatchWindow));

        _now += TimeSpan.FromMinutes(31);
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
    }

    [Fact]
    public void AnotherListStillGetsItsOwnAlertWithinTheHour()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
        a.OnRefreshCompleted(Outcome("b"));
        Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
    }

    [Theory]
    [InlineData("Operation was cancelled")]
    [InlineData("Invalid list ID format")]
    [InlineData("Cannot refresh disabled list")]
    [InlineData("Smart list not found")]
    public void CancelledAndBadRequestsAreNotAlerts(string error)
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", error: error));
        Assert.Empty(After(a, NotificationAggregator.FailureBatchWindow));
    }

    [Fact]
    public void SuccessSendsNothing()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", success: true));
        Assert.Empty(After(a, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void SwitchedOffEventsAreNotSentAndDoNotUseUpTheHour()
    {
        var a = NewAggregator();
        _switches.RefreshFailed = false;
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Empty(After(a, NotificationAggregator.FailureBatchWindow));

        _switches.RefreshFailed = true;
        a.OnRefreshCompleted(Outcome("a"));
        Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
    }

    [Fact]
    public void TheMasterSwitchSilencesEverything()
    {
        var a = NewAggregator();
        _switches.SendPhoneAlerts = false;
        _switches.RefreshRunFinished = true;
        a.OnRefreshCompleted(Outcome("a"));
        a.OnRefreshCompleted(Outcome("b", success: true));
        a.OnRefreshCompleted(Outcome("c", success: true));
        Assert.Empty(After(a, TimeSpan.FromMinutes(1)));
        Assert.Null(a.OnTimerStopped("stopped"));
    }

    [Fact]
    public void SwitchTurnedOffBeforeTheBatchIsSentDropsTheBatch()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a"));
        _switches.SendPhoneAlerts = false;
        Assert.Empty(After(a, NotificationAggregator.FailureBatchWindow));
    }

    // ---- RefreshRunFinished ----

    [Fact]
    public void ARunWithSeveralListsMakesOneSummaryWhenTheQueueIsIdle()
    {
        _switches.RefreshRunFinished = true;
        _switches.RefreshFailed = false;
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", success: true, trigger: "Manual"));
        a.OnRefreshCompleted(Outcome("b", success: true, trigger: "Manual"));
        a.OnRefreshCompleted(Outcome("c", trigger: "Manual"));

        Assert.Empty(After(a, TimeSpan.FromSeconds(1)));
        Assert.Empty(After(a, NotificationAggregator.RunQuietWindow, idle: false)); // still busy

        var e = Assert.Single(After(a, TimeSpan.FromSeconds(1)));
        Assert.Equal(EventNames.RefreshRunFinished, e.Event);
        Assert.Equal(3, e.Fields["Total"]);
        Assert.Equal(2, e.Fields["Succeeded"]);
        Assert.Equal(1, e.Fields["Failed"]);
        Assert.Equal(3.0, e.Fields["DurationSeconds"]);
        Assert.Empty(After(a, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void RunSummaryIsOffByDefault()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", success: true, trigger: "Manual"));
        a.OnRefreshCompleted(Outcome("b", success: true, trigger: "Manual"));
        Assert.Empty(After(a, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void OneListOrAutoRefreshesAreNotARun()
    {
        _switches.RefreshRunFinished = true;
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", success: true, trigger: "Manual"));
        Assert.Empty(After(a, TimeSpan.FromMinutes(1)));

        a.OnRefreshCompleted(Outcome("b", success: true, trigger: "Auto"));
        a.OnRefreshCompleted(Outcome("c", success: true, trigger: "Auto"));
        Assert.Empty(After(a, TimeSpan.FromMinutes(1)));
    }

    // ---- TimerStopped ----

    [Fact]
    public void StoppedTimerIsSentAtMostOncePerDay()
    {
        var a = NewAggregator();
        var first = a.OnTimerStopped("Scheduled refreshes stopped.");
        Assert.NotNull(first);
        Assert.Equal(EventNames.TimerStopped, first!.Event);
        Assert.Equal("Scheduled refreshes stopped.", first.Fields["Message"]);

        _now += TimeSpan.FromHours(23);
        Assert.Null(a.OnTimerStopped("again"));
        _now += TimeSpan.FromHours(2);
        Assert.NotNull(a.OnTimerStopped("again"));
    }

    [Fact]
    public void StoppedTimerSwitchOffSendsNothing()
    {
        var a = NewAggregator();
        _switches.TimerStopped = false;
        Assert.Null(a.OnTimerStopped("x"));
    }

    // ---- No paths ----

    [Theory]
    [InlineData(@"Could not find file 'C:\Media\Music\a.mp3'.", "Could not find file '[path]'.")]
    [InlineData(@"Access denied to \\nas\share\Lists\x.json", "Access denied to [path]")]
    [InlineData("Cannot write /var/lib/emby/data/smartlists/abc/config.json today", "Cannot write [path] today")]
    [InlineData("No such file D:/Library/Lists/one.json", "No such file [path]")]
    [InlineData("Plain sentence with a ratio 1/2 and and/or", "Plain sentence with a ratio 1/2 and and/or")]
    public void PathsAreStrippedFromSentences(string input, string expected)
    {
        Assert.Equal(expected, NotificationAggregator.StripPaths(input));
    }

    [Fact]
    public void LongAndMultiLineMessagesAreShortened()
    {
        var text = NotificationAggregator.StripPaths("first line\nsecond line at C:\\secret\\x");
        Assert.Equal("first line", text);
        Assert.True(NotificationAggregator.StripPaths(new string('x', 500)).Length <= NotificationAggregator.MaxErrorLength);
    }

    [Fact]
    public void PayloadsNeverContainAPathDriveOrShare()
    {
        var a = NewAggregator();
        a.OnRefreshCompleted(Outcome("a", error: @"Could not open 'C:\Users\someone\Music\x.mp3' on \\server\music"));
        var failure = Assert.Single(After(a, NotificationAggregator.FailureBatchWindow));
        var log = new EventLog(LogPath, () => _now);
        var json = log.Append(failure).Data.GetRawText();

        Assert.DoesNotContain("C:", json);
        Assert.DoesNotContain("someone", json);
        Assert.DoesNotContain("server", json);
        Assert.DoesNotContain("\\\\", json);
    }

    // ---- Event log ----

    [Fact]
    public void LogNumbersEventsAndAddsTheCommonFields()
    {
        var log = new EventLog(LogPath, () => _now);
        var logged = log.Append(new PendingEvent(EventNames.TimerStopped, new() { ["Message"] = "m" }));

        Assert.Equal(1, logged.Seq);
        Assert.Equal("SmartLists.TimerStopped", logged.Name);
        var data = logged.Data;
        Assert.Equal(EventNames.ApiLevel, data.GetProperty("ApiLevel").GetInt32());
        Assert.Equal(1, data.GetProperty("Seq").GetInt64());
        Assert.True(Guid.TryParse(data.GetProperty("EventId").GetString(), out _));
        Assert.Equal("2026-10-14T12:00:00Z", data.GetProperty("ServerTimeUtc").GetString());
        Assert.Equal("TimerStopped", data.GetProperty("Event").GetString());
        Assert.Equal("m", data.GetProperty("Message").GetString());
    }

    [Fact]
    public void LogSurvivesARestartAndNumbersKeepGrowing()
    {
        var first = new EventLog(LogPath, () => _now);
        first.Append(new PendingEvent("A", new()));
        first.Append(new PendingEvent("A", new()));

        var second = new EventLog(LogPath, () => _now);
        Assert.Equal(2, second.LatestSeq);
        Assert.Equal(3, second.Append(new PendingEvent("A", new())).Seq);
        Assert.Equal(3, second.After(0, 100, out _).Count);
    }

    [Fact]
    public void LogKeepsOnlyTheLast500AndSaysWhenItIsTruncated()
    {
        var log = new EventLog(LogPath, () => _now);
        for (var i = 0; i < 520; i++)
        {
            log.Append(new PendingEvent("A", new()));
        }

        var all = log.After(0, 1000, out var truncated);
        Assert.True(truncated);
        Assert.Equal(500, all.Count);
        Assert.Equal(21, all[0].Seq);
        Assert.Equal(520, all[^1].Seq);

        log.After(20, 10, out var edge);
        Assert.False(edge); // 21 is the oldest kept, so nothing between 20 and 21 is missing
        log.After(19, 10, out var gone);
        Assert.True(gone);
    }

    [Fact]
    public void AfterReturnsOldestFirstWithinTheLimit()
    {
        var log = new EventLog(LogPath, () => _now);
        for (var i = 0; i < 10; i++)
        {
            log.Append(new PendingEvent("A", new()));
        }

        var page = log.After(3, 4, out var truncated);
        Assert.False(truncated);
        Assert.Equal(new long[] { 4, 5, 6, 7 }, page.Select(e => e.Seq).ToArray());
        Assert.Empty(log.After(10, 100, out _));
    }

    [Fact]
    public void NumbersNeverGoBackAfterADamagedLog()
    {
        var log = new EventLog(LogPath, () => _now);
        for (var i = 0; i < 5; i++)
        {
            log.Append(new PendingEvent("A", new()));
        }

        File.WriteAllText(LogPath, "{ not json");
        var reopened = new EventLog(LogPath, () => _now);
        Assert.Equal(5, reopened.LatestSeq);
        Assert.Equal(6, reopened.Append(new PendingEvent("A", new())).Seq);
        Assert.True(File.Exists(LogPath + ".corrupt"));
    }

    [Fact]
    public void NumbersStartHighWhenEverythingIsLost()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(LogPath, "garbage");
        var log = new EventLog(LogPath, () => _now);
        Assert.True(log.LatestSeq > 1_000_000_000);
    }

    // ---- Catalog, info, settings ----

    [Fact]
    public void CatalogListsTheThreeEventsWithDefaultsThatMatchTheSwitches()
    {
        var catalog = JsonSerializer.SerializeToElement(NotificationCatalog.Build());
        Assert.Equal(EventNames.ApiLevel, catalog.GetProperty("ApiLevel").GetInt32());
        Assert.Equal("SmartLists", catalog.GetProperty("Service").GetProperty("Id").GetString());
        Assert.Equal("smart-lists", catalog.GetProperty("Service").GetProperty("ChannelId").GetString());

        var defaults = new NotificationSwitches();
        var events = catalog.GetProperty("Events").EnumerateArray().ToList();
        Assert.Equal(
            new[] { "SmartLists.RefreshFailed", "SmartLists.RefreshRunFinished", "SmartLists.TimerStopped" },
            events.Select(e => e.GetProperty("Id").GetString()).ToArray());

        foreach (var e in events)
        {
            var shortName = e.GetProperty("Id").GetString()!["SmartLists.".Length..];
            Assert.Equal(defaults.IsOn(shortName), e.GetProperty("DefaultRule").GetProperty("Enabled").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("Title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("LockScreenText").GetString()));
            Assert.Equal(JsonValueKind.Array, e.GetProperty("Params").ValueKind);
        }
    }

    [Fact]
    public void InfoAddsFeaturesAndKeepsTheApiVersion()
    {
        var info = JsonSerializer.SerializeToElement(RuleCatalog.BuildInfo("1.2.3.0"));
        Assert.Equal(1, info.GetProperty("ApiVersion").GetInt32()); // the app requires equality: never change for an addition
        Assert.Equal(EventNames.ApiLevel, info.GetProperty("ApiLevel").GetInt32());
        var features = info.GetProperty("Features").EnumerateArray().Select(f => f.GetString()).ToList();
        Assert.Contains("events", features);
        Assert.Contains("catalog", features);
    }

    [Fact]
    public void PluginConfigurationDefaultsMatchTheContract()
    {
        var config = new PluginConfiguration();
        Assert.True(config.SendPhoneAlerts);
        Assert.True(config.PhoneAlertRefreshFailed);
        Assert.False(config.PhoneAlertRefreshRunFinished);
        Assert.True(config.PhoneAlertTimerStopped);
    }
}

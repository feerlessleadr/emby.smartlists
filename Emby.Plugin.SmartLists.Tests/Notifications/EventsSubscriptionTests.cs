using System.Text.Json;
using Emby.Plugin.SmartLists.Notifications;

namespace Emby.Plugin.SmartLists.Tests.Notifications;

public class EventsSubscriptionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "smartlists-sub-" + Guid.NewGuid().ToString("N"));
    private readonly EventLog _log;

    public EventsSubscriptionTests()
    {
        _log = new EventLog(Path.Combine(_folder, "events.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    private void Add(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _log.Append(new PendingEvent(EventNames.TimerStopped, new() { ["Message"] = "m" }));
        }
    }

    private static List<Dictionary<string, object?>> Events(Dictionary<string, object?> data) => (List<Dictionary<string, object?>>)data["Events"]!;

    [Fact]
    public void WithoutAfterSeqTheFirstPushIsOnlyTheBaseline()
    {
        Add(3);
        var sub = new EventsSubscription();
        sub.ParseStartValues(Array.Empty<string>());

        var first = sub.Next(_log, true)!;
        Assert.Empty(Events(first));
        Assert.Equal(3L, first["LatestSeq"]);
        Assert.Equal(false, first["Truncated"]);
        Assert.Equal(EventNames.ApiLevel, first["ApiLevel"]);
        Assert.Equal(3, sub.LastSentSeq);
    }

    [Fact]
    public void AfterSeqSendsWhatTheAppMissedOldestFirst()
    {
        Add(5);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { "2" });

        var first = sub.Next(_log, true)!;
        Assert.Equal(new long[] { 3, 4, 5 }, Events(first).Select(e => (long)e["Seq"]!).ToArray());
        Assert.Equal("SmartLists.TimerStopped", Events(first)[0]["Name"]);
    }

    [Fact]
    public void NothingIsSentTwiceOnOneConnection()
    {
        Add(2);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { "0" });
        Assert.Equal(2, Events(sub.Next(_log, true)!).Count);

        var heartbeat = sub.Next(_log, true)!;
        Assert.Empty(Events(heartbeat)); // an empty list is the heartbeat
        Assert.Equal(2L, heartbeat["LatestSeq"]);

        Add(1);
        var next = Events(sub.Next(_log, true)!);
        Assert.Equal(3L, Assert.Single(next)["Seq"]);
        Assert.Empty(Events(sub.Next(_log, true)!));
    }

    [Fact]
    public void ConnectionsHaveTheirOwnState()
    {
        Add(1);
        var a = new EventsSubscription();
        a.ParseStartValues(new[] { "0" });
        var b = new EventsSubscription();
        b.ParseStartValues(Array.Empty<string>());

        Assert.Single(Events(a.Next(_log, true)!));
        Assert.Empty(Events(b.Next(_log, true)!));
        Add(1);
        Assert.Single(Events(a.Next(_log, true)!));
        Assert.Single(Events(b.Next(_log, true)!));
    }

    [Fact]
    public void NonAdministratorGetsOneErrorAndNothingMore()
    {
        Add(1);
        var sub = new EventsSubscription();
        var refusal = sub.Next(_log, false)!;
        Assert.Equal(new[] { "Error" }, refusal.Keys.ToArray());
        Assert.Equal("Administrator login required.", refusal["Error"]);
        Assert.Null(sub.Next(_log, false));
        Assert.Null(sub.Next(null, false));
    }

    [Fact]
    public void BeforeThePluginHasStartedAnAdministratorGetsNothingYet()
    {
        Assert.Null(new EventsSubscription().Next(null, true));
    }

    [Fact]
    public void TruncatedIsReportedWhenTheLogNoLongerReachesBack()
    {
        Add(520);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { "5" });
        var first = sub.Next(_log, true)!;
        Assert.Equal(true, first["Truncated"]);
        Assert.Equal(500, Events(first).Count);
    }

    [Fact]
    public void AnAfterSeqAboveTheLatestIsClampedSoNewEventsStillArrive()
    {
        Add(2);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { "9999" });
        Assert.Empty(Events(sub.Next(_log, true)!));
        Add(1);
        Assert.Single(Events(sub.Next(_log, true)!));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("")]
    public void ABadAfterSeqIsIgnored(string value)
    {
        Add(2);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { value });
        Assert.Null(sub.AfterSeq);
        Assert.Empty(Events(sub.Next(_log, true)!));
    }

    [Fact]
    public void PushShapeSerializesAsTheContractSays()
    {
        Add(1);
        var sub = new EventsSubscription();
        sub.ParseStartValues(new[] { "0" });
        var json = JsonSerializer.SerializeToElement(sub.Next(_log, true));

        Assert.Equal(1, json.GetProperty("ApiLevel").GetInt32());
        Assert.Equal(1, json.GetProperty("LatestSeq").GetInt64());
        Assert.False(json.GetProperty("Truncated").GetBoolean());
        var e = json.GetProperty("Events")[0];
        Assert.Equal(1, e.GetProperty("Seq").GetInt64());
        Assert.Equal("SmartLists.TimerStopped", e.GetProperty("Name").GetString());
        Assert.True(e.TryGetProperty("AtUtc", out _));
        Assert.Equal(1, e.GetProperty("Data").GetProperty("Seq").GetInt64());
    }
}

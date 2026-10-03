using System.Text;
using System.Text.Json;
using Emby.Plugin.SmartLists.Api;
using Microsoft.AspNetCore.Mvc;

namespace Emby.Plugin.SmartLists.Tests;

/// <summary>
/// The router stands in for MVC on Emby, so these pin the parts of MVC behaviour the admin API relies on:
/// template matching (literal beats parameter), parameter binding, and the shape of error bodies.
/// </summary>
public class ControllerRouterTests
{
    private sealed class FakeController : ControllerBase
    {
        [HttpGet]
        public ActionResult<string[]> List([FromQuery] string? type = null) => Ok(new[] { "list", type ?? "none" });

        [HttpGet("{id}")]
        public ActionResult<string> Get([FromRoute] string id) => Ok("get:" + id);

        [HttpGet("fields")]
        public ActionResult<string> Fields() => Ok("fields");

        [HttpPost]
        public ActionResult<Dictionary<string, object?>> Create([FromBody] Dictionary<string, object?>? body, [FromQuery] bool skipRefresh = false)
            => Created(string.Empty, new Dictionary<string, object?> { ["skip"] = skipRefresh, ["hasBody"] = body is not null });

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete([FromRoute] Guid id, [FromQuery] bool deleteList = true)
        {
            await Task.Yield();
            return id == Guid.Empty ? NotFound(new { message = "gone" }) : NoContent();
        }

        [HttpGet("boom")]
        public ActionResult Boom() => throw new InvalidOperationException("kaput");
    }

    private static readonly ControllerRouter Router = new(typeof(FakeController), () => new FakeController());

    private static Task<RoutedResponse> Call(string verb, string path, string? body = null, params (string Key, string Value)[] query)
        => Router.InvokeAsync(
            verb,
            path,
            query.ToDictionary(q => q.Key, q => q.Value, StringComparer.OrdinalIgnoreCase),
            null,
            body is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(body)),
            CancellationToken.None);

    private static string Text(RoutedResponse response) => Encoding.UTF8.GetString(response.Body);

    [Fact]
    public async Task LiteralSegmentWinsOverParameter()
    {
        Assert.Equal("\"fields\"", Text(await Call("GET", "fields")));
        Assert.Equal("\"get:abc\"", Text(await Call("GET", "abc")));
    }

    [Fact]
    public async Task EmptyPathMatchesTheRootAction_AndQueryValuesAreBound()
    {
        var response = await Call("GET", string.Empty, null, ("type", "Playlist"));
        Assert.Equal(200, response.Status);
        Assert.Equal("[\"list\",\"Playlist\"]", Text(response));
    }

    [Fact]
    public async Task BodyAndBooleanQueryAreBound_AndCreatedKeepsItsStatus()
    {
        var response = await Call("POST", string.Empty, "{\"a\":1}", ("skipRefresh", "true"));
        Assert.Equal(201, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.True(json.RootElement.GetProperty("skip").GetBoolean());
        Assert.True(json.RootElement.GetProperty("hasBody").GetBoolean());
    }

    [Fact]
    public async Task MissingOptionalBodyBindsNull()
    {
        var response = await Call("POST", string.Empty);
        using var json = JsonDocument.Parse(response.Body);
        Assert.False(json.RootElement.GetProperty("hasBody").GetBoolean());
    }

    [Fact]
    public async Task NoContentAndGuidRouteBinding()
    {
        Assert.Equal(204, (await Call("DELETE", Guid.NewGuid().ToString())).Status);
    }

    [Fact]
    public async Task ErrorBodiesBecomeProblemDetails_WithTheOriginalStatus()
    {
        var response = await Call("DELETE", Guid.Empty.ToString());
        Assert.Equal(404, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("gone", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not Found", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnparseableRouteValueIsABadRequest()
    {
        var response = await Call("DELETE", "not-a-guid");
        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task UnknownPathIs404_AndWrongVerbIs405()
    {
        Assert.Equal(404, (await Call("GET", "a/b/c")).Status);
        Assert.Equal(405, (await Call("PUT", "fields")).Status);
    }

    [Fact]
    public async Task ExceptionsBecomeA500WithTheMessage()
    {
        var response = await Call("GET", "boom");
        Assert.Equal(500, response.Status);
        Assert.Contains("kaput", Text(response), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidsSerializeWithoutDashes()
    {
        var id = Guid.Parse("187e8098-a040-4bf1-a422-00bcf6f9d29f");
        Assert.Equal("\"187e8098a0404bf1a42200bcf6f9d29f\"", JsonSerializer.Serialize(id, ControllerRouter.JsonOptions));
    }
}

using Emby.Plugin.SmartLists.Core.QueryEngine;
using Emby.Plugin.SmartLists.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

// Tests for Api/Filters/SmartListsProblemDetailsAttribute.cs. These tests cover the filter,
// (ExtractDetail is also what the Emby ControllerRouter uses to shape error bodies).
namespace Emby.Plugin.SmartLists.Tests;

#region (B) Api/Filters/SmartListsProblemDetailsAttribute.cs

/// <summary>
/// Blast-radius lockdown for <see cref="SmartListsProblemDetailsAttribute"/>.
/// <para>
/// The filter rewrites this plugin's ad-hoc error bodies into RFC 7807 ProblemDetails. The
/// risk is not that it rewrites too little - it is that it rewrites something it should not
/// (a 200 body the UI reads, another controller's response, an already-shaped ProblemDetails).
/// Most tests below therefore assert that NOTHING happened.
/// </para>
/// <para>
/// No MVC host is needed: the context is assembled by hand and the single DI dependency
/// (<see cref="ProblemDetailsFactory"/>) is a hand-written recording stub, so what the filter
/// asked the factory for is directly observable.
/// </para>
/// </summary>
public class SmartListsProblemDetailsAttributeTests
{
    /// <summary>
    /// The filter's first gate compares <c>context.Controller.GetType().Assembly</c> against the
    /// plugin assembly, so the stand-in only has to be *any* instance of a plugin type - Operand
    /// is the cheapest one with no dependencies.
    /// </summary>
    private static object PluginAssemblyController => new Operand("stand-in for a SmartLists controller");

    private sealed class RecordingProblemDetailsFactory : ProblemDetailsFactory
    {
        public int CallCount { get; private set; }

        public HttpContext? LastHttpContext { get; private set; }

        public int? LastStatusCode { get; private set; }

        public string? LastDetail { get; private set; }

        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
        {
            CallCount++;
            LastHttpContext = httpContext;
            LastStatusCode = statusCode;
            LastDetail = detail;

            // Mirrors what DefaultProblemDetailsFactory does with these two arguments.
            return new ProblemDetails { Status = statusCode, Detail = detail, Title = title };
        }

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
            => throw new NotSupportedException("The filter must never take the validation-problem path.");
    }

    private sealed class SingleServiceProvider(object? problemDetailsFactory) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ProblemDetailsFactory) ? problemDetailsFactory : null;
    }

    private static ResultExecutingContext CreateContext(
        IActionResult result,
        object? controller,
        ProblemDetailsFactory? factory)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new SingleServiceProvider(factory)
        };

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor { ControllerName = "SmartList", ActionName = "GetSmartLists" },
            new ModelStateDictionary());

        return new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller!);
    }

    /// <summary>Runs the filter over a result produced by one of this plugin's controllers.</summary>
    private static (ResultExecutingContext Context, RecordingProblemDetailsFactory Factory) Run(IActionResult result)
    {
        var factory = new RecordingProblemDetailsFactory();
        var context = CreateContext(result, PluginAssemblyController, factory);

        new SmartListsProblemDetailsAttribute().OnResultExecuting(context);

        return (context, factory);
    }

    private static ProblemDetails AssertRewritten(ResultExecutingContext context)
    {
        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);

        // Content negotiation must know it is now serializing a ProblemDetails.
        Assert.Equal(typeof(ProblemDetails), result.DeclaredType);
        return problem;
    }

    // --- rewrite cases -----------------------------------------------------------------

    [Fact]
    public void OnResultExecuting_ErrorBodyWithMessageProperty_BecomesProblemDetailsCarryingTheText()
    {
        var (context, factory) = Run(new BadRequestObjectResult(new { message = "Rule 3 has no value" }));

        var problem = AssertRewritten(context);
        Assert.Equal("Rule 3 has no value", problem.Detail);
        Assert.Equal(400, problem.Status);

        Assert.Equal(1, factory.CallCount);
        Assert.Equal(400, factory.LastStatusCode);
        Assert.Equal("Rule 3 has no value", factory.LastDetail);
        Assert.Same(context.HttpContext, factory.LastHttpContext);
    }

    [Fact]
    public void OnResultExecuting_ErrorBodyWithErrorProperty_BecomesProblemDetailsCarryingTheText()
    {
        var (context, _) = Run(new ObjectResult(new { error = "Playlist not found" }) { StatusCode = 404 });

        var problem = AssertRewritten(context);
        Assert.Equal("Playlist not found", problem.Detail);
        Assert.Equal(404, problem.Status);
    }

    /// <summary>
    /// Five call sites return <c>new { message, error = ex.Message }</c>. Only <c>message</c> is
    /// carried across - the raw exception text is dropped on purpose.
    /// </summary>
    [Fact]
    public void OnResultExecuting_BodyWithBothMessageAndError_KeepsMessageAndDropsError()
    {
        var (context, _) = Run(new ObjectResult(new { message = "Failed to refresh list", error = "System.IO.IOException: disk full" })
        {
            StatusCode = 500
        });

        Assert.Equal("Failed to refresh list", AssertRewritten(context).Detail);
    }

    /// <summary>Covers the <c>BadRequest("Invalid list ID format")</c> style of return.</summary>
    [Fact]
    public void OnResultExecuting_BareStringBody_BecomesProblemDetailsCarryingTheString()
    {
        var (context, _) = Run(new BadRequestObjectResult("Invalid list ID format"));

        Assert.Equal("Invalid list ID format", AssertRewritten(context).Detail);
    }

    /// <summary>
    /// A non-string <c>message</c> is not a recognised error text, so the filter falls through to
    /// <c>error</c> rather than stringifying it.
    /// </summary>
    [Fact]
    public void OnResultExecuting_NonStringMessageProperty_FallsBackToErrorProperty()
    {
        var (context, _) = Run(new ObjectResult(new { message = 42, error = "Real text" }) { StatusCode = 409 });

        Assert.Equal("Real text", AssertRewritten(context).Detail);
    }

    /// <summary>
    /// The status code is never altered - not on the result, not in what the factory is told, and
    /// not in the body it produces.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(500)]
    [InlineData(503)]
    public void OnResultExecuting_Rewriting_PreservesTheStatusCodeExactly(int status)
    {
        var (context, factory) = Run(new ObjectResult(new { message = "nope" }) { StatusCode = status });

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(status, factory.LastStatusCode);
        Assert.Equal(status, Assert.IsType<ProblemDetails>(result.Value).Status);
    }

    // --- pass-through cases (the blast radius) -----------------------------------------

    /// <summary>
    /// The UI reads <c>message</c> out of 2xx bodies to show notifications. Rewriting those would
    /// silently break every success toast, so the 400 threshold is load-bearing.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(302)]
    [InlineData(399)]
    public void OnResultExecuting_StatusBelow400_LeavesTheBodyUntouched(int status)
    {
        var body = new { message = "Smart list created" };
        var (context, factory) = Run(new ObjectResult(body) { StatusCode = status });

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Same(body, result.Value);
        Assert.Null(result.DeclaredType);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_OkObjectResultWithMessage_LeavesTheBodyUntouched()
    {
        var body = new { message = "Refresh queued" };
        var (context, factory) = Run(new OkObjectResult(body));

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_ValueAlreadyProblemDetails_IsNotDoubleWrapped()
    {
        var body = new ProblemDetails { Status = 404, Title = "Not Found", Detail = "Already shaped" };
        var (context, factory) = Run(new ObjectResult(body) { StatusCode = 404 });

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_ValueAlreadyValidationProblemDetails_IsNotDoubleWrapped()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Name", "The Name field is required.");
        var body = new ValidationProblemDetails(modelState) { Status = 400 };

        var (context, factory) = Run(new ObjectResult(body) { StatusCode = 400 });

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    /// <summary>
    /// An error status with a payload that is not one of the three known error shapes is left
    /// exactly as the controller wrote it - the filter never invents a detail.
    /// </summary>
    [Fact]
    public void OnResultExecuting_UnrecognisedValueShape_PassesThroughUnchanged()
    {
        var body = new { failedIds = new[] { "a", "b" }, processed = 7 };
        var (context, factory) = Run(new ObjectResult(body) { StatusCode = 500 });

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Same(body, result.Value);
        Assert.Null(result.DeclaredType);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_MessagePropertyThatIsNotAString_PassesThroughUnchanged()
    {
        var body = new { message = 42 };
        var (context, factory) = Run(new ObjectResult(body) { StatusCode = 400 });

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_NullValue_PassesThroughUnchanged()
    {
        var (context, factory) = Run(new ObjectResult(null) { StatusCode = 500 });

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Null(result.Value);
        Assert.Null(result.DeclaredType);
        Assert.Equal(0, factory.CallCount);
    }

    /// <summary>
    /// Only ObjectResult is considered. A JsonResult carrying the very same body is a near-miss
    /// that must survive untouched.
    /// </summary>
    [Fact]
    public void OnResultExecuting_NonObjectResult_IsNotTouched()
    {
        var result = new JsonResult(new { message = "Failed" }) { StatusCode = 500 };
        var (context, factory) = Run(result);

        Assert.Same(result, context.Result);
        Assert.Equal(0, factory.CallCount);
    }

    /// <summary>
    /// An ObjectResult with no explicit status code (MVC decides it later) is out of scope - the
    /// filter cannot know whether it will end up an error.
    /// </summary>
    [Fact]
    public void OnResultExecuting_ObjectResultWithoutStatusCode_IsNotTouched()
    {
        var body = new { message = "Failed" };
        var (context, factory) = Run(new ObjectResult(body));

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    /// <summary>
    /// THE blast-radius test. Should this filter ever be added to MvcOptions.Filters globally, it
    /// must still refuse to touch a response authored by a controller outside this plugin.
    /// </summary>
    [Fact]
    public void OnResultExecuting_ControllerFromAnotherAssembly_IsNotTouched()
    {
        var body = new { message = "A core Emby error body" };
        var factory = new RecordingProblemDetailsFactory();
        // System.Object lives in System.Private.CoreLib, i.e. not the plugin assembly.
        var context = CreateContext(new ObjectResult(body) { StatusCode = 400 }, new object(), factory);

        new SmartListsProblemDetailsAttribute().OnResultExecuting(context);

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Same(body, result.Value);
        Assert.Null(result.DeclaredType);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public void OnResultExecuting_NullController_IsNotTouched()
    {
        var body = new { message = "Failed" };
        var factory = new RecordingProblemDetailsFactory();
        var context = CreateContext(new ObjectResult(body) { StatusCode = 400 }, null, factory);

        new SmartListsProblemDetailsAttribute().OnResultExecuting(context);

        Assert.Same(body, Assert.IsAssignableFrom<ObjectResult>(context.Result).Value);
        Assert.Equal(0, factory.CallCount);
    }

    /// <summary>
    /// If ProblemDetailsFactory is somehow not registered, the controller's own body ships as-is
    /// rather than the request failing.
    /// </summary>
    [Fact]
    public void OnResultExecuting_ProblemDetailsFactoryNotRegistered_LeavesTheBodyUnchanged()
    {
        var body = new { message = "Failed" };
        var context = CreateContext(new ObjectResult(body) { StatusCode = 400 }, PluginAssemblyController, factory: null);

        new SmartListsProblemDetailsAttribute().OnResultExecuting(context);

        var result = Assert.IsAssignableFrom<ObjectResult>(context.Result);
        Assert.Same(body, result.Value);
        Assert.Null(result.DeclaredType);
    }
}

#endregion

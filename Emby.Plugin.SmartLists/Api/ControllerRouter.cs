using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.SmartLists.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Emby.Plugin.SmartLists.Api
{
    /// <summary>
    /// The parsed parts of a multipart request.
    /// </summary>
    /// <param name="Files">Uploaded files.</param>
    /// <param name="Form">Plain form fields by name.</param>
    public sealed record RequestUploads(IReadOnlyList<IFormFile> Files, IReadOnlyDictionary<string, string> Form);

    /// <summary>
    /// What an endpoint produced, in terms the Emby response layer can write.
    /// </summary>
    /// <param name="Status">HTTP status code.</param>
    /// <param name="ContentType">Response content type.</param>
    /// <param name="Body">Response body bytes (empty for no content).</param>
    /// <param name="FileName">Download file name, if the result was a file.</param>
    public sealed record RoutedResponse(int Status, string ContentType, byte[] Body, string? FileName = null);

    /// <summary>
    /// Dispatches HTTP requests to the attribute-routed methods of a controller class. Emby does not host ASP.NET MVC,
    /// so this reads the <c>[HttpGet]</c>/<c>[HttpPost]</c>/... templates and parameter binding attributes the
    /// controller already carries and applies them to Emby's wildcard endpoint.
    /// </summary>
    public sealed class ControllerRouter
    {
        /// <summary>
        /// JSON options for request and response bodies: property names are left as declared (the web UI reads
        /// Id/Name), reading is case-insensitive, enums are strings, and GUIDs use the dashless form that
        /// Emby itself emits.
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private readonly Func<object> _controllerFactory;
        private readonly List<Route> _routes = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="ControllerRouter"/> class.
        /// </summary>
        /// <param name="controllerType">The controller type whose actions are served.</param>
        /// <param name="controllerFactory">Creates the controller instance used for one request.</param>
        public ControllerRouter(Type controllerType, Func<object> controllerFactory)
        {
            ArgumentNullException.ThrowIfNull(controllerType);
            _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));

            foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    foreach (var verb in attribute.HttpMethods)
                    {
                        _routes.Add(new Route(verb, attribute.Template ?? string.Empty, method));
                    }
                }
            }
        }

        /// <summary>
        /// Gets the number of routes that were discovered.
        /// </summary>
        public int RouteCount => _routes.Count;

        /// <summary>
        /// Runs the matching action.
        /// </summary>
        /// <param name="verb">The HTTP method.</param>
        /// <param name="path">The path below the route prefix.</param>
        /// <param name="query">The query string values.</param>
        /// <param name="uploads">Files and form fields of a multipart request, if any.</param>
        /// <param name="body">The request body stream.</param>
        /// <param name="cancellationToken">The request cancellation token.</param>
        /// <returns>The response to write.</returns>
        public async Task<RoutedResponse> InvokeAsync(
            string verb,
            string path,
            IReadOnlyDictionary<string, string> query,
            RequestUploads? uploads,
            Stream? body,
            CancellationToken cancellationToken)
        {
            var segments = SplitPath(path);
            Route? matched = null;
            Dictionary<string, string>? routeValues = null;
            var pathMatched = false;

            foreach (var route in _routes.OrderByDescending(r => r.LiteralCount))
            {
                if (!route.TryMatch(segments, out var values))
                {
                    continue;
                }

                pathMatched = true;
                if (!string.Equals(route.Verb, verb, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                matched = route;
                routeValues = values;
                break;
            }

            if (matched is null)
            {
                return Problem(pathMatched ? 405 : 404, pathMatched ? "Method not allowed" : "No such endpoint: " + path);
            }

            try
            {
                
                var (values, error) = await BindAsync(matched.Method, routeValues!, query, uploads, body, cancellationToken).ConfigureAwait(false);
                if (error is not null)
                {
                    return Problem(400, error);
                }

                var returned = matched.Method.Invoke(_controllerFactory(), values);
                if (returned is Task task)
                {
                    await task.ConfigureAwait(false);
                    returned = task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
                }

                return await ToResponseAsync(returned, cancellationToken).ConfigureAwait(false);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                return Problem(500, ex.InnerException.Message);
            }
            catch (JsonException ex)
            {
                return Problem(400, "Invalid request body: " + ex.Message);
            }
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            };
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new DashlessGuidConverter());
            return options;
        }

        private static string[] SplitPath(string? path)
            => (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

        private static RoutedResponse Json(int status, object? value)
            => new(status, "application/json", value is null ? [] : JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), JsonOptions));

        private static RoutedResponse Problem(int status, string detail)
            => Json(status, new Dictionary<string, object?>
            {
                ["title"] = ReasonPhrase(status),
                ["status"] = status,
                ["detail"] = detail,
            });

        private static string ReasonPhrase(int status)
            => Enum.IsDefined(typeof(HttpStatusCode), status)
                ? Regex.Replace(((HttpStatusCode)status).ToString(), "(?<=[a-z])(?=[A-Z])", " ")
                : "Error";

        private static async Task<RoutedResponse> ToResponseAsync(object? returned, CancellationToken cancellationToken)
        {
            if (returned is IConvertToActionResult convertible)
            {
                returned = convertible.Convert();
            }

            switch (returned)
            {
                case null:
                    return new RoutedResponse(204, "application/json", []);
                case ObjectResult objectResult:
                {
                    var status = objectResult.StatusCode ?? 200;
                    if (status >= 400 && objectResult.Value is not ProblemDetails)
                    {
                        var detail = SmartListsProblemDetailsAttribute.ExtractDetail(objectResult.Value);
                        if (detail is not null)
                        {
                            return Problem(status, detail);
                        }
                    }

                    return Json(status, objectResult.Value);
                }

                case ContentResult content:
                    return new RoutedResponse(content.StatusCode ?? 200, content.ContentType ?? "text/plain", Encoding.UTF8.GetBytes(content.Content ?? string.Empty));
                case FileContentResult file:
                    return new RoutedResponse(200, file.ContentType, file.FileContents, file.FileDownloadName);
                case FileStreamResult stream:
                {
                    using var buffer = new MemoryStream();
                    await stream.FileStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    await stream.FileStream.DisposeAsync().ConfigureAwait(false);
                    return new RoutedResponse(200, stream.ContentType, buffer.ToArray(), stream.FileDownloadName);
                }

                case PhysicalFileResult physical:
                    return new RoutedResponse(200, physical.ContentType, await File.ReadAllBytesAsync(physical.FileName, cancellationToken).ConfigureAwait(false), physical.FileDownloadName);
                case StatusCodeResult statusCode:
                    return new RoutedResponse(statusCode.StatusCode, "application/json", []);
                default:
                    return Problem(500, "Unsupported result type " + returned.GetType().Name);
            }
        }

        private static object? DefaultFor(ParameterInfo parameter)
            => parameter.HasDefaultValue
                ? parameter.DefaultValue
                : (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null
                    ? Activator.CreateInstance(parameter.ParameterType)
                    : null);

        private static object? ConvertText(string text, Type type)
        {
            var target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(string))
            {
                return text;
            }

            if (target == typeof(Guid))
            {
                return Guid.Parse(text);
            }

            if (target.IsEnum)
            {
                return Enum.Parse(target, text, ignoreCase: true);
            }

            if (target == typeof(bool))
            {
                return bool.Parse(text);
            }

            return Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
        }

        private static async Task<byte[]> ReadAllAsync(Stream? body, CancellationToken cancellationToken)
        {
            if (body is null)
            {
                return [];
            }

            using var buffer = new MemoryStream();
            await body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }

        private static async Task<(object?[] Values, string? Error)> BindAsync(
            MethodInfo method,
            Dictionary<string, string> routeValues,
            IReadOnlyDictionary<string, string> query,
            RequestUploads? uploads,
            Stream? body,
            CancellationToken cancellationToken)
        {
            var parameters = method.GetParameters();
            var values = new object?[parameters.Length];
            byte[]? bodyBytes = null;
            IReadOnlyList<IFormFile>? files = null;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var name = parameter.Name!;

                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    values[i] = cancellationToken;
                    continue;
                }

                if (parameter.ParameterType == typeof(IFormFile))
                {
                    files ??= uploads?.Files ?? [];
                    values[i] = files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ?? (files.Count > 0 ? files[0] : null);
                    if (values[i] is null)
                    {
                        return (values, "Missing file upload");
                    }

                    continue;
                }

                if (parameter.GetCustomAttribute<FromBodyAttribute>() is not null)
                {
                    bodyBytes ??= await ReadAllAsync(body, cancellationToken).ConfigureAwait(false);
                    values[i] = bodyBytes.Length == 0 ? null : JsonSerializer.Deserialize(bodyBytes, parameter.ParameterType, JsonOptions);
                    continue;
                }

                if (parameter.GetCustomAttribute<FromFormAttribute>() is not null)
                {
                    files ??= uploads?.Files ?? [];
                    values[i] = (uploads?.Form.TryGetValue(name, out var formValue) ?? false) ? ConvertText(formValue, parameter.ParameterType) : DefaultFor(parameter);
                    continue;
                }

                string? text = null;
                if (routeValues.TryGetValue(name, out var routeText))
                {
                    text = Uri.UnescapeDataString(routeText);
                }
                else if (query.TryGetValue(name, out var queryText))
                {
                    text = queryText;
                }

                if (text is null)
                {
                    values[i] = DefaultFor(parameter);
                    continue;
                }

                try
                {
                    values[i] = ConvertText(text, parameter.ParameterType);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                {
                    return (values, "Invalid value for '" + name + "'");
                }
            }

            return (values, null);
        }


        private sealed class Route
        {
            private readonly string[] _template;

            public Route(string verb, string template, MethodInfo method)
            {
                Verb = verb;
                Method = method;
                _template = SplitPath(template);
                LiteralCount = _template.Count(s => !IsParameter(s));
            }

            public string Verb { get; }

            public MethodInfo Method { get; }

            public int LiteralCount { get; }

            public bool TryMatch(string[] segments, out Dictionary<string, string> values)
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (segments.Length != _template.Length)
                {
                    return false;
                }

                for (var i = 0; i < _template.Length; i++)
                {
                    if (IsParameter(_template[i]))
                    {
                        // "{id}" or "{id:guid}": the name is everything before an optional constraint.
                        var name = _template[i].Trim('{', '}').Split(':')[0];
                        values[name] = segments[i];
                    }
                    else if (!string.Equals(_template[i], segments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool IsParameter(string segment) => segment.StartsWith('{') && segment.EndsWith('}');
        }

        private sealed class DashlessGuidConverter : JsonConverter<Guid>
        {
            public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
                => Guid.Parse(reader.GetString()!);

            public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.ToString("N"));
        }
    }
}

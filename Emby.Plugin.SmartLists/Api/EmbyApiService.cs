using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.SmartLists.Api.Controllers;
using Emby.Plugin.SmartLists.Host;
using Microsoft.AspNetCore.Http;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.SmartLists.Api
{
    /// <summary>
    /// Catch-all request for every SmartLists admin endpoint. The actual routing happens in
    /// <see cref="ControllerRouter"/> from the controller's own attributes, so adding an action to
    /// <see cref="SmartListController"/> needs no change here.
    /// </summary>
    [Route("/Plugins/SmartLists/{Path*}", "GET,POST,PUT,DELETE", Summary = "SmartLists admin API")]
    [Authenticated(Roles = "Admin")]
    public class SmartListsApiRequest : IReturn<object>, IRequiresRequestStream
    {
        /// <summary>
        /// Gets or sets the path below /Plugins/SmartLists.
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the raw request body.
        /// </summary>
        public Stream? RequestStream { get; set; }
    }

    /// <summary>
    /// Emby endpoint that serves the SmartLists admin API.
    /// </summary>
    public class EmbyApiService : IService, IRequiresRequest
    {
        private static readonly AsyncLocal<Guid> Caller = new();
        private static readonly Lazy<ControllerRouter> Router = new(() => new ControllerRouter(
            typeof(SmartListController),
            () => new SmartListController(SmartListsHost.Instance ?? throw new InvalidOperationException("SmartLists is not running yet"))
            {
                CallerUserId = Caller.Value,
            }));

        private readonly IHttpResultFactory _resultFactory;
        private readonly IAuthorizationContext _authorizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbyApiService"/> class.
        /// </summary>
        /// <param name="resultFactory">Emby's response factory.</param>
        /// <param name="authorizationContext">Emby's request authorization context.</param>
        public EmbyApiService(IHttpResultFactory resultFactory, IAuthorizationContext authorizationContext)
        {
            _resultFactory = resultFactory;
            _authorizationContext = authorizationContext;
        }

        /// <inheritdoc />
        public IRequest Request { get; set; } = null!;

        public Task<object> Get(SmartListsApiRequest request) => HandleAsync("GET", request);

        public Task<object> Post(SmartListsApiRequest request) => HandleAsync("POST", request);

        public Task<object> Put(SmartListsApiRequest request) => HandleAsync("PUT", request);

        public Task<object> Delete(SmartListsApiRequest request) => HandleAsync("DELETE", request);

        private async Task<object> HandleAsync(string verb, SmartListsApiRequest request)
        {
            Caller.Value = _authorizationContext.GetAuthorizationInfo(Request).User?.Id ?? Guid.Empty;

            var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in Request.QueryString.Keys.Cast<string>().Where(k => k is not null))
            {
                query[key] = Request.QueryString[key] ?? string.Empty;
            }

            // Emby parses multipart bodies itself, which consumes the stream: take the files and fields it produced.
            RequestUploads? uploads = null;
            if (Request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true)
            {
                var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var formData = await Request.GetFormData().ConfigureAwait(false);
                foreach (var key in formData.Keys.Where(k => k is not null))
                {
                    form[key] = formData[key] ?? string.Empty;
                }

                var files = (Request.Files ?? []).Select(f => (IFormFile)new FormFile(f.InputStream, 0, f.ContentLength, f.Name, f.FileName)
                {
                    Headers = new HeaderDictionary { ["Content-Type"] = f.ContentType ?? "application/octet-stream" },
                    ContentType = f.ContentType ?? "application/octet-stream",
                }).ToList();
                uploads = new RequestUploads(files, form);
            }

            var response = await Router.Value.InvokeAsync(
                verb,
                request.Path ?? string.Empty,
                query,
                uploads,
                request.RequestStream,
                Request.CancellationToken).ConfigureAwait(false);

            Request.Response.StatusCode = response.Status;
            var headers = new Dictionary<string, string>();
            if (response.FileName is not null)
            {
                headers["Content-Disposition"] = "attachment; filename=\"" + response.FileName.Replace("\"", string.Empty, StringComparison.Ordinal) + "\"";
            }

            return _resultFactory.GetResult(Request, new ReadOnlyMemory<byte>(response.Body), response.ContentType, headers);
        }
    }
}

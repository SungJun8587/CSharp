using Common.Lib;
using DotNetWebAPI.Model;
using DotNetWebAPI.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Common.Middleware
{
    public class LogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HttpLoggerOption _options;
        private readonly IHttpLogger _logger;

        public LogMiddleware(RequestDelegate next, IOptions<HttpLoggerOption> options, IHttpLogger logger)
        {
            _next = next;
            _options = options.Value;
            _logger = logger;
        }

        public async Task Invoke(HttpContext httpContext, IHttpLogModelCreator logCreator)
        {
            HttpLogModel log = logCreator.LogModel;
            // Middleware is enabled only when the EnableRequestResponseLogging config value is set.
            if (_options == null || !_options.IsEnabled)
            {
                await _next(httpContext);
                return;
            }
            log.Request.DateTime = SgTime.I.NowDateTime;
            HttpRequest request = httpContext.Request;

            // Log Base
            log.LogId = Guid.NewGuid().ToString();
            log.TraceId = httpContext.TraceIdentifier;

            log.ClientIP = GlobalFunc.GetUserIP();
            log.Node = _options.Name;

            // Request
            log.Request.Method = request.Method;
            log.Request.Path = request.Path;
            log.Request.Query = request.QueryString.ToString();
            log.Request.Queries = GlobalFunc.FormatQueries(request.QueryString.ToString());
            log.Request.Headers = GlobalFunc.FormatHeaders(request.Headers);
            log.Request.Body = await ReadBodyFromRequest(request);
            log.Request.Scheme = request.Scheme;
            log.Request.Host = request.Host.ToString();
            log.Request.ContentType = request.ContentType; 

            HttpResponse response = httpContext.Response;
            var originalResponseBody = response.Body;
            using var newResponseBody = new MemoryStream();
            response.Body = newResponseBody;                       

            // Call the next middleware in the pipeline
            try
            {
                await _next(httpContext);
            }
            catch (Exception exception)
            {
                /*exception: but was not managed at app.UseExceptionHandler() or by any middleware*/
                LogError(log.Exception, exception);
            }

            newResponseBody.Seek(0, SeekOrigin.Begin);
            var responseBodyText = await new StreamReader(response.Body).ReadToEndAsync();

            newResponseBody.Seek(0, SeekOrigin.Begin);
            await newResponseBody.CopyToAsync(originalResponseBody);

            // Response
            log.Response.ContentType = response.ContentType;
            log.Response.Status = response.StatusCode.ToString();
            log.Response.Headers = GlobalFunc.FormatHeaders(response.Headers);
            log.Response.Body = responseBodyText;
            log.Response.DateTime = SgTime.I.NowDateTime;

            /*exception: but was managed at app.UseExceptionHandler() or by any middleware*/
            var contextFeature = httpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (contextFeature != null && contextFeature.Error != null)
            {
                Exception exception = contextFeature.Error;
                LogError(log.Exception, exception);
            }

            _logger.Log(logCreator);
        }

        private void LogError(CHttpException log, Exception exception)
        {
            log.Source = exception.Source;
            log.Message = exception.Message;
            log.StackTrace = exception.StackTrace;
        }

        private async Task<string> ReadBodyFromRequest(HttpRequest request)
        {
            // Ensure the request's body can be read multiple times (for the next middlewares in the pipeline).
            request.EnableBuffering();
            using var streamReader = new StreamReader(request.Body, leaveOpen: true);
            var requestBody = await streamReader.ReadToEndAsync();
            // Reset the request's body stream position for next middleware in the pipeline.
            request.Body.Position = 0;
            return requestBody;
        }
    }
}
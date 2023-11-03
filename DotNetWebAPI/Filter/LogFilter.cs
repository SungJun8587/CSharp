using Common.Lib;
using DotNetWebAPI.Model;
using DotNetWebAPI.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Common.Filter
{
    [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
    public class HttpLoggerActionFilter : Attribute, IActionFilter
    {
        private HttpLogModel GetLogModel(HttpContext context)
        {
            return context.RequestServices.GetService<IHttpLogModelCreator>().LogModel;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var model = GetLogModel(context.HttpContext);
            model.Request.DateTimeActionLevel = DateTime.Now;
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            var model = GetLogModel(context.HttpContext);
            model.Response.DateTimeActionLevel = SgTime.I.NowDateTime;
        }
    }

    [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
    public class HttpLoggerErrorFilter : Attribute, IExceptionFilter
    {
        private HttpLogModel GetLogModel(HttpContext context)
        {
            return context.RequestServices.GetService<IHttpLogModelCreator>().LogModel;
        }

        public void OnException(ExceptionContext context)
        {
            var model = GetLogModel(context.HttpContext);
            model.Exception.IsActionLevel = true;
            if (model.Response.DateTimeActionLevel == null)
            {
                model.Response.DateTimeActionLevel = SgTime.I.NowDateTime;
            }
        }
    }
}

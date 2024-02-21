using Microsoft.AspNetCore.Mvc.Filters;

namespace Server
{
    public class ApiTaskFilter : IAsyncActionFilter
    {
        private readonly SgTask _sgTask;

        public ApiTaskFilter(SgTask hubTask)
        {
            _sgTask = hubTask;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // Do something before the action executes.
            var errStr = await _sgTask.InvokeTask(async () =>
            {
                await next();
            });

            if (string.IsNullOrEmpty(errStr) == false)
            {
                throw new Exception(errStr);
            }
        }
    }
}

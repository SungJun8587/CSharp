using Microsoft.AspNetCore.SignalR;

namespace Server
{
    public class PacketTaskFilter : IHubFilter
    {
        private readonly SgTask _sgTask;

        public PacketTaskFilter(SgTask hubTask)
        {
            _sgTask = hubTask;
        }

        public async ValueTask<object> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object>> next)
        {
            //Console.WriteLine($"Calling hub method '{invocationContext.HubMethodName}'");
            object result = null;
            var errStr = await _sgTask.InvokeTask(async () =>
            {
                result = await next(invocationContext);
            });

            if (string.IsNullOrEmpty(errStr) == false)
            {
                throw new HubException(errStr);
            }

            return result;
        }
    }
}

using Common.Lib;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace Server
{
    public class SgTask : BackgroundService
    {
        private readonly Channel<Func<Task>> _channel;
        private readonly ILoggerService _logger;

        public SgTask(ILoggerService logger)
        {
            _logger = logger;
            _channel = Channel.CreateUnbounded<Func<Task>>(
                // 기본값이다(여러 Reader, 여러 Writer)
                new UnboundedChannelOptions() { SingleReader = false, SingleWriter = false }
                );

            Start();
        }

        // 채널에 Task를 저장 Release 버전
        public Task<string> InvokeTask(Func<Task> invoke)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;
            var box = async () =>
            {
                string result = "";
                try
                {
                    await invoke();
                }
                catch (HubException ex)
                {
                    result = ex.ToString();
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                }
                finally
                {
                    tcs.SetResult(result);
                }
            };

            // Producer
            var sw = new SpinWait();
            while (!_channel.Writer.TryWrite(box)) sw.SpinOnce();
            return task;
        }

        // 채널에 Task를 저장 Debug 버전
        public Task<string> DebugInvokeTask(ulong playerNo, Func<Task> invoke)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;
            var box = async () =>
            {
                string result = "";
                try
                {
                    await invoke();
                }
                catch (HubException ex)
                {
                    result = ex.ToString();
                    if (ConfigData.IsDebug)
                    {
                        LogContainer logContainer = new LogContainer();
                        logContainer.globalLogs.Add(new Log_GameHubError
                        {
                            PlayerNo = playerNo,
                            Source = ex.Source,
                            Message = ex.Message,
                            StackTrace = ex.StackTrace,
                            ServerGroupNo = ConfigData.ServerGroupNo,
                            ServerChannelNo = ConfigData.ServerChannelNo,
                            InsertDateTime = SgTime.I.NowDateTime
                        });
                        _logger.Add(logContainer);
                    }
                }
                catch (Exception ex)
                {
                    result = ex.ToString();
                    if (ConfigData.IsDebug)
                    {
                        LogContainer logContainer = new LogContainer();
                        logContainer.globalLogs.Add(new Log_GameHubError
                        {
                            PlayerNo = playerNo,
                            Source = ex.Source,
                            Message = ex.Message,
                            StackTrace = ex.StackTrace,
                            ServerGroupNo = ConfigData.ServerGroupNo,
                            ServerChannelNo = ConfigData.ServerChannelNo,
                            InsertDateTime = SgTime.I.NowDateTime
                        });
                        _logger.Add(logContainer);
                    }
                }
                finally
                {
                    tcs.SetResult(result);
                }
            };

            // Producer
            var sw = new SpinWait();
            while (!_channel.Writer.TryWrite(box)) sw.SpinOnce();
            return task;
        }

        private async Task Run(int concurrency, Func<Task> action)
        {
            var tasks = new List<Task>();
            for (var i = 0; i < concurrency; i++)
            {
                tasks.Add(action.Invoke());
            }
            await Task.WhenAll(tasks);
        }

        public void Start()
        {
            List<Task> tasks = new List<Task>();

            int hashCount = (Environment.ProcessorCount * ConfigData.HashMultiple);
            //int hashCount = (Environment.ProcessorCount);

            tasks.Add(Run(hashCount, async () =>
            {
                // Consumer
                while (await _channel.Reader.WaitToReadAsync())
                {
                    while (_channel.Reader.TryRead(out var cb))
                    {
                        // Task 실행
                        await cb();
                    }
                }
            }));

            //await Task.WhenAll(tasks);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.CompletedTask;
        }
    }
}

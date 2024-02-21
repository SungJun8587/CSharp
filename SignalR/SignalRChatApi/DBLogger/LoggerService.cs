using MySqlConnector;
using System.Data;

using System.Threading.Channels;

namespace Server
{
    /// <summary>
    /// Channel에 들어갈 컨테이너
    /// </summary>
    public class LogContainer
    {
        public List<GlobalLogDBBase> globalLogs = new List<GlobalLogDBBase>();
        public List<GameLogDBBase> gameLogs = new List<GameLogDBBase>();
    }

    /// <summary>
    /// Channel에서 꺼내서, 테이블별로 분류된 DataTable
    /// </summary>
    public class LogDataTable
    {
        public Dictionary<string, DataTable> globalTables = new Dictionary<string, DataTable>();
        public Dictionary<string, DataTable> gameTables = new Dictionary<string, DataTable>();
    }

    public class LoggerService : BackgroundService, ILoggerService
    {
        private readonly Channel<LogContainer> _queue;

        private readonly LogDataTable _dataTables;

        private const int MAX_BATCH_SIZE = 100;

        public LoggerService()
        {
            _queue = Channel.CreateUnbounded<LogContainer>();
            _dataTables = new LogDataTable();

        }

        public void Add(LogContainer logContainer)
        {
            _queue.Writer.TryWrite(logContainer);
        }

        public async override Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 기다리면서, 큐에 들어오면 _dataTables.tables 에 채워진다
                    await WaitFillDataTable(stoppingToken);

                    //Console.WriteLine($"Got a batch with {_dataTables.tables.Count}(s) log messages. Bulk inserting them now.");

                    // GameLog
                    foreach(var it in _dataTables.gameTables)
                    {
                        await BulkInsertWithRetries(ConfigData.GameLogDB, it.Value);
                    }

                    // GlobalLog
                    foreach (var it in _dataTables.globalTables)
                    {
                        await BulkInsertWithRetries(ConfigData.GlobalLogDB, it.Value);
                    }                    
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("Stopping token was canceled, which means the service is shutting down.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private async Task WaitFillDataTable(CancellationToken cancellationToken)
        {
            // 대기
            await _queue.Reader.WaitToReadAsync(cancellationToken);

            // 초기화 후
            _dataTables.gameTables.Clear();
            _dataTables.globalTables.Clear();
            
            // MAX_BATCH_SIZE  이하로 읽을 수 있는만큼 읽는다
            int readCount = 0;
            while (readCount < MAX_BATCH_SIZE && _queue.Reader.TryRead(out LogContainer container))
            {
                readCount++;
                foreach (var logBase in container.gameLogs)
                {
                    var tableName = logBase.GetType().Name;
                    if(_dataTables.gameTables.TryGetValue(tableName, out DataTable dataTable) == false)
                    {
                        dataTable = new DataTable();
                        _dataTables.gameTables.Add(tableName, dataTable);
                    }
                    // 자식클래스의 property에 맞게 추가한다
                    logBase.AddToDatatable(ref dataTable);
                }

                foreach (var logBase in container.globalLogs)
                {
                    var tableName = logBase.GetType().Name;
                    if (_dataTables.globalTables.TryGetValue(tableName, out DataTable dataTable) == false)
                    {
                        dataTable = new DataTable();
                        _dataTables.globalTables.Add(tableName, dataTable);
                    }
                    // 자식클래스의 property에 맞게 추가한다
                    logBase.AddToDatatable(ref dataTable);
                }                
            }
        }

        private async Task BulkInsertWithRetries(string connStr, DataTable table)
        {
            try
            {
                var connection = new MySqlConnection(connStr);
                var sqlBulkCopy = new MySqlBulkCopy(connection);
                sqlBulkCopy.DestinationTableName = table.TableName;
                await sqlBulkCopy.WriteToServerAsync(table);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

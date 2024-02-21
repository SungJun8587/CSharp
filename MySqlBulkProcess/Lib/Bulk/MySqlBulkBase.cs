using System.Data;
using MySqlConnector;

namespace Common.Lib.Bulk
{
    /// <summary>MySqlBulk 기본</summary>
    internal class MySqlBulkBase : IDisposable
    {
        /// <summary>MySql 데이터베이스에 대한 연결</summary>
        protected MySqlConnection Connection { get; set; }
        
        /// <summary>MySql 데이터베이스 대량 데이터 처리</summary>
        protected MySqlBulkCopy MySqlBulkCopy { get; set; }

        /// <summary>MySql 데이터베이스에 만들 트랜잭션</summary>
        public MySqlTransaction Tran { get; set; }

        /// <summary>제한 시간이 초과되기 전에 작업이 완료되기 위한 시간(초)</summary>
        public int? CommandTimeout { get; set; }

        /// <summary>
        /// MySql 데이터베이스 대량 데이터 처리(MySqlBulkCopy)를 위한 설정 
        /// </summary>
        /// <param name="connection">MySql 데이터베이스에 대한 연결</param>
        /// <param name="tran">MySql 데이터베이스에 만들 트랜잭션</param>
        /// <param name="commandTimeout">제한 시간이 초과되기 전에 작업이 완료되기 위한 시간(초)</param>
        protected void MySqlBulk(MySqlConnection connection, MySqlTransaction tran, int? commandTimeout = null)
        {
            Connection = connection;
            if (Connection.State != ConnectionState.Open)
            {
                Connection.Open();
            }
            Tran = tran;
            
            MySqlBulkCopy = new MySqlBulkCopy(connection, tran);

            if (commandTimeout.HasValue)
            {
                CommandTimeout = commandTimeout.Value;
                
                // 기본값은 30초. 0 값은 제한이 없음을 나타내며 대량 복사가 무기한 대기
                MySqlBulkCopy.BulkCopyTimeout = commandTimeout.Value;            
            }
        }

        public void Dispose()
        {
            if (Connection.State == ConnectionState.Open)
            {
                Connection.Close();
            }            
            MySqlBulkCopy = null;
        }
    }
}

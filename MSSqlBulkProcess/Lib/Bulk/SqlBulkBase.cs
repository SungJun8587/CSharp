using System;
using System.Data;
using System.Data.SqlClient;

namespace Common.Lib.Bulk
{
    /// <summary>SqlBulk 기본</summary>
    internal class SqlBulkBase : IDisposable
    {
        /// <summary>SQL Server 데이터베이스에 대한 연결</summary>
        protected SqlConnection Connection { get; set; }

        /// <summary>SQL Server 데이터베이스 대량 데이터 처리</summary>
        protected SqlBulkCopy SqlBulkCopy { get; set; }
        
        /// <summary>SQL Server 데이터베이스에 만들 트랜잭션</summary>
        public SqlTransaction Tran { get; set; }

        /// <summary>제한 시간이 초과되기 전에 작업이 완료되기 위한 시간(초)</summary>
        public int? CommandTimeout { get; set; }

        /// <summary>
        /// SQL Server 데이터베이스 대량 데이터 처리(SqlBulkCopy)를 위한 설정 
        /// </summary>
        /// <param name="connection">SQL Server 데이터베이스에 대한 연결</param>
        /// <param name="tran">SQL Server 데이터베이스에 만들 트랜잭션</param>
        /// <param name="option">SqlBulkCopy 인스턴스와 함께 사용할 하나 이상의 옵션을 지정하는 비트 플래그</param>
        /// <param name="commandTimeout">제한 시간이 초과되기 전에 작업이 완료되기 위한 시간(초)</param>
        protected void SqlBulk(SqlConnection connection, SqlTransaction tran, SqlBulkCopyOptions option, int? commandTimeout = null)
        {
            Connection = connection;
            if (Connection.State != ConnectionState.Open)
            {
                Connection.Open();
            }
            Tran = tran;
            SqlBulkCopy = new SqlBulkCopy(connection, option, tran);

            if (commandTimeout.HasValue)
            {
                CommandTimeout = commandTimeout.Value;
                
                // 기본값은 30초. 0 값은 제한이 없음을 나타내며 대량 복사가 무기한 대기
                SqlBulkCopy.BulkCopyTimeout = commandTimeout.Value;            
            }            
        }

        public void Dispose()
        {
            SqlBulkCopy.Close();
            SqlBulkCopy = null;
        }
    }
}

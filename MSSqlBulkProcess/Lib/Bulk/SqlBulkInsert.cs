using System.Collections.Generic;
using System.Data.SqlClient;

namespace Common.Lib.Bulk
{
    /// <summary>SqlBulk 삭제</summary>
    internal class SqlBulkInsert : SqlBulkBase
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tran"></param>
        internal SqlBulkInsert(SqlConnection connection, SqlTransaction tran)
        {
            SqlBulk(connection, tran, SqlBulkCopyOptions.Default);
        }

        /// <summary>
        /// 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        internal void BulkInsert<T>(string destinationTableName, IEnumerable<T> data)
        {
            SqlBulkCopy.DestinationTableName = $"[{destinationTableName}]";
            var dt = SqlBulkCommon.GetDataTableFromFields(data, SqlBulkCopy);
            SqlBulkCopy.BatchSize = 100000;
            SqlBulkCopy.WriteToServer(dt);
        }

        /// <summary>
        /// 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        internal async Task BulkInsertAsync<T>(string destinationTableName, IEnumerable<T> data)
        {
            SqlBulkCopy.DestinationTableName = $"[{destinationTableName}]";
            var dt = SqlBulkCommon.GetDataTableFromFields(data, SqlBulkCopy);
            SqlBulkCopy.BatchSize = 100000;
            await SqlBulkCopy.WriteToServerAsync(dt);
        }
    }
}
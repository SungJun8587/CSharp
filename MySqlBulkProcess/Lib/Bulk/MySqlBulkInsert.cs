using MySqlConnector;

namespace Common.Lib.Bulk
{
    /// <summary>MySqlBulk 등록</summary>
    internal class MySqlBulkInsert : MySqlBulkBase
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tran"></param>
        internal MySqlBulkInsert(MySqlConnection connection, MySqlTransaction tran)
        {
            MySqlBulk(connection, tran);
        }

        /// <summary>
        /// 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        internal void BulkInsert<T>(string destinationTableName, IEnumerable<T> data)
        {
            MySqlBulkCopy.DestinationTableName = $"`{destinationTableName}`";
            var dt = MySqlBulkCommon.GetDataTableFromFields(data, MySqlBulkCopy);
            MySqlBulkCopy.WriteToServer(dt);
        }

        /// <summary>
        /// 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        internal async Task BulkInsertAsync<T>(string destinationTableName, IEnumerable<T> data)
        {
            MySqlBulkCopy.DestinationTableName = $"`{destinationTableName}`";
            var dt = MySqlBulkCommon.GetDataTableFromFields(data, MySqlBulkCopy);
            await MySqlBulkCopy.WriteToServerAsync(dt);
        }
    }
}
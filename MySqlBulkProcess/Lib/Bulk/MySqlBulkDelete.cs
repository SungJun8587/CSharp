using System.Text;
using MySqlConnector;

namespace Common.Lib.Bulk
{
    /// <summary>MySqlBulk 삭제</summary>
    internal class MySqlBulkDelete : MySqlBulkBase
    {
        internal MySqlBulkDelete(MySqlConnection connection, MySqlTransaction tran)
        {
            MySqlBulk(connection, tran);
        }

        /// <summary>
        /// 대상 테이블의 모든 데이터 삭제
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        internal void BulkDelete(string destinationTableName)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"TRUNCATE TABLE `{destinationTableName}`;";
            cmd.Transaction = Tran;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 데이터 일괄 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        /// <param name="columnNameToMatchs">원본 컬럼 목록</param>
        internal int BulkDelete<T>(string destinationTableName, IEnumerable<T> data, List<string> columnNameToMatchs)
        {
            var tempTablename = destinationTableName + "_" + Guid.NewGuid().ToString("N");

            CreateTempTable(destinationTableName, tempTablename);

            var dataAsArray = data as T[] ?? data.ToArray();
            MySqlBulkCopy.DestinationTableName = tempTablename;
            var dt = MySqlBulkCommon.GetDataTableFromFields(dataAsArray, MySqlBulkCopy);
            MySqlBulkCopy.WriteToServer(dt);

            var row = DeleteTempAndDestination(destinationTableName, tempTablename, columnNameToMatchs);

            DropTempTable(tempTablename);

            return row;
        }

        /// <summary>
        /// 임시 테이블 생성
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        private void CreateTempTable(string destinationTableName, string tempTablename)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE `{tempTablename}` AS (SELECT * FROM `{destinationTableName}` LIMIT 0);";
            cmd.Transaction = Tran;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 임시 테이블 삭제
        /// </summary>
        /// <param name="tempTablename">임시 테이블명</param>
        private void DropTempTable(string tempTablename)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE `{tempTablename}`;";
            cmd.Transaction = Tran;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 임시 테이블 데이터 기준으로 대상 테이블 데이터 삭제
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="columnNameToMatchs">삭제 조건 컬럼 목록</param>
        /// <returns>삭제된 Row 수</returns>
        private int DeleteTempAndDestination(string destinationTableName, string tempTablename, List<string> columnNameToMatchs)
        {
            var sb = new StringBuilder(columnNameToMatchs.Count > 0 ? " ON" : "");
            for (var i = 0; i < columnNameToMatchs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" AND");
                }

                sb.Append($" t1.`{columnNameToMatchs[i]}`=t2.`{columnNameToMatchs[i]}`");
            }
            var deleteSql = $"DELETE t1 FROM `{destinationTableName}` AS t1 INNER JOIN `{tempTablename}` AS t2{sb};";

            var cmd = Connection.CreateCommand();
            cmd.CommandText = deleteSql;
            cmd.Transaction = Tran;
            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 대상 테이블의 모든 데이터 삭제
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        internal async Task BulkDeleteAsync(string destinationTableName)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"TRUNCATE TABLE `{destinationTableName}`;";
            cmd.Transaction = Tran;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 데이터 일괄 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터</param>
        /// <param name="columnNameToMatchs">원본 컬럼 목록</param>
        internal async Task<int> BulkDeleteAsync<T>(string destinationTableName, IEnumerable<T> data, List<string> columnNameToMatchs)
        {
            var tempTablename = destinationTableName + "_" + Guid.NewGuid().ToString("N");

            await CreateTempTableAsync(destinationTableName, tempTablename);

            var dataAsArray = data as T[] ?? data.ToArray();
            MySqlBulkCopy.DestinationTableName = tempTablename;
            var dt = MySqlBulkCommon.GetDataTableFromFields(dataAsArray, MySqlBulkCopy);
            await MySqlBulkCopy.WriteToServerAsync(dt);

            var row = await DeleteTempAndDestinationAsync(destinationTableName, tempTablename, columnNameToMatchs);

            await DropTempTableAsync(tempTablename);

            return row;
        }

        /// <summary>
        /// 임시 테이블 생성
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        private async Task CreateTempTableAsync(string destinationTableName, string tempTablename)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"CREATE TABLE `{tempTablename}` AS (SELECT * FROM `{destinationTableName}` LIMIT 0);";
            cmd.Transaction = Tran;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 임시 테이블 삭제
        /// </summary>
        /// <param name="tempTablename">임시 테이블명</param>
        private async Task DropTempTableAsync(string tempTablename)
        {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"DROP TABLE `{tempTablename}`;";
            cmd.Transaction = Tran;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 임시 테이블 데이터 기준으로 대상 테이블 데이터 삭제
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="columnNameToMatchs">삭제 조건 컬럼 목록</param>
        /// <returns>삭제된 Row 수</returns>
        private async Task<int> DeleteTempAndDestinationAsync(string destinationTableName, string tempTablename, List<string> columnNameToMatchs)
        {
            var sb = new StringBuilder(columnNameToMatchs.Count > 0 ? " ON" : "");
            for (var i = 0; i < columnNameToMatchs.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" AND");
                }

                sb.Append($" t1.`{columnNameToMatchs[i]}`=t2.`{columnNameToMatchs[i]}`");
            }
            var deleteSql = $"DELETE t1 FROM `{destinationTableName}` AS t1 INNER JOIN `{tempTablename}` AS t2{sb};";

            var cmd = Connection.CreateCommand();
            cmd.CommandText = deleteSql;
            cmd.Transaction = Tran;
            return await cmd.ExecuteNonQueryAsync();
        }
    }
}
using System.Data.SqlClient;
using System.Text;

namespace Common.Lib.Bulk
{
    /// <summary>SqlBulk 수정</summary>
    internal class SqlBulkUpdate : SqlBulkBase
    {
        internal SqlBulkUpdate(SqlConnection connection, SqlTransaction tran)
        {
            SqlBulk(connection, tran, SqlBulkCopyOptions.KeepIdentity);
        }

        /// <summary>
        /// 데이터 일괄 업데이트
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터<param>
        /// <param name="pkColumns">원본 컬럼 목록</param>
        /// <param name="updateColumns">업데이트된 컬럼 목록</param>
        internal int BulkUpdate<T>(string destinationTableName, IEnumerable<T> data, List<string> pkColumns, List<string> updateColumns)
        {
            var tempTablename = "#" + destinationTableName + "_" + Guid.NewGuid().ToString("N");

            var cols = new List<string>();
            cols.AddRange(pkColumns);
            cols.AddRange(updateColumns);
            var allColumnNames = cols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            CreateTempTable(destinationTableName, tempTablename, allColumnNames);

            var dataAsArray = data as T[] ?? data.ToArray();
            SqlBulkCopy.DestinationTableName = tempTablename;
            var dt = SqlBulkCommon.GetDataTableFromFields(dataAsArray, SqlBulkCopy, allColumnNames);
            SqlBulkCopy.BatchSize = 100000;
            SqlBulkCopy.WriteToServer(dt);

            var row = MergeTempAndDestination(destinationTableName, tempTablename, pkColumns, updateColumns);

            DropTempTable(tempTablename);

            return row;
        }

        /// <summary>
        /// 임시 테이블 생성
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="colomns">컬럼 목록</param>
        private void CreateTempTable(string destinationTableName, string tempTablename, List<string> colomns)
        {
            var str = colomns.Count == 0 ? "*" : string.Join(",", colomns.Select(p => $"[{p}]"));
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"SELECT TOP 0 {str} INTO [{tempTablename}] FROM [{destinationTableName}];";
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
            cmd.CommandText = $"DROP TABLE [{tempTablename}]";
            cmd.Transaction = Tran;
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 임시 테이블 데이터 기준으로 대상 테이블 데이터 수정
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="pkColumns">기준 컬럼</param>
        /// <param name="updateColumns">수정할 컬럼</param>
        /// <returns>적용된 Row 수</returns> 
        private int MergeTempAndDestination(string destinationTableName, string tempTablename, List<string> pkColumns, List<string> updateColumns)
        {
            var pkSql = new StringBuilder();
            for (var i = 0; i < pkColumns.Count; i++)
            {
                if (i > 0)
                {
                    pkSql.Append(" AND");
                }
                pkSql.Append($" Target.[{pkColumns[i]}]=Source.[{pkColumns[i]}]");
            }

            var updateSql = new StringBuilder();
            for (var i = 0; i < updateColumns.Count; i++)
            {
                if (i > 0)
                {
                    updateSql.Append(",");
                }
                updateSql.Append($"Target.[{updateColumns[i]}]=Source.[{updateColumns[i]}]");
            }
            var mergeSql = $"MERGE INTO [{destinationTableName}] AS Target USING [{tempTablename}] AS Source ON {pkSql} WHEN MATCHED THEN UPDATE SET {updateSql};";

            var cmd = Connection.CreateCommand();
            cmd.CommandText = mergeSql;
            cmd.Transaction = Tran;
            return cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// 데이터 일괄 업데이트
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="data">데이터<param>
        /// <param name="pkColumns">원본 컬럼 목록</param>
        /// <param name="updateColumns">업데이트된 컬럼 목록</param>
        internal async Task<int> BulkUpdateAsync<T>(string destinationTableName, IEnumerable<T> data, List<string> pkColumns, List<string> updateColumns)
        {
            var tempTablename = "#" + destinationTableName + "_" + Guid.NewGuid().ToString("N");

            var cols = new List<string>();
            cols.AddRange(pkColumns);
            cols.AddRange(updateColumns);
            var allColumnNames = cols.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            await CreateTempTableAsync(destinationTableName, tempTablename, allColumnNames);

            var dataAsArray = data as T[] ?? data.ToArray();
            SqlBulkCopy.DestinationTableName = tempTablename;
            var dt = SqlBulkCommon.GetDataTableFromFields(dataAsArray, SqlBulkCopy, allColumnNames);
            SqlBulkCopy.BatchSize = 100000;
            await SqlBulkCopy.WriteToServerAsync(dt);

            var row = await MergeTempAndDestinationAsync(destinationTableName, tempTablename, pkColumns, updateColumns);

            await DropTempTableAsync(tempTablename);

            return row;
        }

        /// <summary>
        /// 임시 테이블 생성
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="colomns">컬럼 목록</param>
        private async Task CreateTempTableAsync(string destinationTableName, string tempTablename, List<string> colomns)
        {
            var str = colomns.Count == 0 ? "*" : string.Join(",", colomns.Select(p => $"[{p}]"));
            var cmd = Connection.CreateCommand();
            cmd.CommandText = $"SELECT TOP 0 {str} INTO [{tempTablename}] FROM [{destinationTableName}];";
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
            cmd.CommandText = $"DROP TABLE [{tempTablename}]";
            cmd.Transaction = Tran;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// 임시 테이블 데이터 기준으로 대상 테이블 데이터 수정
        /// </summary>
        /// <param name="destinationTableName">대상 테이블명</param>
        /// <param name="tempTablename">임시 테이블명</param>
        /// <param name="pkColumns">기준 컬럼</param>
        /// <param name="updateColumns">수정할 컬럼</param>
        /// <returns>적용된 Row 수</returns>        
        private async Task<int> MergeTempAndDestinationAsync(string destinationTableName, string tempTablename, List<string> pkColumns, List<string> updateColumns)
        {
            var pkSql = new StringBuilder();
            for (var i = 0; i < pkColumns.Count; i++)
            {
                if (i > 0)
                {
                    pkSql.Append(" AND");
                }
                pkSql.Append($" Target.[{pkColumns[i]}]=Source.[{pkColumns[i]}]");
            }

            var updateSql = new StringBuilder();
            for (var i = 0; i < updateColumns.Count; i++)
            {
                if (i > 0)
                {
                    updateSql.Append(",");
                }
                updateSql.Append($"Target.[{updateColumns[i]}]=Source.[{updateColumns[i]}]");
            }
            var mergeSql = $"MERGE INTO [{destinationTableName}] AS Target USING [{tempTablename}] AS Source ON {pkSql} WHEN MATCHED THEN UPDATE SET {updateSql};";

            var cmd = Connection.CreateCommand();
            cmd.CommandText = mergeSql;
            cmd.Transaction = Tran;
            return await cmd.ExecuteNonQueryAsync();
        }
    }
}
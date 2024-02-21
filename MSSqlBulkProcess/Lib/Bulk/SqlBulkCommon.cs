using System.Data;
using System.Data.SqlClient;

namespace Common.Lib.Bulk
{
    /// <summary>SqlBulk 유틸 함수</summary>
    internal class SqlBulkCommon
    {
        /// <summary>
        /// 열거형 데이터를 데이터 테이블로 변환
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data">열거형 데이터</param>
        /// <param name="sqlBulkCopy">SQL Server 데이터베이스 대량 데이터 처리 객체</param>
        /// <returns>데이터 테이블(DataTable)</returns>
        internal static DataTable GetDataTableFromFields<T>(IEnumerable<T> data, SqlBulkCopy sqlBulkCopy)
        {
            var dt = new DataTable();
            var listType = typeof(T).GetProperties();
            var list = new List<PropertiesModel>();
            foreach (var propertyInfo in listType)
            {
                var columnName = ReflectionHelper.GetColumnName(propertyInfo);
                if (columnName == null)
                {
                    continue;
                }

                DataColumn column;
                if (propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    var typeArray = propertyInfo.PropertyType.GetGenericArguments();
                    var baseType = typeArray[0];
                    column = new DataColumn(columnName, baseType);
                }
                else
                {
                    column = new DataColumn(columnName, propertyInfo.PropertyType);
                }

                dt.Columns.Add(column);

                sqlBulkCopy.ColumnMappings.Add(columnName, columnName);
                list.Add(new PropertiesModel()
                {
                    PropertyInfo = propertyInfo,
                    ColumnName = columnName
                });
            }

            foreach (var value in data)
            {
                var dr = dt.NewRow();
                foreach (var item in list)
                {
                    dr[item.ColumnName] = item.PropertyInfo.GetValue(value, null) ?? DBNull.Value;
                }
                dt.Rows.Add(dr);
            }

            return dt;
        }

        /// <summary>
        /// 열거형 데이터를 지정한 컬럼 목록 기준으로 데이터 테이블로 변환
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data">열거형 데이터</param>
        /// <param name="sqlBulkCopy">SQL Server 데이터베이스 대량 데이터 처리 객체</param>
        /// <param name="columnNames">지정한 컬럼 목록</param>
        /// <returns>데이터 테이블(DataTable)</returns>        
        internal static DataTable GetDataTableFromFields<T>(IEnumerable<T> data, SqlBulkCopy sqlBulkCopy, List<string> columnNames)
        {
            var listType = typeof(T).GetProperties();
            var list = new List<PropertiesModel>();
            foreach (var propertyInfo in listType)
            {
                var columnName = ReflectionHelper.GetColumnName(propertyInfo);
                if (columnName == null)
                {
                    continue;
                }

                // 지정한 컬럼에만 쓰기
                if (!columnNames.Exists(p => string.Equals(columnName, p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                list.Add(new PropertiesModel()
                {
                    PropertyInfo = propertyInfo,
                    ColumnName = columnName
                });
            }

            var dt = new DataTable();
            // 지정한 컬럼 순서와 일치
            var cols = new List<PropertiesModel>();
            foreach (var columnName in columnNames)
            {
                var obj = list.Find(p => string.Equals(p.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));
                if (obj != null)
                {
                    var propertyInfo = obj.PropertyInfo;
                    DataColumn column;
                    if (propertyInfo.PropertyType.IsGenericType && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        var typeArray = propertyInfo.PropertyType.GetGenericArguments();
                        var baseType = typeArray[0];
                        column = new DataColumn(columnName, baseType);
                    }
                    else
                    {
                        column = new DataColumn(columnName, propertyInfo.PropertyType);
                    }
                    dt.Columns.Add(column);
                    //
                    sqlBulkCopy.ColumnMappings.Add(columnName, columnName);
                    //
                    cols.Add(obj);
                }
                else
                {
                    throw new Exception("누락된 컬럼");
                }
            }

            foreach (var value in data)
            {
                var dr = dt.NewRow();
                foreach (var item in cols)
                {
                    dr[item.ColumnName] = item.PropertyInfo.GetValue(value, null) ?? DBNull.Value;
                }
                dt.Rows.Add(dr);
            }

            return dt;
        }
    }
}

using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text;

namespace Common.Lib.Bulk
{
    /// <summary>
    /// 
    /// </summary>
    internal class QueryableBuilder
    {
        private static IDictionary<object, string> ObjectWhere { get; } = new ConcurrentDictionary<object, string>();

        /// <summary>
        /// 대량 데이터 가져오기 목록(Dapper 의존)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="whereConditions">eg: new { Id =1 } 或 new { Id = new []{1, 2}.ToList() }</param>
        /// <returns></returns>
        internal static IQuery<T> GetListByBulk<T>(object whereConditions)
        {
            var name = ReflectionHelper.GetTableName(typeof(T));
            var where = GetWhere(whereConditions);

            var res = new Queryable<T>()
            {
                TableName = name,
                Where = where
            };

            return res;
        }

        /// <summary>
        /// 대량 데이터 가져오기 목록(Dapper 의존)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="whereConditions">eg: new { Id =1 } 或 new { Id = new []{1, 2}.ToList() }</param>
        /// <param name="selectColumns">eg: p => p.Id 或 p => new { p.Id, p.Name }</param>
        /// <returns></returns>
        internal static IQuery<T> GetListByBulk<T>(object whereConditions, Expression<Func<T, object>> selectColumns) where T : new()
        {
            var name = ReflectionHelper.GetTableName(typeof(T));
            var where = GetWhere(whereConditions);

            var res = new Queryable<T>()
            {
                TableName = name,
                Where = where
            };

            // 컬럼 나열
            var cols = selectColumns.GetColumns();
            if (cols != null && cols.Count > 0)
            {
                if (QueryConfig.DialectServer == Dialect.SqlServer)
                {
                    res.SelectColumns = string.Join(",", cols.Select(p => $"[{p}]"));
                }
                else
                {
                    res.SelectColumns = string.Join(",", cols.Select(p => $"`{p}`"));
                }
            }

            return res;
        }

        /// <summary>
        /// 대량 데이터 가져오기 목록(Dapper 의존)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="likeColumns">eg:p => new { p.Id, p.Name }</param>
        /// <param name="keywords">검색 키워드 목록</param>
        /// <param name="param">반환 인자</param>
        /// <returns></returns>
        internal static IQuery<T> GetListByBulkLike<T>(Func<T, object> likeColumns, List<string> keywords, out Dictionary<string, object> param) where T : new()
        {
            var name = ReflectionHelper.GetTableName(typeof(T));
            var where = GetWhere(likeColumns, keywords, out param);

            return new Queryable<T>()
            {
                TableName = name,
                Where = where
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="likeColumns"></param>
        /// <param name="keywords"></param>
        /// <param name="param"></param>
        /// <returns></returns>
        internal static string GetWhere<T>(Func<T, object> likeColumns, List<string> keywords, out Dictionary<string, object> param) where T : new()
        {
            param = new Dictionary<string, object>();
            if (keywords.Count == 0)
            {
                return "";
            }

            var concat = GetQueryColumn(likeColumns);
            if (string.IsNullOrWhiteSpace(concat))
            {
                return "";
            }

            var sb = new StringBuilder();
            sb.Append(" WHERE");
            var list = new List<string>();
            for (var i = 0; i < keywords.Count; i++)
            {
                var name = $"Keyword__{i}";
                var value = keywords[i];
                param.Add(name, value);
                if (QueryConfig.DialectServer == Dialect.SqlServer)
                {
                    list.Add($" {concat} LIKE '%' + @{name} + '%'");
                }
                else if (QueryConfig.DialectServer == Dialect.MySql)
                {
                    list.Add($" {concat} LIKE CONCAT('%', @{name}, '%')");
                }
            }
            sb.Append(string.Join(" AND", list));
            return sb.ToString();
        }

        /// <summary>
        /// 합쳐진 쿼리 컬럼 얻기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="likeColumns"></param>
        /// <returns></returns>
        internal static string GetQueryColumn<T>(Func<T, object> likeColumns) where T : new()
        {
            var columnObj = likeColumns.Invoke(new T());
            var sb = new StringBuilder();
            var fields = columnObj.GetType().GetProperties();
            if (fields.Length > 0)
            {
                var list = new List<string>();
                foreach (var field in fields)
                {
                    list.Add(field.Name.Ns());
                }
                if (QueryConfig.DialectServer == Dialect.SqlServer)
                {
                    sb.Append(string.Join(" + ' ' + ", list));
                }
                else if (QueryConfig.DialectServer == Dialect.MySql)
                {
                    sb.Append($"CONCAT({string.Join(" , ' ' , ", list)})");
                }
            }
            return sb.ToString();
        }

        private static string GetWhere(object whereConditions)
        {
            if (whereConditions == null)
            {
                return "";
            }

            if (ObjectWhere.ContainsKey(whereConditions))
            {
                return ObjectWhere[whereConditions];
            }

            var sb = new StringBuilder();
            var fields = whereConditions.GetType().GetProperties();
            if (fields.Length > 0)
            {
                sb.Append(" WHERE");
                var addAnd = false;
                foreach (var field in fields)
                {
                    if (addAnd)
                    {
                        sb.Append(" AND");
                    }
                    else
                    {
                        addAnd = true;
                    }

                    var fieldName = field.Name;
                    var fieldValue = field.GetValue(whereConditions);
                    switch (fieldValue)
                    {
                        case string _:
                            sb.Append($" {fieldName.Ns()}=@{fieldName}");
                            break;
                        case IEnumerable _:
                            sb.Append($" {fieldName.Ns()} IN @{fieldName}");
                            break;
                        default:
                            sb.Append($" {fieldName.Ns()}=@{fieldName}");
                            break;
                    }
                }
            }

            var wh = sb.ToString();
            ObjectWhere.Add(whereConditions, wh);
            return wh;
        }

        internal static string GetPropertyName<T, TResult>(Expression<Func<T, TResult>> expression)
        {
            if (expression == null)
            {
                return "";
            }

            var rtn = "";
            if (expression.Body is UnaryExpression body)
            {
                rtn = ((MemberExpression)body.Operand).Member.Name;
            }
            else if (expression.Body is MemberExpression body2)
            {
                rtn = body2.Member.Name;
            }
            else if (expression.Body is ParameterExpression body3)
            {
                rtn = body3.Type.Name;
            }
            return rtn;
        }
    }
}

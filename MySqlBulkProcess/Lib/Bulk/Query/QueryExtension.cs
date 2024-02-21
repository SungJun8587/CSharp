using Dapper;
using System.Data;
using System.Linq.Expressions;

namespace Common.Lib.Bulk
{
    /// <summary>
    /// 
    /// </summary>
    public static class QueryExtension
    {
        /// <summary>
        /// 필드 일치 집합 기반 데이터 쿼리(첫 번째 FirstOrDefault 가져오기, OrderBy 정렬)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="whereConditions"></param>
        /// <param name="transaction"></param>
        /// <param name="commandTimeout"></param>
        /// <returns></returns>
        public static IQuery<T> GetListByBulk<T>(this IDbConnection db, object whereConditions, IDbTransaction transaction = null, int? commandTimeout = null)
        {
            var obj = QueryableBuilder.GetListByBulk<T>(whereConditions);
            obj.Db = db;
            obj.Transaction = transaction;
            obj.CommandTimeout = commandTimeout;
            obj.WhereConditions = whereConditions;
            return obj;
        }

        /// <summary>
        /// 필드 일치 집합 기반 데이터 쿼리(첫 번째 FirstOrDefault 가져오기, OrderBy 정렬)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="whereConditions"></param>
        /// <param name="selectColumns"></param>
        /// <param name="transaction"></param>
        /// <param name="commandTimeout"></param>
        /// <returns></returns>
        public static IQuery<T> GetListByBulk<T>(this IDbConnection db, object whereConditions, Expression<Func<T, object>> selectColumns, IDbTransaction transaction = null, int? commandTimeout = null) where T : new()
        {
            var obj = QueryableBuilder.GetListByBulk(whereConditions, selectColumns);
            obj.Db = db;
            obj.Transaction = transaction;
            obj.CommandTimeout = commandTimeout;
            obj.WhereConditions = whereConditions;
            return obj;
        }

        /// <summary>
        /// like 키워드 기반 데이터 조회 (첫 번째 FirstOrDefault 가져오기, OrderBy 정렬)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="likeColumns">like 쿼리 열。eg:p=>new { p.Name, p.Text }</param>
        /// <param name="keywords">키워드 집합</param>
        /// <param name="transaction"></param>
        /// <param name="commandTimeout"></param>
        /// <returns></returns>
        public static IQuery<T> GetListByBulkLike<T>(this IDbConnection db, Func<T, object> likeColumns, List<string> keywords, IDbTransaction transaction = null, int? commandTimeout = null) where T : new()
        {
            var obj = QueryableBuilder.GetListByBulkLike(likeColumns, keywords, out var whereConditions);
            obj.Db = db;
            obj.Transaction = transaction;
            obj.CommandTimeout = commandTimeout;
            obj.WhereConditions = whereConditions;
            return obj;
        }

        /// <summary>
        /// 몇 가지 항목 가져오기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="number"></param>
        /// <returns></returns>
        public static IQuery<T> Take<T>(this IQuery<T> obj, int number)
        {
            obj.Top = number;
            return obj;
        }

        /// <summary>
        /// 순서 정렬
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="obj"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IOrderQuery<T> OrderBy<T, TResult>(this IQuery<T> obj, Expression<Func<T, TResult>> predicate)
        {
            obj.OrderBy = $"ORDER BY {QueryableBuilder.GetPropertyName(predicate)} ASC";
            return (IOrderQuery<T>)obj;
        }

        /// <summary>
        /// 순서 정렬
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="obj"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IOrderQuery<T> ThenBy<T, TResult>(this IOrderQuery<T> obj, Expression<Func<T, TResult>> predicate)
        {
            obj.OrderBy = $"{obj.OrderBy},{QueryableBuilder.GetPropertyName(predicate)} ASC";
            return obj;
        }

        /// <summary>
        /// 역순 정렬
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="obj"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IOrderQuery<T> OrderByDescending<T, TResult>(this IQuery<T> obj, Expression<Func<T, TResult>> predicate)
        {
            obj.OrderBy = $"ORDER BY {QueryableBuilder.GetPropertyName(predicate)} DESC";
            return (IOrderQuery<T>)obj;
        }

        /// <summary>
        /// 역순 정렬
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="obj"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IOrderQuery<T> ThenByDescending<T, TResult>(this IOrderQuery<T> obj, Expression<Func<T, TResult>> predicate)
        {
            obj.OrderBy = $"{obj.OrderBy},{QueryableBuilder.GetPropertyName(predicate)} DESC";
            return obj;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        internal static string Ns(this string name)
        {
            if (QueryConfig.DialectServer == Dialect.SqlServer)
            {
                return $"[{name}]";
            }

            if (QueryConfig.DialectServer == Dialect.MySql)
            {
                return $"`{name}`";
            }

            return name;
        }

        /// <summary>
        /// 목록 얻기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static List<T> ToList<T>(this IQuery<T> obj)
        {
            var col = string.IsNullOrWhiteSpace(obj.SelectColumns) ? "*" : obj.SelectColumns;
            var sql = string.Empty;
            if (QueryConfig.DialectServer == Dialect.SqlServer)
            {
                sql = $"SELECT{(obj.Top >= 0 ? $" TOP ({obj.Top})" : "")} {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }
            else if (QueryConfig.DialectServer == Dialect.MySql)
            {
                sql = obj.Top > 0
                    ? $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy} LIMIT 0,{obj.Top};"
                    : $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }

            return obj.Db.Query<T>(sql, obj.WhereConditions, transaction: obj.Transaction, commandTimeout: obj.CommandTimeout, commandType: CommandType.Text).ToList();
        }

        /// <summary>
        /// 첫 번째 FirstOrDefault 가져오기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static T FirstOrDefault<T>(this IQuery<T> obj)
        {
            obj.Top = 1;
            var col = string.IsNullOrWhiteSpace(obj.SelectColumns) ? "*" : obj.SelectColumns;
            var sql = string.Empty;
            if (QueryConfig.DialectServer == Dialect.SqlServer)
            {
                sql = $"SELECT{(obj.Top >= 0 ? $" TOP ({obj.Top})" : "")} {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }
            else if (QueryConfig.DialectServer == Dialect.MySql)
            {
                if (obj.Top > 0)
                {
                    sql = $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy} LIMIT 0,{obj.Top};";
                }
                else
                {
                    sql = $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
                }
            }
            return obj.Db.Query<T>(sql, obj.WhereConditions, transaction: obj.Transaction, commandTimeout: obj.CommandTimeout, commandType: CommandType.Text).FirstOrDefault();
        }

        /// <summary>
        /// 목록 얻기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static async Task<List<T>> ToListAsync<T>(this IQuery<T> obj)
        {
            var col = string.IsNullOrWhiteSpace(obj.SelectColumns) ? "*" : obj.SelectColumns;
            var sql = string.Empty;
            if (QueryConfig.DialectServer == Dialect.SqlServer)
            {
                sql = $"SELECT{(obj.Top >= 0 ? $" TOP ({obj.Top})" : "")} {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }
            else if (QueryConfig.DialectServer == Dialect.MySql)
            {
                sql = obj.Top > 0
                    ? $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy} LIMIT 0,{obj.Top};"
                    : $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }

            return (await obj.Db.QueryAsync<T>(sql, obj.WhereConditions, transaction: obj.Transaction, commandTimeout: obj.CommandTimeout, commandType: CommandType.Text)).ToList();
        }

        /// <summary>
        /// 첫 번째 FirstOrDefault 가져오기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static async Task<T> FirstOrDefaultAsync<T>(this IQuery<T> obj)
        {
            obj.Top = 1;
            var col = string.IsNullOrWhiteSpace(obj.SelectColumns) ? "*" : obj.SelectColumns;
            var sql = string.Empty;
            if (QueryConfig.DialectServer == Dialect.SqlServer)
            {
                sql = $"SELECT{(obj.Top >= 0 ? $" TOP ({obj.Top})" : "")} {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
            }
            else if (QueryConfig.DialectServer == Dialect.MySql)
            {
                if (obj.Top > 0)
                {
                    sql = $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy} LIMIT 0,{obj.Top};";
                }
                else
                {
                    sql = $"SELECT {col} FROM {obj.TableName} {obj.Where} {obj.OrderBy};";
                }
            }
            return (await obj.Db.QueryAsync<T>(sql, obj.WhereConditions, transaction: obj.Transaction, commandTimeout: obj.CommandTimeout, commandType: CommandType.Text)).FirstOrDefault();
        } 

        /// <summary>
        /// 데이터 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="whereConditions"></param>
        /// <param name="transaction"></param>
        /// <param name="commandTimeout"></param>
        public static int DeleteListByBulk<T>(this IDbConnection db, object whereConditions, IDbTransaction transaction = null, int? commandTimeout = null) where T : new()
        {
            var obj = QueryableBuilder.GetListByBulk<T>(whereConditions);
            var sql = $"DELETE FROM {obj.TableName}{obj.Where};";
            return db.Execute(sql, whereConditions, transaction, commandTimeout, CommandType.Text);
        }   

        /// <summary>
        /// 데이터 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="whereConditions"></param>
        /// <param name="transaction"></param>
        /// <param name="commandTimeout"></param>
        public static async Task<int> DeleteListByBulkAsync<T>(this IDbConnection db, object whereConditions, IDbTransaction transaction = null, int? commandTimeout = null) where T : new()
        {
            var obj = QueryableBuilder.GetListByBulk<T>(whereConditions);
            var sql = $"DELETE FROM {obj.TableName}{obj.Where};";
            return await db.ExecuteAsync(sql, whereConditions, transaction, commandTimeout, CommandType.Text);
        }                         
    }
}

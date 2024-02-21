using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;

namespace Common.Lib.Bulk
{
    /// <summary>SqlBulk 확장</summary>
    public static class SqlBulkExt
    {
        /// <summary>
        /// 데이터 일괄 삽입 (NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="tran"></param>
        public static void BulkInsert<T>(this SqlConnection db, List<T> dt, SqlTransaction tran = null)
        {
            var tableName = typeof(T).Name;
            BulkInsert(db, tableName, dt, tran);
        }

        /// <summary>
        /// 대상 테이블명이 지정된 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="tran"></param>
        public static void BulkInsert<T>(this SqlConnection db, string tableName, List<T> dt, SqlTransaction tran = null)
        {
            using (var sbc = new SqlBulkInsert(db, tran))
            {
                sbc.BulkInsert(tableName, dt);
            }
        }

        /// <summary>
        /// 데이터 일괄 업데이트(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TUpdateColumn"></typeparam>
        /// <typeparam name="TPkColumn"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="columnUpdateExpression">업데이트할 컬럼 집합</param>
        /// <param name="columnPrimaryKeyExpression">원본 컬럼 집합</param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static int BulkUpdate<T, TUpdateColumn, TPkColumn>(this SqlConnection db, List<T> dt, Expression<Func<T, TUpdateColumn>> columnUpdateExpression, Expression<Func<T, TPkColumn>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            var tableName = typeof(T).Name;
            return BulkUpdate(db, tableName, dt, columnUpdateExpression, columnPrimaryKeyExpression, tran);
        }

        /// <summary>
        /// 데이터 일괄 업데이트(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TUpdateColumn"></typeparam>
        /// <typeparam name="TPkColumn"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="columnUpdateExpression">업데이트할 컬럼 집합</param>
        /// <param name="columnPrimaryKeyExpression">원본 컬럼 집합</param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static int BulkUpdate<T, TUpdateColumn, TPkColumn>(this SqlConnection db, string tableName, List<T> dt, Expression<Func<T, TUpdateColumn>> columnUpdateExpression, Expression<Func<T, TPkColumn>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            if (columnPrimaryKeyExpression == null)
            {
                throw new Exception("columnPrimaryKeyExpression is null.");
            }
            if (columnUpdateExpression == null)
            {
                throw new Exception("columnInputExpression is null.");
            }

            var pkColumns = ReflectionHelper.GetColumns(columnPrimaryKeyExpression);
            if (pkColumns.Count == 0)
            {
                throw new Exception("원본 컬럼 집합은 비워 둘 수 없습니다.");
            }

            var updateColumns = ReflectionHelper.GetColumns(columnUpdateExpression);
            if (updateColumns.Count == 0)
            {
                throw new Exception("업데이트할 컬럼 집합은 비워 둘 수 없습니다.");
            }

            using (var sbu = new SqlBulkUpdate(db, tran))
            {
                return sbu.BulkUpdate(tableName, dt, pkColumns, updateColumns);
            }
        }

        /// <summary>
        /// 데이터 일괄 삭제(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TPk"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="columnPrimaryKeyExpression"></param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static int BulkDelete<T, TPk>(this SqlConnection db, List<T> dt, Expression<Func<T, TPk>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            var tableName = typeof(T).Name;
            return BulkDelete(db, tableName, dt, columnPrimaryKeyExpression, tran);
        }

        /// <summary>
        /// 데이터 일괄 삭제(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TPk"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="columnPrimaryKeyExpression"></param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static int BulkDelete<T, TPk>(this SqlConnection db, string tableName, List<T> dt, Expression<Func<T, TPk>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            if (columnPrimaryKeyExpression == null)
            {
                throw new Exception("columnPrimaryKeyExpression is null.");
            }

            var pkColumns = ReflectionHelper.GetColumns(columnPrimaryKeyExpression);
            if (pkColumns.Count == 0)
            {
                throw new Exception("원본 컬럼 집합은 비워 둘 수 없습니다.");
            }

            using (var sbc = new SqlBulkDelete(db, tran))
            {
                return sbc.BulkDelete(tableName, dt, pkColumns);
            }
        }

        /// <summary>
        /// 모든 데이터 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="tran"></param>
        public static void BulkDelete<T>(this SqlConnection db, SqlTransaction tran = null)
        {
            var tableName = typeof(T).Name;
            BulkDelete(db, tableName, tran);
        }

        /// <summary>
        /// 대상 테이블명이 지정된 데이터 일괄 삭제
        /// </summary>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="tran"></param>
        public static void BulkDelete(this SqlConnection db, string tableName, SqlTransaction tran = null)
        {
            using (var sbc = new SqlBulkDelete(db, tran))
            {
                sbc.BulkDelete(tableName);
            }
        }

        /// <summary>
        /// 데이터 일괄 삽입(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="tran"></param>
        public static async Task BulkInsertAsync<T>(this SqlConnection db, List<T> dt, SqlTransaction tran = null)
        {
            var tableName = typeof(T).Name;
            await BulkInsertAsync(db, tableName, dt, tran);
        }

        /// <summary>
        /// 대상 테이블명이 지정된 데이터 일괄 삽입
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="tran"></param>
        public static async Task BulkInsertAsync<T>(this SqlConnection db, string tableName, List<T> dt, SqlTransaction tran = null)
        {
            using (var sbc = new SqlBulkInsert(db, tran))
            {
                await sbc.BulkInsertAsync(tableName, dt);
            }
        }

        /// <summary>
        /// 데이터 일괄 업데이트(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TUpdateColumn"></typeparam>
        /// <typeparam name="TPkColumn"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="columnUpdateExpression">업데이트할 컬럼 집합</param>
        /// <param name="columnPrimaryKeyExpression">원본 컬럼 집합</param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static async Task<int> BulkUpdateAsync<T, TUpdateColumn, TPkColumn>(this SqlConnection db, List<T> dt, Expression<Func<T, TUpdateColumn>> columnUpdateExpression, Expression<Func<T, TPkColumn>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            var tableName = typeof(T).Name;
            return await BulkUpdateAsync(db, tableName, dt, columnUpdateExpression, columnPrimaryKeyExpression, tran);
        }

        /// <summary>
        /// 데이터 일괄 업데이트(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TUpdateColumn"></typeparam>
        /// <typeparam name="TPkColumn"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="columnUpdateExpression">업데이트할 컬럼 집합</param>
        /// <param name="columnPrimaryKeyExpression">원본 컬럼 집합</param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static async Task<int> BulkUpdateAsync<T, TUpdateColumn, TPkColumn>(this SqlConnection db, string tableName, List<T> dt, Expression<Func<T, TUpdateColumn>> columnUpdateExpression, Expression<Func<T, TPkColumn>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            if (columnPrimaryKeyExpression == null)
            {
                throw new Exception("columnPrimaryKeyExpression is null.");
            }
            if (columnUpdateExpression == null)
            {
                throw new Exception("columnInputExpression is null.");
            }

            var pkColumns = ReflectionHelper.GetColumns(columnPrimaryKeyExpression);
            if (pkColumns.Count == 0)
            {
                throw new Exception("원본 컬럼 집합은 비워 둘 수 없습니다.");
            }

            var updateColumns = ReflectionHelper.GetColumns(columnUpdateExpression);
            if (updateColumns.Count == 0)
            {
                throw new Exception("업데이트할 컬럼 집합은 비워 둘 수 없습니다.");
            }

            using (var sbu = new SqlBulkUpdate(db, tran))
            {
                return await sbu.BulkUpdateAsync(tableName, dt, pkColumns, updateColumns);
            }
        }

        /// <summary>
        /// 데이터 일괄 삭제(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TPk"></typeparam>
        /// <param name="db"></param>
        /// <param name="dt"></param>
        /// <param name="columnPrimaryKeyExpression"></param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static async Task<int> BulkDeleteAsync<T, TPk>(this SqlConnection db, List<T> dt, Expression<Func<T, TPk>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            var tableName = typeof(T).Name;
            return await BulkDeleteAsync(db, tableName, dt, columnPrimaryKeyExpression, tran);
        }

        /// <summary>
        /// 데이터 일괄 삭제(NotMapped 속성 지원)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TPk"></typeparam>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="dt"></param>
        /// <param name="columnPrimaryKeyExpression"></param>
        /// <param name="tran"></param>
        /// <returns>적용된 Row 수</returns>
        public static async Task<int> BulkDeleteAsync<T, TPk>(this SqlConnection db, string tableName, List<T> dt, Expression<Func<T, TPk>> columnPrimaryKeyExpression, SqlTransaction tran = null) where T : new()
        {
            if (columnPrimaryKeyExpression == null)
            {
                throw new Exception("columnPrimaryKeyExpression is null.");
            }

            var pkColumns = ReflectionHelper.GetColumns(columnPrimaryKeyExpression);
            if (pkColumns.Count == 0)
            {
                throw new Exception("원본 컬럼 집합은 비워 둘 수 없습니다.");
            }

            using (var sbc = new SqlBulkDelete(db, tran))
            {
                return await sbc.BulkDeleteAsync(tableName, dt, pkColumns);
            }
        }

        /// <summary>
        /// 모든 데이터 삭제
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="db"></param>
        /// <param name="tran"></param>
        public static async Task BulkDeleteAsync<T>(this SqlConnection db, SqlTransaction tran = null)
        {
            var tableName = typeof(T).Name;
            await BulkDeleteAsync(db, tableName, tran);
        }

        /// <summary>
        /// 대상 테이블명이 지정된 데이터 일괄 삭제
        /// </summary>
        /// <param name="db"></param>
        /// <param name="tableName"></param>
        /// <param name="tran"></param>
        public static async Task BulkDeleteAsync(this SqlConnection db, string tableName, SqlTransaction tran = null)
        {
            using (var sbc = new SqlBulkDelete(db, tran))
            {
                await sbc.BulkDeleteAsync(tableName);
            }
        }
    }
}

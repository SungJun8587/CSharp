namespace Common.Lib.Bulk
{
    /// <summary>
    /// 
    /// </summary>
    public class QueryConfig
    {
        internal static Dialect DialectServer { get; set; } = Dialect.SqlServer;

        /// <summary>
        /// 사용할 데이터베이스 설정
        /// </summary>
        /// <param name="dialect"></param>
        /// <returns></returns>
        public static void SetDialect(Dialect dialect)
        {
            DialectServer = dialect;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public enum Dialect
    {
        /// <summary>
        /// 
        /// </summary>
        SqlServer = 0,

        /// <summary>
        /// 
        /// </summary>
        MySql = 1
    }
}

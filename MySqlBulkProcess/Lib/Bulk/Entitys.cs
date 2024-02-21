using System.Reflection;

namespace Common.Lib.Bulk
{
    /// <summary>IEnumerable으로 정의된 임의 클래스에 프로퍼티와 DataTable 컬럼을 매칭</summary>
    internal class PropertiesModel
    {
        /// <summary>임의 클래스에 프로퍼티 정보</summary>
        internal PropertyInfo PropertyInfo { get; set; }

        /// <summary>데이터 테이블 컬럼 명</summary>
        internal string ColumnName { get; set; }
    }
}

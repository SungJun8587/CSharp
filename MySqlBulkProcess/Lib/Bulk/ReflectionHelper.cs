using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;

namespace Common.Lib.Bulk
{
    internal static class ReflectionHelper
    {
        private static readonly IDictionary<Type, string> _tableNameCache = new ConcurrentDictionary<Type, string>();
        private static readonly IDictionary<Type, Tuple<MemberInfo, string>[]> _columnMapCache = new ConcurrentDictionary<Type, Tuple<MemberInfo, string>[]>();
        private static readonly IDictionary<Type, string> _keyColumnNamesCache = new ConcurrentDictionary<Type, string>();
        private static readonly IDictionary<Type, List<string>> _mappedColumnNamesCache = new ConcurrentDictionary<Type, List<string>>();

        internal static string GetTableName(Type type)
        {
            if (_tableNameCache.ContainsKey(type))
            {
                return _tableNameCache[type];
            }

            string TableName = string.Empty;

            var tableAttr = type.GetCustomAttribute<TableAttribute>();
            if (tableAttr != null)
            {
                TableName = (tableAttr.Schema == null ? "" : tableAttr.Schema + ".") + tableAttr.Name;
            }
            else
            {
                TableName = type.Name;
            }

            _tableNameCache[type] = TableName;
            return TableName;
        }

        internal static Tuple<MemberInfo, string>[] GetColumnMap(Type type)
        {
            Tuple<MemberInfo, string>[] GetColumnMapImpl(Type t)
            {
                return t
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Cast<MemberInfo>()
                    .Union(type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    .Where(member => member.GetCustomAttribute<NotMappedAttribute>() == null)
                    .Select(
                        member => new Tuple<MemberInfo, string>(
                            member,
                            member.GetCustomAttribute<ColumnAttribute>()?.Name ?? member.Name))
                    .ToArray();
            }

            if (_columnMapCache.ContainsKey(type))
            {
                return _columnMapCache[type];
            }

            var columnMap = GetColumnMapImpl(type);
            _columnMapCache.Add(type, columnMap);
            return columnMap;
        }

        internal static string GetKeyColumnNames(Type type)
        {
            if (_keyColumnNamesCache.ContainsKey(type))
            {
                return _keyColumnNamesCache[type];
            }

            string KeyColumn = string.Empty;

            var columnsAttr = type.GetProperties().Where(x => x.GetCustomAttributes(typeof(KeyAttribute), true).Length > 0).ToArray();
            if (columnsAttr != null && columnsAttr.Count() > 0)
            {
                KeyColumn = columnsAttr[0].Name;
            }

            _keyColumnNamesCache[type] = KeyColumn;
            return KeyColumn;
        }

        internal static List<string> GetMappedColumnNames(Type type)
        {
            if (_mappedColumnNamesCache.ContainsKey(type))
            {
                return _mappedColumnNamesCache[type];
            }

            List<string> MappedColumns = new List<string>();

            var columnsAttr = type.GetProperties().Where(x => x.GetCustomAttributes(typeof(NotMappedAttribute), true).Length == 0).ToArray();
            foreach (PropertyInfo column in columnsAttr)
            {
                var attributes = column.GetCustomAttributes(false);
                var columnMapping = attributes.FirstOrDefault(a => a.GetType() == typeof(ColumnAttribute));

                if (columnMapping != null) MappedColumns.Add((columnMapping as ColumnAttribute).Name);
                else MappedColumns.Add(column.Name);
            }

            _mappedColumnNamesCache[type] = MappedColumns;
            return MappedColumns;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        internal static string[] GetColumns(object obj)
        {
            return obj.GetType().GetProperties().Select(GetColumnName).Where(p => p != null).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propertyInfo"></param>
        /// <returns></returns>
        internal static string GetColumnName(PropertyInfo propertyInfo)
        {
            var columnAttributes = propertyInfo.GetCustomAttributes().ToList();
            if (columnAttributes.Exists(p => p is NotMappedAttribute))
            {
                return null;
            }

            var columnAttribute = columnAttributes.Find(p => p is ColumnAttribute);
            return columnAttribute != null ? ((ColumnAttribute)columnAttribute).Name : propertyInfo.Name;
        }

        internal static object GetValue(this MemberInfo memberInfo, object obj)
        {
            switch (memberInfo.MemberType)
            {
                case MemberTypes.Field:
                    return ((FieldInfo)memberInfo).GetValue(obj);
                case MemberTypes.Property:
                    return ((PropertyInfo)memberInfo).GetValue(obj);
                default:
                    throw new InvalidOperationException();
            }
        }

        internal static Type GetMemberType(this MemberInfo memberInfo)
        {
            switch (memberInfo.MemberType)
            {
                case MemberTypes.Field:
                    return ((FieldInfo)memberInfo).FieldType;
                case MemberTypes.Property:
                    return ((PropertyInfo)memberInfo).PropertyType;
                default:
                    throw new InvalidOperationException();
            }
        }

        /// <summary>
        /// 컬럼 목록 얻기
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="TColumn"></typeparam>
        /// <param name="expression"></param>
        /// <returns></returns>
        internal static List<string> GetColumns<T, TColumn>(this Expression<Func<T, TColumn>> expression) where T : new()
        {
            if (expression.Body is MemberExpression memberBody)
            {
                return new List<string>() { memberBody.Member.Name };
            }

            if (expression.Body is UnaryExpression unaryBody)
            {
                var name = ((MemberExpression)unaryBody.Operand).Member.Name;
                return new List<string>() { name };
            }

            if (expression.Body is ParameterExpression parameterBody)
            {
                return new List<string>() { parameterBody.Type.Name };
            }

            var t = new T();
            var obj = expression.Compile().Invoke(t);
            return GetMappedColumnNames(obj.GetType());
        }
    }
}

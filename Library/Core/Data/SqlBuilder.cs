// SqlBuilder.cs : 다중 행 INSERT / UPSERT 문장을 만드는 순수 함수 모음(DB 없이 시험할 수 있다).
// Dapper 는 목록 파라미터를 행마다 따로 실행(왕복 N번)하므로, 행을 한 문장 "VALUES (...),(...)" 로 묶어 왕복을 줄인다.
// 파라미터 이름은 ParamName(행, 열) = "@r{행}c{열}" 로 정해져 있다. 값을 채우는 쪽(MySqlDb)이 같은 규칙을 쓴다.
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Data;

public static class SqlBuilder
{
    /// <summary>한 문장에 묶는 행 수의 기본 상한.</summary>
    public const int DefaultMaxRows = 500;

    /// <summary>한 문장의 파라미터 수 상한. MySQL 의 한도(65535)보다 여유를 둔다.</summary>
    public const int MaxParameters = 30000;

    /// <summary>식별자(테이블/열 이름)를 검사해 `이름` 으로 돌려준다. 영문/숫자/밑줄만 허용한다(SQL 문자열에 이름을 직접 붙이므로 주입을 막는다).</summary>
    public static string Ident(string name) => IdentPart(name);

    /// <summary>표 이름을 검사해 `이름` 또는 `스키마`.`이름` 으로 돌려준다(다른 데이터베이스/스키마의 표를 가리킬 수 있다). 점으로 나뉜 각 부분이 식별자 규칙을 따라야 한다.</summary>
    public static string Table(string name)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        int dot = name.IndexOf('.');
        if (dot < 0)
            return IdentPart(name);
        if (name.IndexOf('.', dot + 1) >= 0)
            throw new ArgumentException("invalid table name: " + name, nameof(name));
        return IdentPart(name.Substring(0, dot)) + "." + IdentPart(name.Substring(dot + 1));
    }

    private static string IdentPart(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 64)
            throw new ArgumentException("invalid identifier: " + name, nameof(name));
        for (int i = 0; i < name.Length; ++i)
        {
            char c = name[i];
            bool ok = c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (i > 0 && c >= '0' && c <= '9');
            if (!ok)
                throw new ArgumentException("invalid identifier: " + name, nameof(name));
        }
        return "`" + name + "`";
    }

    public static string ParamName(int row, int col) => ParamName("", row, col);

    /// <summary>파라미터 이름. prefix 를 주면 한 명령에 여러 문장을 묶을 때 문장끼리 이름이 겹치지 않는다(SqlBatch 가 "s{번호}_" 를 준다).</summary>
    public static string ParamName(string prefix, int row, int col) => "@" + prefix + "r" + row + "c" + col;

    /// <summary>한 문장에 담을 수 있는 행 수: min(maxRows, 파라미터 한도 / 열 수), 최소 1.</summary>
    public static int RowsPerStatement(int columnCount, int maxRows = DefaultMaxRows)
    {
        if (columnCount < 1) throw new ArgumentOutOfRangeException(nameof(columnCount));
        return Math.Max(1, Math.Min(Math.Max(1, maxRows), MaxParameters / columnCount));
    }

    /// <summary>INSERT INTO t (a,b) VALUES (@r0c0,@r0c1),(@r1c0,@r1c1)... 를 만든다.</summary>
    public static string InsertRows(string table, IReadOnlyList<string> columns, int rowCount, string paramPrefix = "")
        => InsertRows(table, columns, rowCount, null, paramPrefix);

    /// <summary>파라미터 이름을 행 우선 순서(행*열수+열)로 만든다. SqlBatch 가 값 목록과 SQL 에 같은 배열을 쓰도록 한 번만 만든다.</summary>
    internal static string[] ParamNames(string prefix, int rowCount, int columnCount)
    {
        string[] names = new string[rowCount * columnCount];
        for (int r = 0; r < rowCount; ++r)
            for (int c = 0; c < columnCount; ++c)
                names[r * columnCount + c] = ParamName(prefix, r, c);
        return names;
    }

    private static string InsertRows(string table, IReadOnlyList<string> columns, int rowCount, string? suffix, string paramPrefix)
        => InsertRowsNamed(table, columns, rowCount, suffix, ParamNames(paramPrefix, rowCount, columns.Count));

    internal static string InsertRowsNamed(string table, IReadOnlyList<string> columns, int rowCount, string? suffix, string[] names)
    {
        if (columns.Count == 0) throw new ArgumentException("no columns", nameof(columns));
        if (rowCount < 1) throw new ArgumentOutOfRangeException(nameof(rowCount));

        StringBuilder sb = new StringBuilder(64 + rowCount * columns.Count * 8);
        sb.Append("INSERT INTO ").Append(Table(table)).Append(" (");
        for (int c = 0; c < columns.Count; ++c)
        {
            if (c != 0) sb.Append(", ");
            sb.Append(Ident(columns[c]));
        }
        sb.Append(") VALUES ");
        for (int r = 0; r < rowCount; ++r)
        {
            if (r != 0) sb.Append(',');
            sb.Append('(');
            for (int c = 0; c < columns.Count; ++c)
            {
                if (c != 0) sb.Append(',');
                sb.Append(names[r * columns.Count + c]);
            }
            sb.Append(')');
        }
        if (suffix != null)
            sb.Append(' ').Append(suffix);
        return sb.ToString();
    }

    /// <summary>
    /// 다중 행 UPSERT: INSERT ... ON DUPLICATE KEY UPDATE 비키열 = VALUES(비키열).
    /// keyColumns 는 갱신하지 않을 열(유일 키를 이루는 열)이다. 갱신할 열이 하나도 없으면 아무것도 바꾸지 않는 갱신(키 = 키)을 붙인다.
    /// </summary>
    public static string UpsertRows(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int rowCount, string paramPrefix = "")
        => UpsertRowsNamed(table, columns, keyColumns, rowCount, ParamNames(paramPrefix, rowCount, columns.Count));

    internal static string UpsertRowsNamed(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, int rowCount, string[] names)
    {
        if (keyColumns.Count == 0) throw new ArgumentException("no key columns", nameof(keyColumns));
        HashSet<string> keys = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);
        foreach (string k in keyColumns)
            if (!Contains(columns, k))
                throw new ArgumentException("key column is not in columns: " + k, nameof(keyColumns));

        StringBuilder upd = new StringBuilder("ON DUPLICATE KEY UPDATE ");
        bool any = false;
        for (int c = 0; c < columns.Count; ++c)
        {
            if (keys.Contains(columns[c])) continue;
            if (any) upd.Append(", ");
            string id = Ident(columns[c]);
            upd.Append(id).Append(" = VALUES(").Append(id).Append(')');
            any = true;
        }
        if (!any)
        {
            string id = Ident(keyColumns[0]);
            upd.Append(id).Append(" = ").Append(id);
        }
        return InsertRowsNamed(table, columns, rowCount, upd.ToString(), names);
    }

    /// <summary>DELETE FROM t WHERE k IN (@k0,@k1,...) 를 만든다. 파라미터 이름은 "@{접두어}k{번호}".</summary>
    public static string DeleteByKeys(string table, string keyColumn, int keyCount, string paramPrefix = "")
    {
        if (keyCount < 1) throw new ArgumentOutOfRangeException(nameof(keyCount));
        StringBuilder sb = new StringBuilder(48 + keyCount * 6);
        sb.Append("DELETE FROM ").Append(Table(table)).Append(" WHERE ").Append(Ident(keyColumn)).Append(" IN (");
        for (int i = 0; i < keyCount; ++i)
        {
            if (i != 0) sb.Append(',');
            sb.Append("@").Append(paramPrefix).Append("k").Append(i);
        }
        return sb.Append(')').ToString();
    }

    private static bool Contains(IReadOnlyList<string> list, string name)
    {
        for (int i = 0; i < list.Count; ++i)
            if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

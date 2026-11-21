// SqlBatch.cs : 여러 문장을 한 명령(한 번의 DB 왕복)으로 묶어 보내기 위한 모음. 텍스트와 파라미터만 다루므로 DB 없이 시험할 수 있다.
// 문장마다 파라미터 이름 접두어("s0_", "s1_", ...)가 달라 한 명령에 합쳐도 이름이 겹치지 않는다. 명령 하나의 파라미터 수와 글자 수가 한도를 넘으면 명령을 나눈다.
// 실행은 MySqlDb.ExecuteBatchAsync. MySQL 은 문장 하나가 실패하면 그 뒤 문장을 실행하지 않고 오류를 낸다(트랜잭션 안에서 부르면 호출자가 롤백한다).
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Data;

public sealed class SqlBatch
{
    /// <summary>명령 하나에 담는 파라미터 수 상한.</summary>
    public const int MaxParametersPerCommand = SqlBuilder.MaxParameters;

    /// <summary>명령 하나의 SQL 글자 수 상한(대략). 값은 클라이언트에서 문장에 끼워 넣어 보내므로 서버의 max_allowed_packet 을 넘기지 않게 한다.</summary>
    public const int MaxCharsPerCommand = 512 * 1024;

    private const int KeyChunk = 500;

    /// <summary>문장 하나: SQL, 파라미터 이름/값(같은 길이의 배열), 글자 수 어림.</summary>
    public sealed class Statement
    {
        public string Sql = "";
        public string[] Names = Array.Empty<string>();
        public object?[] Values = Array.Empty<object?>();
        public int Chars;
    }

    /// <summary>실행할 명령 하나: 합쳐진 SQL 과 그 안의 문장들(파라미터는 문장별로 들고 있어 다시 복사하지 않는다).</summary>
    public sealed class Command
    {
        public string Sql = "";
        public List<Statement> Statements = new List<Statement>();
        public int StatementCount => Statements.Count;
        public int ParameterCount
        {
            get
            {
                int n = 0;
                foreach (Statement s in Statements) n += s.Names.Length;
                return n;
            }
        }
    }

    private readonly List<Statement> _stmts = new List<Statement>();

    public int StatementCount => _stmts.Count;
    public bool IsEmpty => _stmts.Count == 0;

    /// <summary>값 하나가 SQL 글자로 차지할 크기의 어림(문자열은 길이, 바이트 배열은 16진 리터럴 2배, 그 밖은 24자).</summary>
    internal static int EstimateChars(object? v) => v switch
    {
        null => 4,
        string s => s.Length + 2,
        byte[] b => b.Length * 2 + 3,
        _ => 24,
    };

    /// <summary>문장 하나(이미 접두어가 붙은 이름을 쓴 SQL)와 그 파라미터를 더한다. 접두어는 NextPrefix 로 받는다.</summary>
    public SqlBatch AddStatement(string sql, string[] names, object?[] values)
    {
        if (names.Length != values.Length) throw new ArgumentException("names and values differ in length");
        int chars = sql.Length;
        for (int i = 0; i < values.Length; ++i)
            chars += EstimateChars(values[i]);
        _stmts.Add(new Statement { Sql = sql, Names = names, Values = values, Chars = chars });
        return this;
    }

    /// <summary>다음 문장에 쓸 파라미터 이름 접두어.</summary>
    public string NextPrefix => "s" + _stmts.Count + "_";

    private static object?[] RowValues(IReadOnlyList<object?[]> rows, int from, int count, int columnCount)
    {
        object?[] v = new object?[count * columnCount];
        for (int r = 0; r < count; ++r)
        {
            object?[] row = rows[from + r];
            if (row.Length != columnCount)
                throw new ArgumentException("row " + (from + r) + " has " + row.Length + " values, expected " + columnCount);
            Array.Copy(row, 0, v, r * columnCount, columnCount);
        }
        return v;
    }

    /// <summary>다중 행 INSERT(문장당 최대 maxRows 행, 파라미터 한도 안에서)를 더한다.</summary>
    public SqlBatch AddInsertRows(string table, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, int maxRows = SqlBuilder.DefaultMaxRows)
    {
        int per = SqlBuilder.RowsPerStatement(columns.Count, maxRows);
        for (int from = 0; from < rows.Count; from += per)
        {
            int n = Math.Min(per, rows.Count - from);
            string[] names = SqlBuilder.ParamNames(NextPrefix, n, columns.Count);
            AddStatement(SqlBuilder.InsertRowsNamed(table, columns, n, null, names), names, RowValues(rows, from, n, columns.Count));
        }
        return this;
    }

    /// <summary>다중 행 UPSERT 를 더한다. keyColumns 는 갱신하지 않는 키 열이다.</summary>
    public SqlBatch AddUpsertRows(string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns, IReadOnlyList<object?[]> rows, int maxRows = SqlBuilder.DefaultMaxRows)
    {
        int per = SqlBuilder.RowsPerStatement(columns.Count, maxRows);
        for (int from = 0; from < rows.Count; from += per)
        {
            int n = Math.Min(per, rows.Count - from);
            string[] names = SqlBuilder.ParamNames(NextPrefix, n, columns.Count);
            AddStatement(SqlBuilder.UpsertRowsNamed(table, columns, keyColumns, n, names), names, RowValues(rows, from, n, columns.Count));
        }
        return this;
    }

    /// <summary>키 값이 keys 에 있는 행을 지우는 문장(키 500개씩)을 더한다.</summary>
    public SqlBatch AddDeleteByKeys(string table, string keyColumn, IReadOnlyList<object> keys)
    {
        for (int from = 0; from < keys.Count; from += KeyChunk)
        {
            int n = Math.Min(KeyChunk, keys.Count - from);
            string prefix = NextPrefix;
            string[] names = new string[n];
            object?[] values = new object?[n];
            for (int i = 0; i < n; ++i)
            {
                names[i] = "@" + prefix + "k" + i;
                values[i] = keys[from + i];
            }
            AddStatement(SqlBuilder.DeleteByKeys(table, keyColumn, n, prefix), names, values);
        }
        return this;
    }

    /// <summary>
    /// keys 의 행을 모두 지우고 rows 를 다시 넣는다(자식 표를 통째로 갈아 끼울 때). rows 에 키 열도 들어 있어야 한다.
    /// keys 가 비었는데 rows 가 있으면 예외다(넣을 행이 조용히 사라지지 않게). 둘 다 비면 아무것도 더하지 않는다.
    /// </summary>
    public SqlBatch AddReplaceRows(string table, string keyColumn, IReadOnlyList<object> keys, IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        if (keys.Count == 0)
        {
            if (rows.Count != 0)
                throw new ArgumentException("rows were given without any keys to replace", nameof(keys));
            return this;
        }
        AddDeleteByKeys(table, keyColumn, keys);
        return AddInsertRows(table, columns, rows);
    }

    /// <summary>문장들을 한도에 맞춰 명령으로 묶는다. 순서는 더한 순서 그대로다.</summary>
    public List<Command> BuildCommands()
    {
        List<Command> result = new List<Command>();
        StringBuilder sql = new StringBuilder();
        Command? cur = null;
        int paramCount = 0, chars = 0;
        foreach (Statement st in _stmts)
        {
            if (cur != null && (paramCount + st.Names.Length > MaxParametersPerCommand || chars + st.Chars > MaxCharsPerCommand))
            {
                cur.Sql = sql.ToString();
                result.Add(cur);
                cur = null;
            }
            if (cur == null)
            {
                cur = new Command();
                sql.Clear();
                paramCount = 0;
                chars = 0;
            }
            if (cur.Statements.Count != 0)
                sql.Append(";\n");
            sql.Append(st.Sql);
            cur.Statements.Add(st);
            paramCount += st.Names.Length;
            chars += st.Chars;
        }
        if (cur != null)
        {
            cur.Sql = sql.ToString();
            result.Add(cur);
        }
        return result;
    }
}

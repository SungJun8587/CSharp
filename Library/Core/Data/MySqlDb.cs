// MySqlDb.cs : MySqlConnector 위의 얇은 도우미. 커넥션 풀은 MySqlDataSource(연결 문자열 옵션 MaximumPoolSize 등)가 맡는다.
//  - 일시적 오류(교착, 락 대기 초과, 연결 끊김)는 RetryPolicy 로 다시 시도한다. 트랜잭션은 통째로 새 연결에서 다시 한다 -> 작업은 여러 번 실행돼도 결과가 같아야 한다(통째 교체, UPSERT).
//  - 다중 행 INSERT / UPSERT / 키 목록으로 지우고 다시 넣기를 문장 한 개씩으로 묶어 DB 왕복을 줄인다(SqlBuilder).
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;
using MySqlConnector;

namespace Core.Data;

public sealed class MySqlDb : IDisposable
{
    private readonly string _tag;
    private readonly string _connectionString;
    private readonly MySqlDataSource _ds;
    private readonly RetryPolicy _retry;
    private readonly CircuitBreaker _breaker;

    /// <summary>작업 소요 시간/오류/재시도 계측과 느린 쿼리 기록(db_* 지표).</summary>
    public DbMetrics Metrics { get; }

    /// <summary>일시적 오류로 다시 시도한 횟수(누적).</summary>
    public long Retries => Metrics.Retries;

    /// <param name="connectionString">예) "Server=127.0.0.1;Port=3306;Database=game;User ID=game;Password=...;MaximumPoolSize=16;ConnectionTimeout=5;DefaultCommandTimeout=10".
    /// 기본 연결 시간 초과(15초)와 명령 시간 초과(30초)는 재시도와 곱해져 길어지므로 줄여 쓰는 것을 권한다.</param>
    /// <param name="metrics">null 이면 이름(name)으로 머리말을 정해 기본 레지스트리에 올리고 200ms 이상을 느린 작업으로 기록한다.</param>
    /// <param name="breaker">DB 에 닿지 못하는 오류가 연속으로 이어지면 잠시 접속을 시도하지 않고 바로 CircuitOpenException 을 던진다. null 이면 연속 5번 실패에 10초 열림.</param>
    /// <param name="name">이 데이터베이스의 이름. 로그 머리말("[MySqlDb:이름]")과 기본 지표 이름("db_이름_…")에 쓴다. 데이터베이스가 여럿일 때 구분한다. null 이면 이름 없이 "db_…".</param>
    public MySqlDb(string connectionString, RetryPolicy? retry = null, DbMetrics? metrics = null, CircuitBreaker? breaker = null, string? name = null)
    {
        _tag = string.IsNullOrEmpty(name) ? "MySqlDb" : "MySqlDb:" + name;
        _breaker = breaker ?? new CircuitBreaker(5, 10_000);
        Metrics = metrics ?? new DbMetrics(null, 200, null, string.IsNullOrEmpty(name) ? "db" : "db_" + MetricSafe(name), _tag);
        _connectionString = connectionString;
        _ds = new MySqlDataSource(connectionString);
        _retry = retry ?? new RetryPolicy(maxAttempts: 3, baseDelayMs: 100, maxDelayMs: 2000);
    }

    // 지표 이름은 영문/숫자/밑줄만 쓸 수 있어 DB 이름의 그 밖의 글자(예: "game-db")는 _ 로 바꾼다(그대로 두면 생성자가 지표 등록에서 예외를 던진다).
    private static string MetricSafe(string name)
    {
        char[] c = name.ToCharArray();
        for (int i = 0; i < c.Length; ++i)
            if (!((c[i] >= 'a' && c[i] <= 'z') || (c[i] >= 'A' && c[i] <= 'Z') || (c[i] >= '0' && c[i] <= '9') || c[i] == '_'))
                c[i] = '_';
        return new string(c);
    }

    public void Dispose() => _ds.Dispose();

    /// <summary>다시 시도할 만한 오류인가. MySQL 오류 번호(DbErrors) 또는 연결/입출력 오류.</summary>
    public static bool IsTransient(Exception ex)
    {
        if (IsCommandTimeout(ex))
            return false;
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is MySqlException me && (me.IsTransient || DbErrors.IsTransientCode(me.Number)))
                return true;
            if (e is IOException || e is SocketException || e is TimeoutException)
                return true;
        }
        return false;
    }

    /// <summary>회로 상태(Closed 정상, Open 접속 시도 없이 거절, HalfOpen 시험 호출 중).</summary>
    public CircuitState Circuit => _breaker.State;

    /// <summary>명령 시간 초과(클라이언트 CommandTimeout 또는 서버 max_execution_time)인가(안쪽 예외까지 본다). DB 장애도 재시도 대상도 아니다.</summary>
    public static bool IsCommandTimeout(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (DbErrors.IsCommandTimeoutMessage(e.Message))
                return true;
            if (e is MySqlException me && me.Number == DbErrors.QueryTimeout)
                return true;
        }
        return false;
    }

    /// <summary>DB 에 닿지 못한 오류인가(접속 실패/끊김/입출력/시간 초과). 서킷 브레이커가 세는 실패다. 교착, 락 대기 초과, SQL 오류는 아니다.</summary>
    public static bool IsOutage(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is CircuitOpenException)
                return false;
            if (DbErrors.IsPoolExhaustedMessage(e.Message))
                return false;       // 풀 대기 시간 초과: DB 가 죽은 것이 아니라 이쪽 부하(IsPoolExhausted)
            if (DbErrors.IsCommandTimeoutMessage(e.Message) || (e is MySqlException qt && qt.Number == DbErrors.QueryTimeout))
                return false;       // 쿼리가 느린 것(IsCommandTimeout): 연결은 된다
            if (e is MySqlException me)
            {
                if (DbErrors.IsOutageCode(me.Number))
                    return true;
                if (me.Number == 0)       // 서버 오류 번호가 없다 = 서버가 답하기 전의 연결 계층 실패
                    return true;
            }
            if (e is IOException || e is SocketException || e is TimeoutException)
                return true;
        }
        return false;
    }

    /// <summary>풀의 연결이 모두 사용 중이라 기다리다 시간이 초과된 오류인가(안쪽 예외까지 본다). 서킷 브레이커가 세지 않고 재시도하지 않는다.</summary>
    public static bool IsPoolExhausted(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
            if (DbErrors.IsPoolExhaustedMessage(e.Message))
                return true;
        return false;
    }

    /// <summary>시도 하나를 회로를 거쳐 실행한다. 열려 있으면 접속하지 않고 거절한다.</summary>
    private async Task<T> Guarded<T>(Func<Task<T>> attempt, CancellationToken ct)
    {
        if (!_breaker.TryAllow())
        {
            Metrics.Reject();
            throw new CircuitOpenException();
        }
        try
        {
            T r = await attempt().ConfigureAwait(false);
            CircuitState before = _breaker.State;
            _breaker.OnSuccess();
            if (before == CircuitState.HalfOpen)
                LogManager.Info("[{0}] circuit closed (database reachable again)", _tag);
            return r;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                _breaker.OnNeutral();       // 호출자가 취소했다: DB 상태와 무관(서버가 "쿼리 중단"으로 답해도 성공이 아니다)
            else if (IsPoolExhausted(ex))
            {
                Metrics.PoolTimeout();      // 이쪽 부하: 서킷에 세지 않는다
                _breaker.OnNeutral();
            }
            else if (IsCommandTimeout(ex))
                _breaker.OnNeutral();       // 느린 쿼리: DB 장애로도 정상으로도 세지 않는다
            else if (IsOutage(ex))
            {
                CircuitState before = _breaker.State;
                _breaker.OnFailure();
                if (before != CircuitState.Open && _breaker.State == CircuitState.Open)
                    LogManager.Error("[{0}] circuit opened (database unreachable): {1}", _tag, ex.Message);
            }
            else if (ex is MySqlException)
                _breaker.OnSuccess();       // 서버가 응답했다(교착, 제약 위반 등): 닿는다
            else
                _breaker.OnNeutral();
            throw;
        }
    }

    private bool ShouldRetry(Exception ex, CancellationToken ct)
    {
        if (ex is CircuitOpenException || ct.IsCancellationRequested || IsPoolExhausted(ex))
            return false;       // 취소, 서킷 열림, 풀 고갈은 다시 해도 소용없다(풀 고갈은 재시도하면 대기만 늘어난다)
        if (!IsTransient(ex))
            return false;
        Metrics.Retry();
        LogManager.Warning("[{0}] transient error, retrying: {1}", _tag, ex.Message);
        return true;
    }

    /// <summary>재시도 없이 연결 하나를 빌린다(마이그레이션처럼 한 연결에서 세션 상태가 필요한 작업용). 호출자가 닫는다.</summary>
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct)
        => await _ds.OpenConnectionAsync(ct).ConfigureAwait(false);       // 회로를 거치지 않는다

    /// <summary>스크립트 실행용 연결(재시도/회로/풀링 없음). 사용자 변수(@name)를 SQL 에 쓸 수 있도록 AllowUserVariables 를 켜서 연다. 호출자가 닫는다.</summary>
    public async Task<MySqlConnection> OpenScriptConnectionAsync(CancellationToken ct)
    {
        MySqlConnectionStringBuilder b = new MySqlConnectionStringBuilder(_connectionString) { AllowUserVariables = true, Pooling = false };       // 풀링하지 않는다: 닫으면 세션과 그 락이 바로 사라지고 유휴 연결이 남지 않는다
        MySqlConnection conn = new MySqlConnection(b.ConnectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>연결 하나를 빌려 작업을 하고 돌려준다. 일시적 오류면 새 연결에서 작업을 다시 한다(읽기용). op 는 계측/느린 쿼리 로그에 남는 작업 이름이다.</summary>
    public Task<T> WithConnectionAsync<T>(string op, Func<MySqlConnection, CancellationToken, Task<T>> work, CancellationToken ct)
    {
        return _retry.ExecuteAsync(t => Guarded(async () =>
        {
            long t0 = Stopwatch.GetTimestamp();
            bool failed = true;
            try
            {
                await using MySqlConnection conn = await _ds.OpenConnectionAsync(t).ConfigureAwait(false);
                T r = await work(conn, t).ConfigureAwait(false);
                failed = false;
                return r;
            }
            finally
            {
                Metrics.Record(op, Stopwatch.GetElapsedTime(t0).TotalMilliseconds, failed);
            }
        }, ct), ex => ShouldRetry(ex, ct), ct);
    }

    /// <summary>트랜잭션 하나로 작업을 하고 커밋한다. op 는 계측/느린 쿼리 로그에 남는 작업 이름이다. 일시적 오류면 롤백하고 새 연결/새 트랜잭션으로 통째로 다시 한다.</summary>
    /// <param name="isolation">Unspecified 면 서버 기본값(보통 REPEATABLE READ). 여러 행을 지우고 다시 넣는 저장은 ReadCommitted 를 주면 갭 락이 줄어 동시 트랜잭션끼리 교착이 덜 난다(바이너리 로그가 STATEMENT 형식이면 쓸 수 없다).</param>
    public Task InTransactionAsync(string op, Func<MySqlConnection, MySqlTransaction, CancellationToken, Task> work, CancellationToken ct, IsolationLevel isolation = IsolationLevel.Unspecified)
    {
        return _retry.ExecuteAsync<bool>(t => Guarded(async () =>
        {
            long t0 = Stopwatch.GetTimestamp();
            bool failed = true;
            try
            {
                await using MySqlConnection conn = await _ds.OpenConnectionAsync(t).ConfigureAwait(false);
                await using MySqlTransaction tx = isolation == IsolationLevel.Unspecified
                    ? await conn.BeginTransactionAsync(t).ConfigureAwait(false)
                    : await conn.BeginTransactionAsync(isolation, t).ConfigureAwait(false);
                await work(conn, tx, t).ConfigureAwait(false);
                await tx.CommitAsync(t).ConfigureAwait(false);
                failed = false;
                return true;
            }
            finally
            {
                Metrics.Record(op, Stopwatch.GetElapsedTime(t0).TotalMilliseconds, failed);
            }
        }, ct), ex => ShouldRetry(ex, ct), ct);
    }

    private static void Bind(MySqlCommand cmd, IReadOnlyList<object?[]> rows, int from, int count, int columnCount)
    {
        for (int r = 0; r < count; ++r)
        {
            object?[] row = rows[from + r];
            if (row.Length != columnCount)
                throw new ArgumentException("row " + (from + r) + " has " + row.Length + " values, expected " + columnCount);
            for (int c = 0; c < columnCount; ++c)
                cmd.Parameters.AddWithValue(SqlBuilder.ParamName(r, c), row[c] ?? DBNull.Value);
        }
    }

    /// <summary>
    /// SqlBatch 의 문장들을 순서대로 실행한다. 한도(파라미터 수/글자 수) 안에서는 한 명령(= DB 왕복 한 번)으로 보낸다.
    /// commandTimeoutSec 가 0 보다 크면 이 호출의 명령 시간 초과를 연결 문자열 기본값 대신 그 값으로 한다(대량 작업용).
    /// 영향 행 수의 합을 돌려준다(UPSERT 의 "바뀌지 않은 행"을 세느냐는 연결 설정에 따라 다르다).
    /// </summary>
    public static async Task<int> ExecuteBatchAsync(MySqlConnection conn, MySqlTransaction? tx, SqlBatch batch, CancellationToken ct, int commandTimeoutSec = 0)
    {
        int total = 0;
        foreach (SqlBatch.Command c in batch.BuildCommands())
        {
            await using MySqlCommand cmd = new MySqlCommand(c.Sql, conn, tx);
            if (commandTimeoutSec > 0)
                cmd.CommandTimeout = commandTimeoutSec;
            foreach (SqlBatch.Statement st in c.Statements)
                for (int i = 0; i < st.Names.Length; ++i)
                    cmd.Parameters.AddWithValue(st.Names[i], st.Values[i] ?? DBNull.Value);
            total += await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        return total;
    }

    /// <summary>행들을 다중 행 INSERT 로 넣는다(문장당 최대 maxRows 행, 파라미터 한도 안에서, 문장들은 한 명령으로). 서버가 알려 준 영향 행 수의 합을 돌려준다.</summary>
    public static Task<int> InsertRowsAsync(MySqlConnection conn, MySqlTransaction? tx, string table, IReadOnlyList<string> columns,
        IReadOnlyList<object?[]> rows, CancellationToken ct, int maxRows = SqlBuilder.DefaultMaxRows, int commandTimeoutSec = 0)
        => ExecuteBatchAsync(conn, tx, new SqlBatch().AddInsertRows(table, columns, rows, maxRows), ct, commandTimeoutSec);

    /// <summary>행들을 다중 행 UPSERT 로 넣는다. keyColumns 는 갱신하지 않는 키 열이다. 돌려주는 값은 영향 행 수인데, 연결 설정(UseAffectedRows)에 따라 "값이 같아 바뀌지 않은 행"을 세느냐가 달라서
    /// 새로 들어간 행과 이미 있던 행을 구별하는 데 쓰면 안 된다(그럴 때는 TryInsertAsync).</summary>
    public static Task<int> UpsertRowsAsync(MySqlConnection conn, MySqlTransaction? tx, string table, IReadOnlyList<string> columns, IReadOnlyList<string> keyColumns,
        IReadOnlyList<object?[]> rows, CancellationToken ct, int maxRows = SqlBuilder.DefaultMaxRows, int commandTimeoutSec = 0)
        => ExecuteBatchAsync(conn, tx, new SqlBatch().AddUpsertRows(table, columns, keyColumns, rows, maxRows), ct, commandTimeoutSec);

    /// <summary>
    /// 행 하나를 넣는다. 같은 키가 이미 있으면(오류 1062) 넣지 않고 false. 거래 ID 처럼 "한 번만 처리"해야 하는 멱등 기록에 쓴다.
    /// 트랜잭션 안에서 불러도 InnoDB 는 실패한 문장만 되돌리므로 트랜잭션은 계속 쓸 수 있다. 그 밖의 오류는 그대로 던진다.
    /// </summary>
    public static async Task<bool> TryInsertAsync(MySqlConnection conn, MySqlTransaction? tx, string table, IReadOnlyList<string> columns, object?[] row, CancellationToken ct)
    {
        if (row.Length != columns.Count)
            throw new ArgumentException("row has " + row.Length + " values, expected " + columns.Count, nameof(row));
        await using MySqlCommand cmd = new MySqlCommand(SqlBuilder.InsertRows(table, columns, 1), conn, tx);
        Bind(cmd, new[] { row }, 0, 1, columns.Count);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (MySqlException e) when (e.Number == DbErrors.DuplicateEntry)
        {
            return false;
        }
    }

    /// <summary>
    /// 키 값이 keys 에 있는 행을 모두 지우고 rows 를 다시 넣는다(자식 표를 통째로 갈아 끼울 때). rows 에는 키 열도 들어 있어야 한다.
    /// 지우는 문장은 키 500개씩, 넣는 문장은 행 500개씩 묶고 모두 한 명령으로 보낸다. 같은 트랜잭션 안에서 부르면 중간 상태는 보이지 않는다.
    /// </summary>
    public static async Task ReplaceRowsAsync(MySqlConnection conn, MySqlTransaction? tx, string table, string keyColumn, IReadOnlyList<object> keys,
        IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, CancellationToken ct, int commandTimeoutSec = 0)
        => await ExecuteBatchAsync(conn, tx, new SqlBatch().AddReplaceRows(table, keyColumn, keys, columns, rows), ct, commandTimeoutSec).ConfigureAwait(false);
}

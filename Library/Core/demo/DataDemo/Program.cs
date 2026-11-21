// Core.Data 통합 시험(실제 MySQL 필요). 환경변수 CORE_DATA_TEST_DB 가 없으면 DB 가 필요 없는 시험만 돈다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Data;
using Core.Util;
using MySqlConnector;

namespace Core.DataDemo;

public static class Program
{
    private static int s_pass, s_fail;

    private static void Check(bool ok, string what)
    {
        if (ok) { ++s_pass; return; }
        ++s_fail;
        Console.WriteLine("  FAIL: " + what);
    }

    public static async Task<int> Main()
    {
        try { await TestOutageNeedsNoDb(); }
        catch (Exception e) { ++s_fail; Console.WriteLine("  FAIL (exception) outage: " + e); }

        string? cs = Environment.GetEnvironmentVariable("CORE_DATA_TEST_DB");
        if (string.IsNullOrEmpty(cs))
            Console.WriteLine("CORE_DATA_TEST_DB 가 없어 DB 시험은 건너뜁니다.");
        else
        {
            try { await RunDbTests(cs); }
            catch (Exception e) { ++s_fail; Console.WriteLine("  FAIL (exception) db tests: " + e); }
        }

        Console.WriteLine("passed " + s_pass + ", failed " + s_fail);
        return s_fail == 0 ? 0 : 1;
    }

    // ---- DB 없이: 접속 불가가 연속되면 서킷이 열려 접속 시도 없이 바로 거절한다 ----
    private static async Task TestOutageNeedsNoDb()
    {
        DbMetrics m = new DbMetrics(new MetricRegistry(), 0, null, "t_out");
        using MySqlDb db = new MySqlDb("Server=127.0.0.1;Port=1;Database=x;User ID=u;Password=p;ConnectionTimeout=2;Pooling=false",
            new RetryPolicy(maxAttempts: 1), m, new CircuitBreaker(3, 60_000));

        int outage = 0;
        for (int i = 0; i < 3; ++i)
        {
            try { await db.WithConnectionAsync("t", (c, t) => Task.FromResult(1), CancellationToken.None); }
            catch (Exception e) when (!(e is CircuitOpenException)) { if (MySqlDb.IsOutage(e)) ++outage; else Console.WriteLine("  (not classified as outage: " + e.GetType().Name + " " + (e as MySqlException)?.Number + ")"); }
        }
        Check(outage == 3, "unreachable server counts as outage (3 of 3)");
        Check(db.Circuit == CircuitState.Open, "circuit opens after the threshold");

        Stopwatch sw = Stopwatch.StartNew();
        bool rejected = false;
        try { await db.WithConnectionAsync("t", (c, t) => Task.FromResult(1), CancellationToken.None); }
        catch (CircuitOpenException) { rejected = true; }
        Check(rejected && sw.ElapsedMilliseconds < 200 && m.Rejected == 1, "open circuit rejects immediately without connecting");

        bool txRejected = false;
        try { await db.InTransactionAsync("t", (c, tx, t) => Task.CompletedTask, CancellationToken.None); }
        catch (CircuitOpenException) { txRejected = true; }
        Check(txRejected && m.Rejected == 2, "transactions are rejected too");
    }

    // ---- 실제 DB ----
    // 풀이 가득 차 연결을 못 얻는 것은 DB 장애가 아니다: 서킷이 열리면 안 되고 지표만 올라간다.
    private static async Task TestPoolExhaustion(string cs)
    {
        MySqlConnectionStringBuilder b = new MySqlConnectionStringBuilder(cs) { MaximumPoolSize = 1, MinimumPoolSize = 0, ConnectionTimeout = 1, Pooling = true, ConnectionReset = false };
        DbMetrics m = new DbMetrics(new MetricRegistry(), 0, null, "t_pool");
        using MySqlDb pdb = new MySqlDb(b.ConnectionString, new RetryPolicy(maxAttempts: 1), m, new CircuitBreaker(2, 60_000), "pooltest");
        Task<int> hold = pdb.WithConnectionAsync("hold", async (c, t) => { await using MySqlCommand q = new MySqlCommand("SELECT SLEEP(3)", c); await q.ExecuteScalarAsync(t); return 1; }, CancellationToken.None);
        await Task.Delay(300);
        Exception? err = null;
        for (int i = 0; i < 3; ++i)
        {
            try { await pdb.WithConnectionAsync("wait", (c, t) => Task.FromResult(1), CancellationToken.None); }
            catch (Exception e) { err = e; }
        }
        await hold;
        Check(err != null && MySqlDb.IsPoolExhausted(err) && !MySqlDb.IsOutage(err), "pool exhaustion is detected and is not an outage (" + err?.GetType().Name + ": " + err?.Message + ")");
        Check(pdb.Circuit == CircuitState.Closed && m.PoolExhausted >= 1, "pool exhaustion leaves the circuit closed and counts " + m.PoolExhausted);
    }

    // 명령 시간 초과는 느린 쿼리일 뿐: 장애로 세지 않고(서킷 닫힘) 재시도하지 않는다. 호출별 시간 초과도 확인한다.
    private static async Task TestCommandTimeout(string cs)
    {
        DbMetrics m = new DbMetrics(new MetricRegistry(), 0, null, "t_cto");
        using MySqlDb tdb = new MySqlDb(cs, new RetryPolicy(maxAttempts: 3, baseDelayMs: 10, maxDelayMs: 20), m, new CircuitBreaker(2, 60_000), "ctotest");
        int attempts = 0;
        Exception? err = null;
        for (int i = 0; i < 3; ++i)
        {
            try
            {
                await tdb.WithConnectionAsync("slow", async (c, t) =>
                {
                    ++attempts;
                    await using MySqlCommand q = new MySqlCommand("SELECT SLEEP(3)", c) { CommandTimeout = 1 };
                    return await q.ExecuteScalarAsync(t);
                }, CancellationToken.None);
            }
            catch (Exception e) { err = e; }
        }
        Check(err != null && MySqlDb.IsCommandTimeout(err) && !MySqlDb.IsOutage(err) && !MySqlDb.IsTransient(err), "command timeout is detected and is neither outage nor transient (" + err?.GetType().Name + ": " + err?.Message + ")");
        Check(attempts == 3 && tdb.Circuit == CircuitState.Closed && tdb.Retries == 0, "command timeout: no retry, circuit closed (attempts " + attempts + ")");

        await using MySqlConnection conn = await tdb.OpenAsync(CancellationToken.None);
        bool timedOut = false;
        try { await MySqlDb.ExecuteBatchAsync(conn, null, new SqlBatch().AddStatement("SELECT SLEEP(3)", Array.Empty<string>(), Array.Empty<object?>()), CancellationToken.None, commandTimeoutSec: 1); }
        catch (Exception e) when (MySqlDb.IsCommandTimeout(e)) { timedOut = true; }
        Check(timedOut, "per-call commandTimeoutSec is applied to batch commands");
    }

    private static async Task Exec(MySqlConnection c, string sql)
    {
        await using MySqlCommand cmd = new MySqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(MySqlConnection c, string sql)
    {
        await using MySqlCommand cmd = new MySqlCommand(sql, c);
        object? r = await cmd.ExecuteScalarAsync();
        return r == null || r is DBNull ? -1 : Convert.ToInt64(r);
    }

    private static async Task RunDbTests(string cs)
    {
        List<string> slowLog = new List<string>();
        DbMetrics metrics = new DbMetrics(new MetricRegistry(), 20, slowLog.Add, "t_db");
        using MySqlDb db = new MySqlDb(cs, new RetryPolicy(maxAttempts: 4, baseDelayMs: 20, maxDelayMs: 100), metrics);

        // 시험 전용 DB 인지 확인(이름에 test). 아니면 아무것도 지우지 않고 멈춘다.
        string dbName;
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            await using MySqlCommand q = new MySqlCommand("SELECT DATABASE()", c);
            dbName = (await q.ExecuteScalarAsync())?.ToString() ?? "";
        }
        if (dbName.IndexOf("test", StringComparison.OrdinalIgnoreCase) < 0)
        {
            ++s_fail;
            Console.WriteLine("  FAIL: refusing to run, database '" + dbName + "' does not look like a test database (name must contain 'test')");
            return;
        }

        await Cleanup(db);
        try
        {
            await TestRows(db);
            await TestTransactions(db);
            await TestDeadlockRetry(db);
            await TestMigrations(db);
            await TestMetrics(db, metrics, slowLog);
            await TestPoolExhaustion(cs);
            await TestCommandTimeout(cs);
        }
        finally
        {
            await Cleanup(db);
        }
    }

    private static async Task Cleanup(MySqlDb db)
    {
        await using MySqlConnection c = await db.OpenAsync(CancellationToken.None);
        foreach (string t in new[] { "cdt_kv", "cdt_child", "cdt_m1", "cdt_m2", "cdt_m3", "cdt_m4", "schema_version" })
            await Exec(c, "DROP TABLE IF EXISTS `" + t + "`");
    }

    private static readonly string[] KvCols = { "id", "v", "note" };
    private static readonly string[] ChildCols = { "owner", "slot", "val" };

    private static async Task TestRows(MySqlDb db)
    {
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            await Exec(c, "CREATE TABLE cdt_kv (id BIGINT UNSIGNED NOT NULL PRIMARY KEY, v INT NOT NULL, note VARCHAR(20) NULL) ENGINE=InnoDB");
            await Exec(c, "CREATE TABLE cdt_child (owner BIGINT UNSIGNED NOT NULL, slot INT NOT NULL, val INT NOT NULL, PRIMARY KEY (owner, slot)) ENGINE=InnoDB");
        }

        // 1200행: 한 문장 500행씩 3문장이 한 명령으로 간다.
        List<object?[]> rows = new List<object?[]>();
        for (int i = 1; i <= 1200; ++i) rows.Add(new object?[] { (ulong)i, i, i % 2 == 0 ? "even" : null });
        await db.InTransactionAsync("t.insert", async (c, tx, t) =>
        {
            int n = await MySqlDb.InsertRowsAsync(c, tx, "cdt_kv", KvCols, rows, t);
            Check(n == 1200, "InsertRows reports 1200 affected rows (got " + n + ")");
        }, CancellationToken.None);
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            Check(await Scalar(c, "SELECT COUNT(*) FROM cdt_kv") == 1200, "1200 rows inserted");
            Check(await Scalar(c, "SELECT COUNT(*) FROM cdt_kv WHERE note IS NULL") == 600, "null values are stored as NULL");
        }

        // UPSERT: 기존 행은 비키 열만 바뀌고, 새 행은 들어간다.
        await db.InTransactionAsync("t.upsert", async (c, tx, t) =>
        {
            await MySqlDb.UpsertRowsAsync(c, tx, "cdt_kv", KvCols, new[] { "id" }, new List<object?[]> { new object?[] { 1UL, 111, "x" }, new object?[] { 5000UL, 5, null } }, t);
        }, CancellationToken.None);
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            Check(await Scalar(c, "SELECT v FROM cdt_kv WHERE id = 1") == 111 && await Scalar(c, "SELECT COUNT(*) FROM cdt_kv WHERE id = 5000") == 1 && await Scalar(c, "SELECT COUNT(*) FROM cdt_kv") == 1201, "UpsertRows updates existing and inserts new rows");
        }

        // ReplaceRows: 1번 주인은 새 행으로, 2번 주인은 비움, 3번 주인은 그대로.
        await db.InTransactionAsync("t.seed", async (c, tx, t) =>
        {
            await MySqlDb.InsertRowsAsync(c, tx, "cdt_child", ChildCols, new List<object?[]>
            {
                new object?[] { 1UL, 0, 10 }, new object?[] { 1UL, 1, 11 }, new object?[] { 2UL, 0, 20 }, new object?[] { 3UL, 0, 30 },
            }, t);
        }, CancellationToken.None);
        await db.InTransactionAsync("t.replace", async (c, tx, t) =>
        {
            await MySqlDb.ReplaceRowsAsync(c, tx, "cdt_child", "owner", new List<object> { 1UL, 2UL }, ChildCols,
                new List<object?[]> { new object?[] { 1UL, 7, 77 } }, t);
        }, CancellationToken.None);
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            Check(await Scalar(c, "SELECT COUNT(*) FROM cdt_child WHERE owner = 1") == 1 && await Scalar(c, "SELECT val FROM cdt_child WHERE owner = 1 AND slot = 7") == 77, "ReplaceRows replaces the owner's rows");
            Check(await Scalar(c, "SELECT COUNT(*) FROM cdt_child WHERE owner = 2") == 0 && await Scalar(c, "SELECT COUNT(*) FROM cdt_child WHERE owner = 3") == 1, "ReplaceRows empties an owner without rows and leaves other owners alone");
        }

        // 스키마 한정 이름: 현재 DB 이름으로 지정해도 같은 표에 닿는다.
        string dbName;
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
        {
            await using MySqlCommand q = new MySqlCommand("SELECT DATABASE()", c);
            dbName = (await q.ExecuteScalarAsync())?.ToString() ?? "";
        }
        await db.InTransactionAsync("t.qualified", async (c, tx, t) =>
        {
            await MySqlDb.UpsertRowsAsync(c, tx, dbName + ".cdt_kv", KvCols, new[] { "id" }, new List<object?[]> { new object?[] { 2UL, 222, null } }, t);
        }, CancellationToken.None);
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
            Check(await Scalar(c, "SELECT v FROM cdt_kv WHERE id = 2") == 222, "schema-qualified table name works");
    }

    private static async Task TestTransactions(MySqlDb db)
    {
        // TryInsert: 처음엔 true, 같은 키는 false. 트랜잭션은 계속 쓸 수 있다.
        bool first = false, dup = true, after = false;
        await db.InTransactionAsync("t.tryinsert", async (c, tx, t) =>
        {
            first = await MySqlDb.TryInsertAsync(c, tx, "cdt_kv", KvCols, new object?[] { 9001UL, 1, null }, t);
            dup = await MySqlDb.TryInsertAsync(c, tx, "cdt_kv", KvCols, new object?[] { 9001UL, 2, null }, t);
            after = await MySqlDb.TryInsertAsync(c, tx, "cdt_kv", KvCols, new object?[] { 9002UL, 3, null }, t);
        }, CancellationToken.None);
        Check(first && !dup && after, "TryInsert: true, then false on duplicate, transaction still usable");
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
            Check(await Scalar(c, "SELECT v FROM cdt_kv WHERE id = 9001") == 1 && await Scalar(c, "SELECT COUNT(*) FROM cdt_kv WHERE id = 9002") == 1, "duplicate TryInsert changed nothing");

        // 작업이 예외를 던지면 롤백되고, 일시적이 아닌 오류는 재시도하지 않는다.
        long retries0 = db.Retries;
        bool threw = false;
        try
        {
            await db.InTransactionAsync("t.rollback", async (c, tx, t) =>
            {
                await MySqlDb.InsertRowsAsync(c, tx, "cdt_kv", KvCols, new List<object?[]> { new object?[] { 9100UL, 1, null } }, t);
                throw new InvalidOperationException("boom");
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { threw = true; }
        await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
            Check(threw && await Scalar(c, "SELECT COUNT(*) FROM cdt_kv WHERE id = 9100") == 0 && db.Retries == retries0, "failed transaction is rolled back and not retried");

        // 격리 수준 지정
        long level = -1;
        await db.InTransactionAsync("t.iso", async (c, tx, t) =>
        {
            await using MySqlCommand q = new MySqlCommand("SELECT @@transaction_isolation = 'READ-COMMITTED'", c, tx);
            level = Convert.ToInt64(await q.ExecuteScalarAsync(t));
        }, CancellationToken.None, System.Data.IsolationLevel.ReadCommitted);
        Check(level == 1, "isolation level is applied");
    }

    // 두 트랜잭션이 서로 반대 순서로 같은 두 행을 잠가 교착(1213)을 만든다. 희생된 쪽이 통째로 다시 실행돼 둘 다 성공해야 한다.
    private static async Task TestDeadlockRetry(MySqlDb db)
    {
        await db.InTransactionAsync("t.dl.seed", async (c, tx, t) =>
        {
            await MySqlDb.UpsertRowsAsync(c, tx, "cdt_kv", KvCols, new[] { "id" }, new List<object?[]> { new object?[] { 100UL, 0, null }, new object?[] { 200UL, 0, null } }, t);
        }, CancellationToken.None);

        long retries0 = db.Retries;
        TaskCompletionSource<bool> aLocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> bLocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int aTries = 0, bTries = 0;

        async Task Run(ulong first, ulong second, TaskCompletionSource<bool> mine, TaskCompletionSource<bool> other, Action tried)
        {
            int attempt = 0;
            await db.InTransactionAsync("t.dl." + first, async (c, tx, t) =>
            {
                ++attempt; tried();
                await using (MySqlCommand u1 = new MySqlCommand("UPDATE cdt_kv SET v = v + 1 WHERE id = " + first, c, tx))
                    await u1.ExecuteNonQueryAsync(t);
                if (attempt == 1)
                {
                    mine.TrySetResult(true);
                    await Task.WhenAny(other.Task, Task.Delay(5000));       // 서로 첫 행을 잠글 때까지 기다린 뒤 상대 행을 건드린다
                }
                await using (MySqlCommand u2 = new MySqlCommand("UPDATE cdt_kv SET v = v + 1 WHERE id = " + second, c, tx))
                    await u2.ExecuteNonQueryAsync(t);
            }, CancellationToken.None);
        }

        await Task.WhenAll(Run(100, 200, aLocked, bLocked, () => ++aTries), Run(200, 100, bLocked, aLocked, () => ++bTries));
        await using MySqlConnection cc = await db.OpenAsync(CancellationToken.None);
        Check(await Scalar(cc, "SELECT v FROM cdt_kv WHERE id = 100") == 2 && await Scalar(cc, "SELECT v FROM cdt_kv WHERE id = 200") == 2, "deadlock victim re-ran the whole transaction: both rows incremented exactly twice");
        Check(db.Retries > retries0 && aTries + bTries >= 3, "a deadlock was retried (tries " + aTries + "+" + bTries + ", retries +" + (db.Retries - retries0) + ")");
    }

    private static string NewDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "cdt_mig_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(d);
        return d;
    }

    private static async Task TestMigrations(MySqlDb db)
    {
        string dir = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "001_m1.sql"), "-- 첫 표\nCREATE TABLE IF NOT EXISTS cdt_m1 (id INT NOT NULL PRIMARY KEY, name VARCHAR(10) DEFAULT 'a;b');\nINSERT INTO cdt_m1 (id) VALUES (1);\n");
            // 사용자 변수 + PREPARE: AllowUserVariables 전용 연결이 있어야 돈다. 열이 이미 있으면 아무것도 하지 않는 형태.
            File.WriteAllText(Path.Combine(dir, "002_m2.sql"),
                "CREATE TABLE IF NOT EXISTS cdt_m2 (id INT NOT NULL PRIMARY KEY);\n" +
                "SET @sql = (SELECT IF(COUNT(*) = 0, 'ALTER TABLE cdt_m2 ADD COLUMN extra INT NOT NULL DEFAULT 5', 'DO 0') FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cdt_m2' AND COLUMN_NAME = 'extra');\n" +
                "PREPARE st FROM @sql;\nEXECUTE st;\nDEALLOCATE PREPARE st;\n");
            File.WriteAllText(Path.Combine(dir, "003_m3.sql"), "CREATE TABLE IF NOT EXISTS cdt_m3 (id INT NOT NULL PRIMARY KEY); /* 끝 */");

            int n1 = await MigrationRunner.MigrateAsync(db, dir, CancellationToken.None);
            Check(n1 == 3, "migration applies all 3 scripts (got " + n1 + ")");
            int n2 = await MigrationRunner.MigrateAsync(db, dir, CancellationToken.None);
            Check(n2 == 0, "second run applies nothing");
            await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
            {
                Check(await Scalar(c, "SELECT COUNT(*) FROM schema_version") == 3, "schema_version has 3 rows");
                Check(await Scalar(c, "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'cdt_m2' AND COLUMN_NAME = 'extra'") == 1, "conditional ALTER with user variables ran");
                Check(await Scalar(c, "SELECT COUNT(*) FROM cdt_m1 WHERE name = 'a;b'") == 1, "semicolon inside a string literal survived");
            }

            File.AppendAllText(Path.Combine(dir, "001_m1.sql"), "-- 바뀜\n");
            bool mismatch = false;
            try { await MigrationRunner.MigrateAsync(db, dir, CancellationToken.None); }
            catch (InvalidOperationException) { mismatch = true; }
            Check(mismatch, "changed script after apply is rejected (checksum)");
            File.WriteAllText(Path.Combine(dir, "001_m1.sql"), "-- 첫 표\nCREATE TABLE IF NOT EXISTS cdt_m1 (id INT NOT NULL PRIMARY KEY, name VARCHAR(10) DEFAULT 'a;b');\nINSERT INTO cdt_m1 (id) VALUES (1);\n");
            File.WriteAllText(Path.Combine(dir, "004_m4.sql"), "CREATE TABLE IF NOT EXISTS cdt_m4 (id INT NOT NULL PRIMARY KEY);");
            Check(await MigrationRunner.MigrateAsync(db, dir, CancellationToken.None) == 1, "restored script passes and the new version applies");

            // 동시에 시작해도 한 번씩만 적용된다.
            await using (MySqlConnection c = await db.OpenAsync(CancellationToken.None))
                foreach (string t in new[] { "cdt_m1", "cdt_m2", "cdt_m3", "cdt_m4", "schema_version" })
                    await Exec(c, "DROP TABLE IF EXISTS `" + t + "`");
            Task<int>[] both = { MigrationRunner.MigrateAsync(db, dir, CancellationToken.None), MigrationRunner.MigrateAsync(db, dir, CancellationToken.None) };
            int[] r = await Task.WhenAll(both);
            Check(r.Sum() == 4 && r.Min() >= 0, "two concurrent runners apply each script exactly once (applied " + r[0] + " + " + r[1] + ")");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private static async Task TestMetrics(MySqlDb db, DbMetrics m, List<string> slowLog)
    {
        long slow0 = m.Slow;
        await db.WithConnectionAsync("t.slow", async (c, t) =>
        {
            await using MySqlCommand q = new MySqlCommand("SELECT SLEEP(0.06)", c);
            return await q.ExecuteScalarAsync(t);
        }, CancellationToken.None);
        Check(m.Ops > 0 && m.Slow == slow0 + 1 && slowLog.Any(x => x.Contains("op=t.slow")), "slow query is counted and logged with its operation name");
    }
}

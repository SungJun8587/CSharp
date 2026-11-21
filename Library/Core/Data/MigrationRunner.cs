// MigrationRunner.cs : 스키마 마이그레이션 러너. 스크립트(NNN_이름.sql)를 버전 순으로 적용한다.
//  - 적용 기록은 버전 표(기본 schema_version: version, name, checksum, applied_at)에 남긴다. 이미 적용한 버전은 건너뛴다.
//  - 이미 적용한 파일의 내용이 바뀌었으면(체크섬 불일치) 예외를 던진다. 고치려면 새 버전 파일을 추가한다.
//  - 여러 서버가 동시에 시작해도 한 곳만 적용하도록 GET_LOCK 으로 막는다(락 이름에 데이터베이스 이름이 들어가 다른 데이터베이스끼리는 막지 않는다).
//  - 스크립트 안에서 사용자 변수(@name, PREPARE 등)를 쓸 수 있도록 전용 연결(AllowUserVariables, 풀링 없음)로 실행한다.
//  - DDL 은 MySQL 에서 암묵 커밋되므로 스크립트 하나는 통째로 롤백되지 않는다. 스크립트는 다시 실행해도 안전하게(IF NOT EXISTS 등) 쓴다. 중간에 실패하면 그 버전은 기록되지 않고 다음 시작 때 처음부터 다시 실행된다.
//  - 한 데이터베이스에 모듈이 여럿이면 versionTable 을 모듈마다 다르게 준다.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core.Util;
using MySqlConnector;

namespace Core.Data;

public static class MigrationRunner
{
    public const string DefaultVersionTable = "schema_version";

    /// <summary>폴더의 스크립트를 적용한다. 새로 적용한 개수를 돌려준다.</summary>
    public static Task<int> MigrateAsync(MySqlDb db, string dir, CancellationToken ct, int lockTimeoutSec = 60, string versionTable = DefaultVersionTable, int statementTimeoutSec = 300)
        => MigrateAsync(db, MigrationScripts.FromDirectory(dir), ct, lockTimeoutSec, versionTable, statementTimeoutSec);

    /// <summary>스크립트 목록을 적용한다(폴더 또는 어셈블리 리소스에서 만든 것).</summary>
    public static async Task<int> MigrateAsync(MySqlDb db, IReadOnlyList<MigrationScript> scripts, CancellationToken ct, int lockTimeoutSec = 60, string versionTable = DefaultVersionTable, int statementTimeoutSec = 300)
    {
        string table = SqlBuilder.Table(versionTable);
        await using MySqlConnection conn = await db.OpenScriptConnectionAsync(ct).ConfigureAwait(false);

        string dbName = "";
        await using (MySqlCommand q = new MySqlCommand("SELECT DATABASE()", conn))
        {
            object? r = await q.ExecuteScalarAsync(ct).ConfigureAwait(false);
            dbName = r == null || r is DBNull ? "" : Convert.ToString(r) ?? "";
        }
        string lockName = ("core_data_migration:" + dbName + ":" + versionTable);
        if (lockName.Length > 64)
            lockName = lockName.Substring(0, 64);

        long got;
        await using (MySqlCommand lk = new MySqlCommand("SELECT GET_LOCK(@n, @t)", conn))
        {
            lk.Parameters.AddWithValue("@n", lockName);
            lk.Parameters.AddWithValue("@t", lockTimeoutSec);
            object? r = await lk.ExecuteScalarAsync(ct).ConfigureAwait(false);
            got = r == null || r is DBNull ? 0 : Convert.ToInt64(r);
        }
        if (got != 1)
            throw new TimeoutException("could not acquire schema migration lock within " + lockTimeoutSec + "s");

        try
        {
            await Exec(conn, "CREATE TABLE IF NOT EXISTS " + table + " (" +
                "version INT NOT NULL, name VARCHAR(128) NOT NULL, checksum CHAR(64) NOT NULL, " +
                "applied_at DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3), PRIMARY KEY (version)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", ct, statementTimeoutSec).ConfigureAwait(false);

            Dictionary<int, string> applied = new Dictionary<int, string>();
            await using (MySqlCommand q = new MySqlCommand("SELECT version, checksum FROM " + table, conn))
            await using (MySqlDataReader rd = await q.ExecuteReaderAsync(ct).ConfigureAwait(false))
                while (await rd.ReadAsync(ct).ConfigureAwait(false))
                    applied[rd.GetInt32(0)] = rd.GetString(1);

            int count = 0;
            foreach (MigrationScript s in scripts)
            {
                if (applied.TryGetValue(s.Version, out string? sum))
                {
                    if (!string.Equals(sum, s.Checksum, StringComparison.Ordinal))
                        throw new InvalidOperationException("migration " + s.Version + "_" + s.Name + " was changed after it was applied (checksum mismatch); add a new version instead");
                    continue;
                }

                LogManager.Info("[Migration] applying {0}_{1}", s.Version, s.Name);
                foreach (string stmt in SqlScript.Split(s.Text))
                    await Exec(conn, stmt, ct, statementTimeoutSec).ConfigureAwait(false);

                await using (MySqlCommand ins = new MySqlCommand("INSERT INTO " + table + " (version, name, checksum) VALUES (@v, @n, @c)", conn))
                {
                    ins.Parameters.AddWithValue("@v", s.Version);
                    ins.Parameters.AddWithValue("@n", s.Name);
                    ins.Parameters.AddWithValue("@c", s.Checksum);
                    await ins.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                ++count;
            }
            return count;
        }
        finally
        {
            try
            {
                await using MySqlCommand rel = new MySqlCommand("SELECT RELEASE_LOCK(@n)", conn);
                rel.Parameters.AddWithValue("@n", lockName);
                await rel.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogManager.Warning("[Migration] release lock failed: {0}", e.Message);       // 연결을 닫으면(풀링 없음) 락은 어차피 사라진다
            }
        }
    }

    private static async Task Exec(MySqlConnection conn, string sql, CancellationToken ct, int timeoutSec)
    {
        await using MySqlCommand cmd = new MySqlCommand(sql, conn);
        cmd.CommandTimeout = timeoutSec;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

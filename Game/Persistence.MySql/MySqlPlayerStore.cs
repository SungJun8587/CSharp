// MySqlPlayerStore.cs : Dapper + MySqlConnector 로 만든 IPlayerStore. 스키마는 Persistence/Schema/001_players.sql.
// 커넥션 풀은 MySqlConnector 가 연결 문자열 옵션(MaximumPoolSize, MinimumPoolSize, ConnectionIdleTimeout 등)으로 관리한다.
// 예) "Server=127.0.0.1;Port=3306;Database=game;User ID=game;Password=...;MaximumPoolSize=16;Pooling=true"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Game.ZoneCommon;
using MySqlConnector;

namespace Game.Persistence.MySql;

public sealed class MySqlPlayerStore : IPlayerStore, IDisposable
{
    private readonly MySqlDataSource _ds;

    public MySqlPlayerStore(string connectionString)
    {
        _ds = new MySqlDataSource(connectionString);
    }

    public void Dispose() => _ds.Dispose();

    private sealed class PlayerRow
    {
        public ulong CharId { get; set; }
        public uint HeroId { get; set; }
        public bool HasVitals { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public uint ArrivalZone { get; set; }
        public float ArrivalX { get; set; }
        public float ArrivalY { get; set; }
        public uint Level { get; set; }
        public uint Exp { get; set; }
    }

    private sealed class CurrencyRow
    {
        public uint CurrencyId { get; set; }
        public uint Amount { get; set; }
    }

    private sealed class ItemRow
    {
        public uint ItemId { get; set; }
        public uint Count { get; set; }
    }

    private sealed class SkillRow
    {
        public uint SkillId { get; set; }
        public byte Slot { get; set; }
    }

    public async Task<PlayerState?> LoadAsync(ulong charId, CancellationToken ct)
    {
        await using MySqlConnection conn = await _ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        PlayerRow? row = await conn.QueryFirstOrDefaultAsync<PlayerRow>(new CommandDefinition(
            "SELECT char_id AS CharId, hero_id AS HeroId, has_vitals AS HasVitals, hp AS Hp, mp AS Mp, " +
            "arrival_zone AS ArrivalZone, arrival_x AS ArrivalX, arrival_y AS ArrivalY, level AS Level, exp AS Exp FROM players WHERE char_id = @CharId",
            new { CharId = charId }, cancellationToken: ct)).ConfigureAwait(false);
        if (row == null)
            return null;

        IEnumerable<ItemRow> items = await conn.QueryAsync<ItemRow>(new CommandDefinition(
            "SELECT item_id AS ItemId, item_cnt AS Count FROM player_items WHERE char_id = @CharId ORDER BY slot",
            new { CharId = charId }, cancellationToken: ct)).ConfigureAwait(false);

        PlayerState s = new PlayerState
        {
            CharId = row.CharId, HeroId = row.HeroId, HasVitals = row.HasVitals, Hp = row.Hp, Mp = row.Mp,
            ArrivalZone = row.ArrivalZone, ArrivalX = row.ArrivalX, ArrivalY = row.ArrivalY, Level = row.Level, Exp = row.Exp,
        };
        IEnumerable<CurrencyRow> wallet = await conn.QueryAsync<CurrencyRow>(new CommandDefinition(
            "SELECT currency_id AS CurrencyId, amount AS Amount FROM player_currency WHERE char_id = @CharId ORDER BY currency_id",
            new { CharId = charId }, cancellationToken: ct)).ConfigureAwait(false);
        foreach (CurrencyRow c in wallet)
            if (s.Wallet.Count < PlayerState.MaxCurrencies)
                s.Wallet.Add(new KeyValuePair<uint, uint>(c.CurrencyId, c.Amount));
        IEnumerable<SkillRow> skills = await conn.QueryAsync<SkillRow>(new CommandDefinition(
            "SELECT skill_id AS SkillId, slot AS Slot FROM player_skill WHERE char_id = @CharId ORDER BY skill_id",
            new { CharId = charId }, cancellationToken: ct)).ConfigureAwait(false);
        foreach (SkillRow k in skills)
            if (s.Skills.Count < PlayerState.MaxSkills)
                s.Skills.Add(new KeyValuePair<uint, byte>(k.SkillId, k.Slot));
        foreach (ItemRow it in items)
            if (s.Items.Count < PlayerState.MaxItems)
                s.Items.Add(new KeyValuePair<uint, uint>(it.ItemId, it.Count));
        return s;
    }

    public async Task SaveAsync(PlayerState state, CancellationToken ct)
    {
        await using MySqlConnection conn = await _ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using MySqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveCore(conn, tx, state, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveBatchAsync(IReadOnlyList<PlayerState> states, CancellationToken ct)
    {
        if (states.Count == 0)
            return;
        await using MySqlConnection conn = await _ds.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using MySqlTransaction tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (PlayerState s in states)
            await SaveCore(conn, tx, s, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task SaveCore(MySqlConnection conn, MySqlTransaction tx, PlayerState s, CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO players (char_id, hero_id, has_vitals, hp, mp, arrival_zone, arrival_x, arrival_y, level, exp) " +
            "VALUES (@CharId, @HeroId, @HasVitals, @Hp, @Mp, @ArrivalZone, @ArrivalX, @ArrivalY, @Level, @Exp) " +
            "ON DUPLICATE KEY UPDATE hero_id = VALUES(hero_id), has_vitals = VALUES(has_vitals), hp = VALUES(hp), mp = VALUES(mp), " +
            "arrival_zone = VALUES(arrival_zone), arrival_x = VALUES(arrival_x), arrival_y = VALUES(arrival_y), level = VALUES(level), exp = VALUES(exp)",
            new { s.CharId, s.HeroId, s.HasVitals, s.Hp, s.Mp, s.ArrivalZone, s.ArrivalX, s.ArrivalY, s.Level, s.Exp },
            tx, cancellationToken: ct)).ConfigureAwait(false);

        // 가방은 통째로 갈아 끼운다(칸 수가 작아 변경분 비교보다 단순하고, 같은 트랜잭션이라 중간 상태는 보이지 않는다).
        // 재화도 같은 방식으로 통째로 갈아 끼운다.
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM player_currency WHERE char_id = @CharId", new { s.CharId }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (s.Wallet.Count != 0)
        {
            List<object> wrows = new List<object>(s.Wallet.Count);
            foreach (KeyValuePair<uint, uint> w in s.Wallet)
                wrows.Add(new { s.CharId, CurrencyId = w.Key, Amount = w.Value });
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO player_currency (char_id, currency_id, amount) VALUES (@CharId, @CurrencyId, @Amount)",
                wrows, tx, cancellationToken: ct)).ConfigureAwait(false);
        }

        // 보유 스킬과 장착 칸도 통째로 갈아 끼운다.
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM player_skill WHERE char_id = @CharId", new { s.CharId }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (s.Skills.Count != 0)
        {
            List<object> krows = new List<object>(s.Skills.Count);
            foreach (KeyValuePair<uint, byte> k in s.Skills)
                krows.Add(new { s.CharId, SkillId = k.Key, Slot = k.Value });
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO player_skill (char_id, skill_id, slot) VALUES (@CharId, @SkillId, @Slot)",
                krows, tx, cancellationToken: ct)).ConfigureAwait(false);
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM player_items WHERE char_id = @CharId", new { s.CharId }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (s.Items.Count == 0)
            return;

        List<object> rows = new List<object>(s.Items.Count);
        for (int i = 0; i < s.Items.Count; ++i)
            rows.Add(new { s.CharId, Slot = (ushort)i, ItemId = s.Items[i].Key, Count = s.Items[i].Value });
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO player_items (char_id, slot, item_id, item_cnt) VALUES (@CharId, @Slot, @ItemId, @Count)",
            rows, tx, cancellationToken: ct)).ConfigureAwait(false);
    }
}

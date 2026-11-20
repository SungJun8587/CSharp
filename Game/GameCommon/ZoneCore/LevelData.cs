// LevelData.cs : 레벨/경험치 데이터 (서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>
/// 레벨 표와 몬스터별 경험치. 기획 데이터의 Level 시트(레벨마다 다음 레벨까지 필요한 경험치와 능력치 배율)와
/// MonsterExp 시트(몬스터 템플릿 ID마다 처치 경험치)에 대응한다.
/// 레벨 1은 영웅 템플릿의 능력치 그대로이고(StatPercent 100), 레벨이 오르면 MaxHp/MaxMp/Attack/Defense에 배율이 곱해진다.
/// 사거리/공격 간격/이동 속도/회복량은 레벨과 무관하다. 로드가 끝나면 변경하지 않으며 존들이 같은 인스턴스를 공유한다.
/// </summary>
public sealed class LevelData
{
    public const uint MaxStatPercent = 100000;          // 배율 상한(%). 오버플로 방지

    public sealed class Row
    {
        public uint Level;              // 1부터 연속
        public uint ExpToNext;          // 이 레벨에서 다음 레벨까지 필요한 경험치. 마지막(최대) 레벨은 0
        public uint StatPercent;        // 이 레벨의 능력치 배율(%). 레벨 1은 100
    }

    private readonly List<Row> _rows = new List<Row>();
    private readonly Dictionary<uint, uint> _monsterExp = new Dictionary<uint, uint>();

    /// <summary>레벨 행을 추가한다. 레벨은 1부터 순서대로 하나씩 더해야 한다. 어긋나면 false.</summary>
    public bool AddLevel(Row row)
    {
        if (row.Level != (uint)_rows.Count + 1)
            return false;
        _rows.Add(row);
        return true;
    }

    /// <summary>몬스터 처치 경험치를 추가한다. 템플릿 ID가 0이거나 이미 있으면 false.</summary>
    public bool AddMonsterExp(uint monsterId, uint exp)
    {
        if (monsterId == 0)
            return false;
        return _monsterExp.TryAdd(monsterId, exp);
    }

    /// <summary>최대 레벨(= 레벨 행 수). 행이 없으면 0.</summary>
    public uint MaxLevel => (uint)_rows.Count;

    public int MonsterExpCount => _monsterExp.Count;

    /// <summary>그 레벨에서 다음 레벨까지 필요한 경험치. 최대 레벨이거나 범위 밖이면 0.</summary>
    public uint ExpToNext(uint level) => level >= 1 && level <= _rows.Count ? _rows[(int)level - 1].ExpToNext : 0;

    /// <summary>몬스터 템플릿의 처치 경험치. 표에 없으면 0.</summary>
    public uint ExpOf(uint monsterId) => _monsterExp.TryGetValue(monsterId, out uint exp) ? exp : 0;

    /// <summary>레벨을 1..MaxLevel 범위로 보정한다. 행이 없으면 1.</summary>
    public uint ClampLevel(uint level) => _rows.Count == 0 ? 1 : Math.Clamp(level, 1u, (uint)_rows.Count);

    /// <summary>기본 능력치에 레벨 배율을 곱한 능력치를 돌려준다(MaxHp/MaxMp/Attack/Defense만). 범위 밖 레벨은 보정한다.</summary>
    public CombatStats Scale(CombatStats b, uint level)
    {
        if (_rows.Count == 0)
            return b;
        long pct = _rows[(int)ClampLevel(level) - 1].StatPercent;
        CombatStats s = b;
        s.MaxHp = ScaleInt(b.MaxHp, pct);
        s.MaxMp = ScaleInt(b.MaxMp, pct);
        s.Attack = ScaleInt(b.Attack, pct);
        s.Defense = ScaleInt(b.Defense, pct);
        return s;
    }

    private static int ScaleInt(int v, long pct) => (int)Math.Min(int.MaxValue, (long)v * pct / 100);

    /// <summary>
    /// 값 범위를 검사한다. 첫 번째 오류만 설명으로 돌려준다.
    /// 검사 항목: 레벨 1개 이상, 레벨 1의 배율 100, 배율이 줄지 않고 상한 이하, 마지막이 아닌 레벨의 필요 경험치 >= 1, 마지막 레벨은 0.
    /// </summary>
    public bool Validate(out string error)
    {
        error = string.Empty;
        if (_rows.Count == 0)
        {
            error = "at least one level is required";
            return false;
        }
        for (int i = 0; i < _rows.Count; ++i)
        {
            Row r = _rows[i];
            bool last = i == _rows.Count - 1;
            if (i == 0 && r.StatPercent != 100) { error = "level 1: statPercent must be 100"; return false; }
            if (r.StatPercent < 100 || r.StatPercent > MaxStatPercent) { error = $"level {r.Level}: statPercent must be 100..{MaxStatPercent}"; return false; }
            if (i > 0 && r.StatPercent < _rows[i - 1].StatPercent) { error = $"level {r.Level}: statPercent must not decrease"; return false; }
            if (!last && r.ExpToNext < 1) { error = $"level {r.Level}: expToNext must be >= 1"; return false; }
            if (last && r.ExpToNext != 0) { error = $"level {r.Level}: the last level must have expToNext 0"; return false; }
        }
        return true;
    }
}

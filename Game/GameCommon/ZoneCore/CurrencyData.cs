// CurrencyData.cs : 재화 정의와 몬스터 처치 보상(서버 구동 시 로드되는 불변 데이터)
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>
/// 재화(골드 등)의 정의와 몬스터별 처치 보상. 기획 데이터의 Currency 시트(재화 ID, 이름, 보유 상한)와
/// MonsterCurrency 시트(몬스터마다 재화, 수량 범위, 확률)에 대응한다. 한 몬스터에 여러 줄(여러 재화)을 둘 수 있다.
/// 로드가 끝나면 변경하지 않으며 존들이 같은 인스턴스를 공유한다.
/// </summary>
public sealed class CurrencyData
{
    public sealed class Currency
    {
        public uint Id;
        public string Name = string.Empty;
        public uint MaxAmount;              // 보유 상한. 넘치는 만큼은 버려진다
        public bool Paid;                   // 결제로만 얻는 유료 재화. 몬스터 보상이 될 수 없고 존 서버가 쓰지(차감하지) 않는다
    }

    public sealed class Reward
    {
        public uint MonsterId;
        public uint CurrencyId;
        public uint MinAmount;
        public uint MaxAmount;
        public uint ChancePermille;         // 1000분율
    }

    private static readonly Reward[] NoRewards = new Reward[0];

    private readonly Dictionary<uint, Currency> _currencies = new Dictionary<uint, Currency>();
    private readonly List<uint> _order = new List<uint>();
    private readonly Dictionary<uint, List<Reward>> _rewards = new Dictionary<uint, List<Reward>>();

    public bool AddCurrency(Currency c)
    {
        if (c.Id == 0 || !_currencies.TryAdd(c.Id, c))
            return false;
        _order.Add(c.Id);
        return true;
    }

    public void AddReward(Reward r)
    {
        if (!_rewards.TryGetValue(r.MonsterId, out List<Reward>? list))
            _rewards.Add(r.MonsterId, list = new List<Reward>());
        list.Add(r);
    }

    public int CurrencyCount => _order.Count;
    public int RewardCount { get { int n = 0; foreach (List<Reward> l in _rewards.Values) n += l.Count; return n; } }

    /// <summary>정의된 재화 ID(표의 순서).</summary>
    public IReadOnlyList<uint> CurrencyIds => _order;

    public bool Has(uint currencyId) => _currencies.ContainsKey(currencyId);

    /// <summary>유료 재화인가. 정의되지 않은 재화는 false.</summary>
    public bool IsPaid(uint currencyId) => _currencies.TryGetValue(currencyId, out Currency? c) && c.Paid;

    /// <summary>보유 상한. 정의되지 않은 재화는 0.</summary>
    public uint MaxOf(uint currencyId) => _currencies.TryGetValue(currencyId, out Currency? c) ? c.MaxAmount : 0;

    /// <summary>몬스터의 보상 목록. 없으면 빈 목록.</summary>
    public IReadOnlyList<Reward> RewardsOf(uint monsterId) => _rewards.TryGetValue(monsterId, out List<Reward>? l) ? l : NoRewards;

    /// <summary>
    /// 값 범위를 검사한다. 첫 번째 오류만 설명으로 돌려준다.
    /// 검사 항목: 보상의 재화가 정의돼 있다, 수량은 1 이상이고 최소 &lt;= 최대 &lt;= 그 재화의 상한, 확률은 1..1000, 같은 몬스터가 같은 재화를 두 줄 갖지 않는다, 유료 재화(Paid)는 몬스터 보상이 될 수 없다, 상한은 1 이상.
    /// </summary>
    public bool Validate(out string error)
    {
        error = string.Empty;
        foreach (Currency c in _currencies.Values)
            if (c.MaxAmount < 1) { error = $"currency {c.Id}: maxAmount must be >= 1"; return false; }
        foreach (KeyValuePair<uint, List<Reward>> kv in _rewards)
        {
            HashSet<uint> seen = new HashSet<uint>();
            foreach (Reward r in kv.Value)
            {
                if (!_currencies.TryGetValue(r.CurrencyId, out Currency? c)) { error = $"monster {r.MonsterId}: unknown currency {r.CurrencyId}"; return false; }
                if (c.Paid) { error = $"monster {r.MonsterId}: paid currency {r.CurrencyId} cannot be a monster reward"; return false; }
                if (!seen.Add(r.CurrencyId)) { error = $"monster {r.MonsterId}: currency {r.CurrencyId} appears twice"; return false; }
                if (r.MinAmount < 1 || r.MinAmount > r.MaxAmount || r.MaxAmount > c.MaxAmount) { error = $"monster {r.MonsterId}: amount range must satisfy 1 <= min <= max <= {c.MaxAmount}"; return false; }
                if (r.ChancePermille < 1 || r.ChancePermille > 1000) { error = $"monster {r.MonsterId}: chancePermille must be 1..1000"; return false; }
            }
        }
        return true;
    }
}

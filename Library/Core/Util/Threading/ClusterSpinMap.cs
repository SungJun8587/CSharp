// ClusterSpinMap.cs : 클러스터(샤드)별 락으로 경합을 나눈 맵.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 키를 클러스터 N개로 나누고 클러스터마다 <see cref="Dictionary{TKey,TValue}"/>와 <see cref="RWSpinLock"/>을 둔 맵.
/// 서로 다른 클러스터의 접근은 서로 막지 않으므로 세션·플레이어 같은 큰 테이블에서 스레드 경합이 줄어든다.
///
/// <para><b>분배</b>: <c>(해시 &amp; 0x7FFFFFFF) % 클러스터 수</c>. 정수 키는 해시가 값 자체라 연속 ID가 고르게 흩어진다.</para>
/// <para><b>락</b>: 조회는 읽기 락, 삽입·삭제는 쓰기 락. 보유 시간이 짧은 스핀 락이므로 <see cref="ForEach"/> 콜백과 factory 안에서
/// 같은 맵을 다시 부르거나(데드락) 오래 걸리는 일을 하면 안 된다. 오래 걸리는 순회는 <see cref="Snapshot"/>으로 복사한 뒤 한다.</para>
/// <para>값 자체의 스레드 안전성은 맵이 보장하지 않는다. 참조 타입 값을 여러 스레드가 고치면 값 쪽에서 동기화한다.</para>
/// </summary>
public sealed class ClusterSpinMap<TKey, TValue> where TKey : notnull
{
    // 락(RWSpinLock)이 스스로 캐시 라인을 독점하므로 클러스터끼리 락 상태를 다투지 않는다.
    private sealed class Cluster
    {
        public readonly RWSpinLock Lock = new RWSpinLock();
        public readonly Dictionary<TKey, TValue> Map;

        public Cluster(IEqualityComparer<TKey>? comparer) => Map = new Dictionary<TKey, TValue>(comparer);
    }

    private readonly Cluster[] _clusters;
    private readonly IEqualityComparer<TKey> _comparer;
    // 삽입/삭제는 해당 클러스터의 쓰기 락 안에서 갱신한다. 락 밖에서 갱신하면 같은 키의 삽입→삭제가 겹칠 때 감소가 증가보다 먼저 반영돼
    // Count가 순간 음수가 될 수 있다. 읽는 쪽은 락을 잡지 않는다.
    private int _count;

    /// <param name="clusterCount">클러스터 수. 동시에 접근하는 스레드 수의 몇 배 정도가 적당하다.</param>
    /// <param name="comparer">키 비교/해시. null이면 기본값. 클러스터 선택에도 같은 해시를 쓴다.</param>
    public ClusterSpinMap(int clusterCount = 16, IEqualityComparer<TKey>? comparer = null)
    {
        if (clusterCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(clusterCount));

        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _clusters = new Cluster[clusterCount];
        for (int i = 0; i < clusterCount; ++i)
            _clusters[i] = new Cluster(_comparer);
    }

    public int ClusterCount => _clusters.Length;

    private Cluster ClusterOf(TKey key) => _clusters[(_comparer.GetHashCode(key) & 0x7FFFFFFF) % _clusters.Length];

    /// <summary>전체 개수. 락을 잡지 않고 읽는다. 음수가 되거나 맵과 어긋나지 않지만, 여러 클러스터에 걸친 변경이 진행 중이면 그 순간의 근삿값이다.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>없을 때만 넣는다. 이미 있으면 false(값은 바뀌지 않는다).</summary>
    public bool Insert(TKey key, TValue value)
    {
        Cluster c = ClusterOf(key);
        using (c.Lock.Write())
        {
            if (!c.Map.TryAdd(key, value))
                return false;

            Interlocked.Increment(ref _count);
            return true;
        }
    }

    /// <summary>넣거나 덮어쓴다.</summary>
    public void Set(TKey key, TValue value)
    {
        Cluster c = ClusterOf(key);
        using (c.Lock.Write())
        {
            // 조회와 저장을 한 번의 해시로 끝낸다.
            ref TValue? slot = ref CollectionsMarshal.GetValueRefOrAddDefault(c.Map, key, out bool exists);
            slot = value;
            if (!exists)
                Interlocked.Increment(ref _count);
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        Cluster c = ClusterOf(key);
        using (c.Lock.Read())
            return c.Map.TryGetValue(key, out value!);
    }

    public bool ContainsKey(TKey key)
    {
        Cluster c = ClusterOf(key);
        using (c.Lock.Read())
            return c.Map.ContainsKey(key);
    }

    /// <summary>
    /// 있으면 그 값을, 없으면 factory로 만들어 넣고 그 값을 돌려준다. 같은 키를 동시에 부르면 하나만 만들어져 넣어지고 모두 같은 값을 받는다.
    /// factory는 쓰기 락 안에서 호출되므로 짧고 이 맵을 다시 부르지 않아야 한다.
    /// </summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        Cluster c = ClusterOf(key);

        using (c.Lock.Read())
        {
            if (c.Map.TryGetValue(key, out TValue? existing))
                return existing;
        }

        using (c.Lock.Write())
        {
            if (c.Map.TryGetValue(key, out TValue? existing))
                return existing;

            TValue created = factory(key);
            c.Map.Add(key, created);
            Interlocked.Increment(ref _count);      // 쓰기 락 안
            return created;
        }
    }

    /// <summary>지운다. 있었으면 true.</summary>
    public bool Remove(TKey key) => Remove(key, out _);

    public bool Remove(TKey key, out TValue value)
    {
        Cluster c = ClusterOf(key);
        using (c.Lock.Write())
        {
            if (!c.Map.Remove(key, out value!))
                return false;

            Interlocked.Decrement(ref _count);
            return true;
        }
    }

    public void Clear()
    {
        foreach (Cluster c in _clusters)
        {
            using (c.Lock.Write())
            {
                int removed = c.Map.Count;
                c.Map.Clear();
                if (removed != 0)
                    Interlocked.Add(ref _count, -removed);
            }
        }
    }

    /// <summary>클러스터마다 읽기 락을 잡고 순회한다. 콜백 안에서 이 맵을 고치거나 오래 걸리는 일을 하면 안 된다.</summary>
    public void ForEach(Action<TKey, TValue> action)
    {
        foreach (Cluster c in _clusters)
        {
            using (c.Lock.Read())
            {
                foreach (KeyValuePair<TKey, TValue> kv in c.Map)
                    action(kv.Key, kv.Value);
            }
        }
    }

    /// <summary>전체를 복사해 돌려준다(클러스터별 시점). 이후 순회는 락 없이 자유롭게 할 수 있다.</summary>
    public List<KeyValuePair<TKey, TValue>> Snapshot()
    {
        var result = new List<KeyValuePair<TKey, TValue>>();
        foreach (Cluster c in _clusters)
        {
            using (c.Lock.Read())
                result.AddRange(c.Map);
        }
        return result;
    }
}

// HashRing.cs : 일관 해싱 링.
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Util;

/// <summary>
/// 일관 해싱(consistent hashing). 키(캐릭터 ID, 방 ID 등)를 노드(서버)에 나눠 준다. 노드를 더하거나 빼도 옮겨지는 키가 약 1/N뿐이다.
/// 노드마다 가상 노드를 여러 개 두어 분포를 고르게 한다. 읽기(<see cref="TryGetNodeByHash"/>)는 락이 없고 추가/제거는 새 링을 만들어 바꿔 끼우므로
/// 읽는 쪽과 바꾸는 쪽이 동시에 있어도 안전하다(바꾸는 쪽끼리만 직렬화한다).
/// </summary>
public sealed class HashRing<TNode> where TNode : notnull
{
    private sealed class Ring
    {
        public readonly ulong[] Points;
        public readonly TNode[] Owners;
        public Ring(ulong[] p, TNode[] o) { Points = p; Owners = o; }
    }

    private readonly int _virtualNodes;
    private readonly Func<TNode, string> _nameOf;
    private readonly object _writeLock = new object();
    private readonly Dictionary<TNode, ulong[]> _nodes = new Dictionary<TNode, ulong[]>();
    private volatile Ring _ring = new Ring(Array.Empty<ulong>(), Array.Empty<TNode>());

    /// <param name="virtualNodes">노드당 가상 노드 수(기본 160)</param>
    /// <param name="nameOf">노드의 안정적인 이름(해시 입력). 프로세스를 다시 띄워도 같은 값이어야 같은 배치가 나온다. 기본은 ToString()</param>
    public HashRing(int virtualNodes = 160, Func<TNode, string>? nameOf = null)
    {
        if (virtualNodes < 1) throw new ArgumentOutOfRangeException(nameof(virtualNodes));
        _virtualNodes = virtualNodes;
        _nameOf = nameOf ?? (n => n.ToString() ?? "");
    }

    public int NodeCount { get { lock (_writeLock) return _nodes.Count; } }

    /// <summary>문자열의 64비트 해시(FNV-1a + 믹싱). 프로세스·플랫폼이 달라도 같은 값이 나온다(string.GetHashCode와 다르다).</summary>
    public static ulong HashKey(string key)
    {
        int max = Encoding.UTF8.GetMaxByteCount(key.Length);
        Span<byte> buf = max <= 256 ? stackalloc byte[256] : new byte[max];
        int n = Encoding.UTF8.GetBytes(key, buf);
        ulong h = 14695981039346656037UL;
        for (int i = 0; i < n; ++i)
        {
            h ^= buf[i];
            h *= 1099511628211UL;
        }
        return Mix(h);
    }

    public static ulong HashKey(ulong key) => Mix(key + 0x9E3779B97F4A7C15UL);

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public bool AddNode(TNode node)
    {
        lock (_writeLock)
        {
            if (_nodes.ContainsKey(node)) return false;
            string name = _nameOf(node);
            ulong[] pts = new ulong[_virtualNodes];
            for (int i = 0; i < pts.Length; ++i)
                pts[i] = HashKey(name + "#" + i);
            _nodes.Add(node, pts);
            Rebuild();
            return true;
        }
    }

    public bool RemoveNode(TNode node)
    {
        lock (_writeLock)
        {
            if (!_nodes.Remove(node)) return false;
            Rebuild();
            return true;
        }
    }

    private void Rebuild()
    {
        int total = _nodes.Count * _virtualNodes;
        var entries = new List<(ulong Point, TNode Owner, string Name)>(total);
        foreach (KeyValuePair<TNode, ulong[]> kv in _nodes)
        {
            string name = _nameOf(kv.Key);
            foreach (ulong p in kv.Value)
                entries.Add((p, kv.Key, name));
        }

        // 같은 점이 겹치면 노드 이름으로 정한다: 추가 순서/정렬 구현과 무관하게 프로세스마다 같은 링이 나오게(일관 해싱의 전제).
        entries.Sort((a, b) =>
        {
            int c = a.Point.CompareTo(b.Point);
            return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
        });
        ulong[] points = new ulong[entries.Count];
        TNode[] owners = new TNode[entries.Count];
        for (int i = 0; i < entries.Count; ++i)
        {
            points[i] = entries[i].Point;
            owners[i] = entries[i].Owner;
        }
        _ring = new Ring(points, owners);
    }

    /// <summary>이미 <see cref="HashKey(string)"/>/<see cref="HashKey(ulong)"/>로 계산한 해시로 노드를 찾는다. 노드가 하나도 없으면 false.
    /// 키(ID)를 그대로 넘기지 않는다: 해시를 거치지 않은 연속 ID는 한쪽 노드로 몰린다(키는 <see cref="TryGetNode(long, out TNode)"/>).</summary>
    public bool TryGetNodeByHash(ulong keyHash, out TNode node)
    {
        Ring r = _ring;
        if (r.Points.Length == 0)
        {
            node = default!;
            return false;
        }
        int i = Array.BinarySearch(r.Points, keyHash);
        if (i < 0) i = ~i;
        if (i == r.Points.Length) i = 0;        // 링을 한 바퀴 돈다
        node = r.Owners[i];
        return true;
    }

    public bool TryGetNode(string key, out TNode node) => TryGetNodeByHash(HashKey(key), out node);
    public bool TryGetNode(long key, out TNode node) => TryGetNodeByHash(HashKey(unchecked((ulong)key)), out node);
}

// SpatialHash.cs : 균일 격자 기반 공간 해시(AOI, 범위 질의).
using System;
using System.Collections.Generic;

namespace Core.Util;

/// <summary>
/// 2D 균일 격자에 항목을 넣고 원/사각형 범위 안의 항목을 빠르게 찾는다. 격자 크기는 질의 반경과 비슷하게 잡는 것이 좋다(대개 반경의 1~2배).
/// 좌표 범위에 제한이 없다(필요한 칸만 만든다). 좌표는 유한한 값이어야 한다(NaN/무한대는 <see cref="Add"/>에서 예외, 질의에서는 결과 없음).
/// 스레드 안전하지 않다.
/// <para>항목은 ID(<typeparamref name="TId"/>)로 구분하고 위치만 갖는다. 같은 ID를 다시 넣으면 위치만 갱신된다(<see cref="Move(TId, float, float)"/>와 같다).
/// 칸 목록에 좌표를 함께 저장하므로 질의가 후보마다 사전을 다시 조회하지 않는다. 같은 칸 안에서 움직이면 칸 이동 비용이 없다.</para>
/// <para>질의 범위가 저장된 칸 수보다 많은 칸을 덮으면(아주 큰 반경) 빈 칸을 훑지 않고 저장된 항목만 순회한다.</para>
/// </summary>
public sealed class SpatialHash<TId> where TId : notnull
{
    private struct Item
    {
        public TId Id;
        public float X, Y;
    }

    private struct Entry
    {
        public long Cell;
        public int Index;
    }

    private readonly float _inv;
    private readonly Dictionary<TId, Entry> _entries = new Dictionary<TId, Entry>();
    private readonly Dictionary<long, List<Item>> _cells = new Dictionary<long, List<Item>>();
    private readonly Stack<List<Item>> _listPool = new Stack<List<Item>>();

    public float CellSize { get; }
    public int Count => _entries.Count;

    public SpatialHash(float cellSize)
    {
        if (!(cellSize > 0) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize));
        CellSize = cellSize;
        _inv = 1f / cellSize;
    }

    private static long Pack(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    // 범위를 벗어난 좌표는 int 한계로 자른다(맨 가장자리 칸에 모인다).
    private int Cell1(float v)
    {
        float c = MathF.Floor(v * _inv);
        if (c >= int.MaxValue) return int.MaxValue;
        if (c <= int.MinValue) return int.MinValue;
        return (int)c;
    }

    private long CellOf(float x, float y) => Pack(Cell1(x), Cell1(y));

    public bool Contains(TId id) => _entries.ContainsKey(id);

    public bool TryGetPosition(TId id, out float x, out float y)
    {
        if (_entries.TryGetValue(id, out Entry e))
        {
            Item it = _cells[e.Cell][e.Index];
            x = it.X;
            y = it.Y;
            return true;
        }
        x = y = 0;
        return false;
    }

    public void Add(TId id, float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "좌표는 유한한 값이어야 한다");
        if (_entries.ContainsKey(id))
        {
            Move(id, x, y);
            return;
        }
        AddToCell(id, x, y, CellOf(x, y));
    }

    private void AddToCell(TId id, float x, float y, long cell)
    {
        if (!_cells.TryGetValue(cell, out List<Item>? list))
        {
            list = _listPool.Count > 0 ? _listPool.Pop() : new List<Item>(8);
            _cells[cell] = list;
        }
        list.Add(new Item { Id = id, X = x, Y = y });
        _entries[id] = new Entry { Cell = cell, Index = list.Count - 1 };
    }

    /// <summary>위치를 옮긴다. 없는 ID거나 좌표가 유한하지 않으면 false.</summary>
    public bool Move(TId id, float x, float y) => Move(id, x, y, out _);

    public bool Move(TId id, float x, float y, out bool changedCell)
    {
        changedCell = false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !_entries.TryGetValue(id, out Entry e))
            return false;
        long cell = CellOf(x, y);
        if (cell == e.Cell)
        {
            List<Item> same = _cells[cell];
            same[e.Index] = new Item { Id = id, X = x, Y = y };
            return true;
        }
        RemoveFromCell(e);
        AddToCell(id, x, y, cell);
        changedCell = true;
        return true;
    }

    public bool Remove(TId id)
    {
        if (!_entries.TryGetValue(id, out Entry e))
            return false;
        RemoveFromCell(e);
        _entries.Remove(id);
        return true;
    }

    private void RemoveFromCell(Entry e)
    {
        List<Item> list = _cells[e.Cell];
        int last = list.Count - 1;
        if (e.Index != last)
        {
            Item moved = list[last];
            list[e.Index] = moved;
            _entries[moved.Id] = new Entry { Cell = e.Cell, Index = e.Index };
        }
        list.RemoveAt(last);
        if (list.Count == 0)
        {
            _cells.Remove(e.Cell);
            _listPool.Push(list);
        }
    }

    /// <summary>(cx,cy) 중심 반경 radius 안(경계 포함)의 항목을 results에 더한다. 더한 개수를 돌려준다.</summary>
    public int QueryCircle(float cx, float cy, float radius, List<TId> results)
    {
        if (!float.IsFinite(cx) || !float.IsFinite(cy) || float.IsNaN(radius) || radius < 0)
            return 0;
        float r2 = radius * radius;
        if (float.IsInfinity(radius))
            r2 = float.MaxValue;
        return QueryCore(cx - radius, cy - radius, cx + radius, cy + radius, results, cx, cy, r2, circle: true);
    }

    /// <summary>사각형 [minX,maxX]x[minY,maxY] 안(경계 포함)의 항목을 results에 더한다.</summary>
    public int QueryRect(float minX, float minY, float maxX, float maxY, List<TId> results)
    {
        if (float.IsNaN(minX) || float.IsNaN(minY) || float.IsNaN(maxX) || float.IsNaN(maxY))
            return 0;
        return QueryCore(minX, minY, maxX, maxY, results, 0, 0, 0, circle: false);
    }

    private int QueryCore(float minX, float minY, float maxX, float maxY, List<TId> results, float cx, float cy, float r2, bool circle)
    {
        int added = 0;
        int gx0 = Cell1(minX), gx1 = Cell1(maxX), gy0 = Cell1(minY), gy1 = Cell1(maxY);
        double span = ((double)gx1 - gx0 + 1) * ((double)gy1 - gy0 + 1);      // long이면 전 범위에서 넘친다

        if (span > _cells.Count)
        {
            // 덮는 칸이 저장된 칸보다 많다: 빈 칸을 하나씩 조회하는 대신 저장된 칸만 본다.
            foreach (KeyValuePair<long, List<Item>> kv in _cells)
            {
                int gx = (int)(kv.Key >> 32), gy = (int)(uint)kv.Key;
                if (gx < gx0 || gx > gx1 || gy < gy0 || gy > gy1)
                    continue;
                added += Scan(kv.Value, minX, minY, maxX, maxY, results, cx, cy, r2, circle);
            }
            return added;
        }

        for (int gx = gx0; ; ++gx)
        {
            for (int gy = gy0; ; ++gy)
            {
                if (_cells.TryGetValue(Pack(gx, gy), out List<Item>? list))
                    added += Scan(list, minX, minY, maxX, maxY, results, cx, cy, r2, circle);
                if (gy == gy1) break;       // int.MaxValue에서 ++로 감기지 않도록 끝에서 멈춘다
            }
            if (gx == gx1) break;
        }
        return added;
    }

    private static int Scan(List<Item> list, float minX, float minY, float maxX, float maxY, List<TId> results, float cx, float cy, float r2, bool circle)
    {
        int added = 0;
        for (int i = 0; i < list.Count; ++i)
        {
            Item it = list[i];
            bool hit;
            if (circle)
            {
                float dx = it.X - cx, dy = it.Y - cy;
                hit = dx * dx + dy * dy <= r2;
            }
            else
            {
                hit = it.X >= minX && it.X <= maxX && it.Y >= minY && it.Y <= maxY;
            }
            if (hit)
            {
                results.Add(it.Id);
                ++added;
            }
        }
        return added;
    }

    public void Clear()
    {
        foreach (List<Item> l in _cells.Values)
        {
            l.Clear();
            _listPool.Push(l);
        }
        _cells.Clear();
        _entries.Clear();
    }
}

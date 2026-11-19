// GridPathfinder.cs : 격자 A* 길찾기와 경로 다듬기.
using System;
using System.Collections.Generic;

namespace Core.Util;

public readonly record struct GridPoint(int X, int Y);

/// <summary>
/// 격자 A*. 이동 비용은 직선 10, 대각선 14(정수)이고 팔방 이동 시 모서리를 가로지르는 대각 이동은 하지 않는다
/// (대각으로 가려면 인접한 두 칸이 모두 비어 있어야 한다). 휴리스틱이 일관적이라 결과는 이 비용 기준 최단 경로다.
/// 탐색용 배열은 세대 표시로 재사용해 호출마다 초기화하지 않는다. 인스턴스는 스레드 안전하지 않다(존/스레드마다 하나씩).
/// </summary>
public sealed class GridPathfinder
{
    private const int Orth = 10;
    private const int Diag = 14;

    private static readonly int[] s_dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] s_dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private readonly bool[] _blocked;
    private readonly int[] _g;
    private readonly int[] _parent;
    private readonly int[] _seen;          // _g/_parent가 이번 탐색 것인지
    private readonly int[] _closed;
    private readonly PriorityQueue<int, int> _open = new PriorityQueue<int, int>();
    private int _gen;

    public GridPathfinder(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > 64_000_000) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        int n = width * height;
        _blocked = new bool[n];
        _g = new int[n];
        _parent = new int[n];
        _seen = new int[n];
        _closed = new int[n];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>마지막 FindPath에서 꺼낸(확장한) 칸 수. 성능 점검용.</summary>
    public int LastExpanded { get; private set; }

    public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>범위 밖은 막힌 것으로 본다.</summary>
    public bool IsBlocked(int x, int y) => !InBounds(x, y) || _blocked[y * Width + x];

    public void SetBlocked(int x, int y, bool blocked)
    {
        if (!InBounds(x, y)) throw new ArgumentOutOfRangeException(nameof(x));
        _blocked[y * Width + x] = blocked;
    }

    /// <summary>
    /// 시작에서 목표까지의 경로(시작과 목표 포함)를 path에 채운다. 못 찾거나 시작/목표가 막혔으면 false(path는 비운다).
    /// maxExpand로 탐색량을 제한할 수 있다(넘으면 false).
    /// </summary>
    public bool FindPath(int sx, int sy, int gx, int gy, List<GridPoint> path, bool diagonal = true, int maxExpand = int.MaxValue)
    {
        path.Clear();
        LastExpanded = 0;
        if (IsBlocked(sx, sy) || IsBlocked(gx, gy)) return false;
        if (sx == gx && sy == gy) { path.Add(new GridPoint(sx, sy)); return true; }

        if (++_gen == int.MaxValue)
        {
            Array.Clear(_seen);
            Array.Clear(_closed);
            _gen = 1;
        }
        _open.Clear();

        int start = sy * Width + sx;
        int goal = gy * Width + gx;
        _seen[start] = _gen;
        _g[start] = 0;
        _parent[start] = -1;
        _open.Enqueue(start, H(sx, sy, gx, gy, diagonal));

        int dirs = diagonal ? 8 : 4;
        int expanded = 0;
        while (_open.TryDequeue(out int cur, out _))
        {
            if (_closed[cur] == _gen) continue;         // 더 나쁜 값으로 들어갔던 옛 항목
            _closed[cur] = _gen;
            LastExpanded = ++expanded;

            if (cur == goal)
            {
                for (int i = goal; i >= 0; i = _parent[i])
                    path.Add(new GridPoint(i % Width, i / Width));
                path.Reverse();
                return true;
            }
            if (expanded >= maxExpand) return false;

            int cx = cur % Width, cy = cur / Width;
            int gcur = _g[cur];
            for (int d = 0; d < dirs; ++d)
            {
                int nx = cx + s_dx[d], ny = cy + s_dy[d];
                if (IsBlocked(nx, ny)) continue;
                int cost = Orth;
                if (d >= 4)
                {
                    if (IsBlocked(cx + s_dx[d], cy) || IsBlocked(cx, cy + s_dy[d])) continue;
                    cost = Diag;
                }
                int ni = ny * Width + nx;
                if (_closed[ni] == _gen) continue;
                int ng = gcur + cost;
                if (_seen[ni] == _gen && _g[ni] <= ng) continue;
                _seen[ni] = _gen;
                _g[ni] = ng;
                _parent[ni] = cur;
                _open.Enqueue(ni, ng + H(nx, ny, gx, gy, diagonal));
            }
        }
        return false;
    }

    /// <summary>두 칸 사이 직선이 막힌 칸(또는 모서리 끼임)에 걸리지 않는가.</summary>
    public bool HasLineOfSight(int x0, int y0, int x1, int y1)
    {
        if (IsBlocked(x0, y0) || IsBlocked(x1, y1)) return false;
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int x = x0, y = y0;
        while (x != x1 || y != y1)
        {
            int e2 = 2 * err;
            int px = x, py = y;
            bool stepX = e2 > -dy, stepY = e2 < dx;
            if (stepX) { err -= dy; x += sx; }
            if (stepY) { err += dx; y += sy; }
            if (IsBlocked(x, y)) return false;
            if (stepX && stepY && (IsBlocked(px + sx, py) || IsBlocked(px, py + sy))) return false;
        }
        return true;
    }

    /// <summary>경로에서 직선으로 갈 수 있는 중간 점을 건너뛰어 꺾이는 점만 남긴다(제자리 수정). 처음과 끝 점은 그대로다.</summary>
    public void Smooth(List<GridPoint> path)
    {
        if (path.Count < 3) return;
        int w = 1;                                  // 앞쪽에 남길 점을 제자리로 모은다(읽기 위치가 항상 쓰기 위치보다 앞)
        int anchor = 0;
        GridPoint last = path[path.Count - 1];
        for (int i = 2; i < path.Count; ++i)
        {
            if (!HasLineOfSight(path[anchor].X, path[anchor].Y, path[i].X, path[i].Y))
            {
                path[w++] = path[i - 1];
                anchor = i - 1;
            }
        }
        path[w++] = last;
        path.RemoveRange(w, path.Count - w);
    }

    private static int H(int x, int y, int gx, int gy, bool diagonal)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return diagonal ? Orth * Math.Max(dx, dy) + (Diag - Orth) * Math.Min(dx, dy) : Orth * (dx + dy);
    }
}

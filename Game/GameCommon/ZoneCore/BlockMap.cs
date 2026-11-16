// BlockMap.cs : implementation of the BlockMap class.
using System;
using System.Collections.Generic;

namespace Game.ZoneCore;

/// <summary>
/// 존 평면을 균일 격자로 나누고 셀마다 이동 가능/불가를 기록한 차단 맵.
/// 나무, 바위, 절벽 같은 장애물을 사각형/원으로 등록하면 격자에 래스터화한다(겹치는 셀은 모두 차단).
/// 서버는 이 맵으로 이동 목적지까지의 직선 경로를 검사한다. 엔티티는 점으로 취급하므로
/// 몸 크기를 반영하려면 장애물을 엔티티 반경만큼 키워서 등록한다.
///
/// Init/AddRect/AddCircle로 구성을 마친 뒤에는 읽기 전용이며, 읽기 메서드는 여러 스레드에서 동시에 호출해도 안전하다.
/// (구성 중에는 한 스레드에서만 사용한다.) 존들은 같은 인스턴스를 공유한다.
///
/// FindPath()는 8방향 A*로 우회 경로를 찾아 직선으로 이어지는 구간을 합친 경유점 목록을 돌려준다.
/// 각 구간은 IsSegmentClear()를 통과하므로 서버의 ClampMove()가 구간을 줄이지 않는다.
///
/// 경로 검사는 시작 셀을 제외한 셀만 본다. 시작 셀이 차단이어도(경계 오차로 가장자리에 걸친 경우 등)
/// 이웃 셀이 비어 있으면 빠져나갈 수 있다. 차단 영역 한가운데에 있는 엔티티는 이동할 수 없으며, 스폰/순간이동 검증으로 그런 위치에 놓이지 않게 한다.
/// </summary>
public sealed class BlockMap
{
    /// <summary>셀 수 상한. 이보다 크면 Init이 실패한다(셀당 1바이트).</summary>
    public const long MaxCells = 64L * 1024 * 1024;

    /// <summary>셀 크기 하한. 경로 검사가 충돌 지점에서 물러서는 거리(Backoff)보다 충분히 커야 한다.</summary>
    public const float MinCellSize = 0.5f;

    /// <summary>서버와 클라이언트(봇)가 같은 판정을 하려면 같은 셀 크기를 써야 하므로 공용 기본값을 둔다.</summary>
    public const float DefaultCellSize = 1.0f;

    /// <summary>충돌 지점에서 물러서는 거리. 부동소수점 오차로 차단 셀 경계를 넘어가지 않게 한다.</summary>
    public const float Backoff = 0.01f;

    /// <summary>경로탐색 한 번에 펼칠 수 있는 셀 수 기본 상한. 막힌 목적지에서 맵 전체를 뒤지지 않게 비용을 묶는다.</summary>
    public const uint DefaultPathNodes = 8192;

    /// <summary>장애물을 키우는 기본 여유 폭(엔티티 반경 대신). 서버와 봇이 같은 값을 써야 같은 판정이 된다.</summary>
    public const float DefaultMargin = 0.5f;

    private float _minX;
    private float _minY;
    private float _maxX;
    private float _maxY;
    private float _cellSize = 1.0f;
    private int _cellsX;
    private int _cellsY;
    private int _blockedCount;
    private byte[] _cells = Array.Empty<byte>();    // 0 = 이동 가능, 1 = 차단

    /// <summary>Init() 전에는 모든 좌표가 이동 가능이다.</summary>
    public BlockMap()
    {
    }

    public int BlockedCellCount => _blockedCount;
    public int CellsX => _cellsX;
    public int CellsY => _cellsY;
    public float CellSize => _cellSize;

    /// <summary>존 영역과 셀 크기로 격자를 구성한다. 기존 내용은 지워진다.</summary>
    /// <param name="cellSize">셀 한 변의 길이 (MinCellSize 이상)</param>
    /// <returns>성공 시 true, 값이 유효하지 않거나 셀 수가 MaxCells를 넘으면 false</returns>
    public bool Init(float minX, float minY, float maxX, float maxY, float cellSize)
    {
        _cells = Array.Empty<byte>();
        _cellsX = 0;
        _cellsY = 0;
        _blockedCount = 0;

        if (!(float.IsFinite(minX) && float.IsFinite(minY) && float.IsFinite(maxX) && float.IsFinite(maxY) && float.IsFinite(cellSize)))
            return false;
        if (!(maxX > minX) || !(maxY > minY) || !(cellSize >= MinCellSize))
            return false;

        double cx = Math.Ceiling(((double)maxX - minX) / cellSize);
        double cy = Math.Ceiling(((double)maxY - minY) / cellSize);
        if (cx * cy > (double)MaxCells)
            return false;

        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;
        _cellSize = cellSize;
        _cellsX = (int)cx;
        _cellsY = (int)cy;
        _cells = new byte[(long)_cellsX * _cellsY];
        return true;
    }

    /// <summary>좌표가 속한 셀의 X 인덱스. 영역 밖은 가장자리 셀로 보정한다.</summary>
    private int CellX(double x)
    {
        double v = Math.Floor((x - _minX) / _cellSize);
        return (int)Math.Min(Math.Max(v, 0.0), (double)(_cellsX - 1));
    }

    /// <summary>좌표가 속한 셀의 Y 인덱스. 영역 밖은 가장자리 셀로 보정한다.</summary>
    private int CellY(double y)
    {
        double v = Math.Floor((y - _minY) / _cellSize);
        return (int)Math.Min(Math.Max(v, 0.0), (double)(_cellsY - 1));
    }

    private void BlockCell(int cx, int cy)
    {
        long i = (long)cy * _cellsX + cx;
        if (_cells[i] == 0)
        {
            _cells[i] = 1;
            ++_blockedCount;
        }
    }

    /// <summary>사각형 장애물을 등록한다. 사각형과 조금이라도 겹치는 셀은 모두 차단된다.</summary>
    /// <param name="x">사각형 최소 X (왼쪽 아래 모서리)</param>
    /// <param name="y">사각형 최소 Y</param>
    /// <param name="width">너비 (0 이하이거나 유한하지 않으면 무시)</param>
    /// <param name="height">높이 (0 이하이거나 유한하지 않으면 무시)</param>
    public void AddRect(float x, float y, float width, float height)
    {
        if (_cells.Length == 0 || !(float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height))
            || !(width > 0.0f) || !(height > 0.0f))
            return;

        double x1 = (double)x + width;
        double y1 = (double)y + height;
        if (x1 <= _minX || y1 <= _minY || x >= _maxX || y >= _maxY)
            return;

        // 최대 경계가 셀 경계에 정확히 닿으면 다음 셀로 번지지 않게 경계를 살짝 안쪽으로 당겨 셀을 구한다.
        double eps = (double)_cellSize * 1e-6;
        int cx0 = CellX(x);
        int cy0 = CellY(y);
        int cx1 = Math.Max(cx0, CellX(x1 - eps));
        int cy1 = Math.Max(cy0, CellY(y1 - eps));

        for (int cy = cy0; cy <= cy1; ++cy)
            for (int cx = cx0; cx <= cx1; ++cx)
                BlockCell(cx, cy);
    }

    /// <summary>원형 장애물을 등록한다. 원과 조금이라도 겹치는 셀은 모두 차단된다.</summary>
    /// <param name="radius">반경 (0 이하이거나 유한하지 않으면 무시)</param>
    public void AddCircle(float centerX, float centerY, float radius)
    {
        if (_cells.Length == 0 || !(float.IsFinite(centerX) && float.IsFinite(centerY) && float.IsFinite(radius)) || !(radius > 0.0f))
            return;

        double cx = centerX;
        double cy = centerY;
        double r = radius;
        if (cx + r <= _minX || cy + r <= _minY || cx - r >= _maxX || cy - r >= _maxY)
            return;

        int x0 = CellX(cx - r);
        int x1 = CellX(cx + r);
        int y0 = CellY(cy - r);
        int y1 = CellY(cy + r);

        for (int iy = y0; iy <= y1; ++iy)
        {
            double cellMinY = _minY + (double)iy * _cellSize;
            double nearY = Math.Min(Math.Max(cy, cellMinY), cellMinY + _cellSize);
            for (int ix = x0; ix <= x1; ++ix)
            {
                double cellMinX = _minX + (double)ix * _cellSize;
                double nearX = Math.Min(Math.Max(cx, cellMinX), cellMinX + _cellSize);
                double dx = nearX - cx;
                double dy = nearY - cy;
                if (dx * dx + dy * dy < r * r)      // 접하기만 하는 셀은 막지 않는다
                    BlockCell(ix, iy);
            }
        }
    }

    /// <summary>셀이 차단 셀인지. 격자 밖 인덱스는 false.</summary>
    public bool IsCellBlocked(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= _cellsX || cy >= _cellsY)
            return false;
        return _cells[(long)cy * _cellsX + cx] != 0;
    }

    /// <summary>좌표가 차단 셀 안인지. 영역 밖이거나 NaN이면 false(영역 검사는 호출자 몫).</summary>
    public bool IsBlocked(Vec2 pos)
    {
        if (_cells.Length == 0 || !(pos.X >= _minX && pos.X <= _maxX && pos.Y >= _minY && pos.Y <= _maxY))
            return false;
        return IsCellBlocked(CellX(pos.X), CellY(pos.Y));
    }

    /// <summary>
    /// 선분이 처음 차단 셀에 들어가는 지점을 찾는다. (격자 순회: Amanatides-Woo)
    /// 시작 셀은 검사하지 않고, 격자 밖으로 나가면 거기서 끝낸다.
    /// </summary>
    /// <param name="tHit">[out] 충돌 지점의 선분 매개변수 (0..1)</param>
    private bool FindHit(Vec2 from, Vec2 to, out double tHit)
    {
        tHit = 0.0;
        if (_cells.Length == 0 || _blockedCount == 0)
            return false;

        double dx = (double)to.X - from.X;
        double dy = (double)to.Y - from.Y;
        if (!(double.IsFinite(dx) && double.IsFinite(dy)) || (dx == 0.0 && dy == 0.0))
            return false;

        int cx = CellX(from.X);
        int cy = CellY(from.Y);
        int endX = CellX(to.X);
        int endY = CellY(to.Y);
        if (cx == endX && cy == endY)
            return false;       // 같은 셀 안의 이동

        const double kInf = double.PositiveInfinity;
        int stepX = dx > 0.0 ? 1 : -1;
        int stepY = dy > 0.0 ? 1 : -1;
        double deltaX = dx != 0.0 ? _cellSize / Math.Abs(dx) : kInf;
        double deltaY = dy != 0.0 ? _cellSize / Math.Abs(dy) : kInf;

        double maxX = kInf;
        if (dx != 0.0)
        {
            double edge = _minX + (double)(stepX > 0 ? cx + 1 : cx) * _cellSize;
            maxX = (edge - from.X) / dx;
        }
        double maxY = kInf;
        if (dy != 0.0)
        {
            double edge = _minY + (double)(stepY > 0 ? cy + 1 : cy) * _cellSize;
            maxY = (edge - from.Y) / dy;
        }

        // 선분이 지나는 셀 수는 (|끝-시작| 셀 차이 합 + 1) 이하이므로 그 횟수만 돈다.
        int limit = Math.Abs(endX - cx) + Math.Abs(endY - cy);
        for (int i = 0; i < limit; ++i)
        {
            double t;
            if (maxX < maxY)
            {
                t = maxX;
                maxX += deltaX;
                cx += stepX;
            }
            else
            {
                t = maxY;
                maxY += deltaY;
                cy += stepY;
            }

            if (t > 1.0 || cx < 0 || cy < 0 || cx >= _cellsX || cy >= _cellsY)
                return false;

            if (IsCellBlocked(cx, cy))
            {
                tHit = Math.Max(t, 0.0);
                return true;
            }
        }

        return false;
    }

    /// <summary>선분 전체가 차단 셀을 지나지 않는지.</summary>
    public bool IsSegmentClear(Vec2 from, Vec2 to) => !FindHit(from, to, out _);

    /// <summary>
    /// from에서 to로 직선 이동할 때 막히기 전까지 갈 수 있는 가장 먼 지점을 구한다.
    /// 막히면 충돌 지점에서 Backoff만큼 물러선 위치를 돌려줘, 부동소수점 오차로 차단 셀에 들어가지 않게 한다.
    /// </summary>
    /// <param name="hit">막혀서 목적지가 바뀌었으면 true</param>
    /// <returns>도달 가능한 지점 (막힘이 없으면 to 그대로)</returns>
    public Vec2 ClampMove(Vec2 from, Vec2 to, out bool hit)
    {
        bool blocked = FindHit(from, to, out double t);
        hit = blocked;
        if (!blocked)
            return to;

        double dx = (double)to.X - from.X;
        double dy = (double)to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double tt = Math.Max(0.0, t - (double)Backoff / len);

        // float로 줄이면서 좌표가 큰 곳(ulp > Backoff)에서는 차단 셀 안으로 반올림될 수 있으므로 확인하고 더 물러선다.
        Vec2 result = from;
        for (int i = 0; i < 8; ++i)
        {
            result.X = (float)(from.X + dx * tt);
            result.Y = (float)(from.Y + dy * tt);
            if (tt <= 0.0 || !IsBlocked(result))
                return result;
            tt = Math.Max(0.0, tt - (double)Backoff * (2 << i) / len);
        }
        return from;
    }

    /// <summary><see cref="ClampMove(Vec2, Vec2, out bool)"/>에서 막힘 여부가 필요 없을 때.</summary>
    public Vec2 ClampMove(Vec2 from, Vec2 to) => ClampMove(from, to, out _);

    /// <summary>경유점 병합 때 한 점에서 앞으로 직선 검사를 해 보는 최대 점 수.</summary>
    private const int MergeLookahead = 48;

    private struct PathNode
    {
        public float G;
        public int X;
        public int Y;
        public int Parent;
    }

    // 호출마다 할당하지 않도록 스레드별 작업 공간을 재사용한다.
    [ThreadStatic] private static List<PathNode>? t_nodes;
    [ThreadStatic] private static Dictionary<long, int>? t_best;
    [ThreadStatic] private static List<bool>? t_closed;
    [ThreadStatic] private static List<Vec2>? t_pts;
    [ThreadStatic] private static PriorityQueue<int, (float F, int Node)>? t_open;

    /// <summary>
    /// from에서 to까지 장애물을 피하는 경로를 찾는다. (8방향 A*, 대각선은 양쪽 이웃 셀이 비어 있을 때만)
    /// 직선이 비어 있으면 [to] 한 점만 돌려준다. 그렇지 않으면 셀 경로를 구한 뒤, 서로 직선으로 갈 수 있는
    /// 경유점을 건너뛰어 합친다. 반환값의 각 구간은 IsSegmentClear()를 통과하도록 만들어지므로
    /// 서버가 경유점을 차례로 이동 명령으로 내려도 ClampMove()에 걸리지 않는다.
    /// 펼친 셀 수가 maxNodes를 넘으면 실패한다(도달 불가 목적지에서 맵 전체를 뒤지지 않도록 비용을 묶는다).
    /// </summary>
    /// <param name="to">목적지 (차단 셀 안이면 실패)</param>
    /// <param name="path">[out] 경유점. from은 포함하지 않고 마지막은 to</param>
    /// <param name="maxNodes">펼칠 수 있는 셀 수 상한</param>
    /// <returns>경로를 찾으면 true</returns>
    public bool FindPath(Vec2 from, Vec2 to, List<Vec2> path, uint maxNodes = DefaultPathNodes)
    {
        path.Clear();
        if (!(float.IsFinite(from.X) && float.IsFinite(from.Y) && float.IsFinite(to.X) && float.IsFinite(to.Y)))
            return false;

        if (_cells.Length == 0 || _blockedCount == 0 || IsSegmentClear(from, to))
        {
            path.Add(to);
            return true;
        }

        int gx = CellX(to.X);
        int gy = CellY(to.Y);
        if (IsCellBlocked(gx, gy))
            return false;

        List<PathNode> nodes = t_nodes ??= new List<PathNode>();
        Dictionary<long, int> best = t_best ??= new Dictionary<long, int>();
        List<bool> closed = t_closed ??= new List<bool>();
        PriorityQueue<int, (float F, int Node)> open = t_open ??= new PriorityQueue<int, (float F, int Node)>();
        nodes.Clear();
        best.Clear();
        closed.Clear();
        open.Clear();

        const float kDiag = 1.41421356f;
        const float kHeuristicWeight = 1.5f;

        float Heuristic(int x, int y)
        {
            float hx = (float)Math.Abs(x - gx);
            float hy = (float)Math.Abs(y - gy);
            // 가중치 1.5: 최단은 아니어도 펼치는 셀 수를 크게 줄인다. 결과는 경유점 병합으로 다듬어진다.
            return kHeuristicWeight * ((hx > hy ? hx : hy) + (kDiag - 1.0f) * (hx > hy ? hy : hx));
        }

        long Key(int x, int y) => (long)y * _cellsX + x;

        int sx = CellX(from.X);
        int sy = CellY(from.Y);
        nodes.Add(new PathNode { G = 0.0f, X = sx, Y = sy, Parent = -1 });
        closed.Add(false);
        best[Key(sx, sy)] = 0;
        open.Enqueue(0, (Heuristic(sx, sy), 0));

        int goalNode = -1;
        uint expanded = 0;
        while (open.TryDequeue(out int idx, out _))
        {
            if (closed[idx])
                continue;
            closed[idx] = true;

            PathNode cur = nodes[idx];
            if (best[Key(cur.X, cur.Y)] != idx)
                continue;       // 더 좋은 경로로 갱신되어 뒤처진 중복 노드 (다시 펼치지 않는다)
            if (cur.X == gx && cur.Y == gy)
            {
                goalNode = idx;
                break;
            }
            if (++expanded > maxNodes)
                return false;

            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = cur.X + dx;
                    int ny = cur.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= _cellsX || ny >= _cellsY || IsCellBlocked(nx, ny))
                        continue;
                    if (dx != 0 && dy != 0 && (IsCellBlocked(cur.X + dx, cur.Y) || IsCellBlocked(cur.X, cur.Y + dy)))
                        continue;       // 모서리 가로지르기 금지

                    float ng = cur.G + (dx != 0 && dy != 0 ? kDiag : 1.0f);
                    long key = Key(nx, ny);
                    if (best.TryGetValue(key, out int prev) && ng >= nodes[prev].G)
                        continue;

                    nodes.Add(new PathNode { G = ng, X = nx, Y = ny, Parent = idx });
                    closed.Add(false);
                    int ni = nodes.Count - 1;
                    best[key] = ni;
                    open.Enqueue(ni, (ng + Heuristic(nx, ny), ni));
                }
            }
        }

        if (goalNode < 0)
            return false;

        // 셀 경로를 거꾸로 따라가 점 목록(from, 셀 중심들, to)을 만든다.
        List<Vec2> pts = t_pts ??= new List<Vec2>();
        pts.Clear();
        for (int n = goalNode; n >= 0 && nodes[n].Parent >= 0; n = nodes[n].Parent)
        {
            Vec2 c;
            c.X = (float)(_minX + (nodes[n].X + 0.5) * _cellSize);
            c.Y = (float)(_minY + (nodes[n].Y + 0.5) * _cellSize);
            pts.Add(c);
        }
        if (pts.Count == 0)
            pts.Add(to);
        pts[0] = to;        // 마지막 셀의 중심 대신 실제 목적지
        pts.Reverse();
        pts.Insert(0, from);

        // 직선으로 갈 수 있는 가장 먼 점으로 건너뛴다. 굽이진 긴 경로에서 선분 검사가 제곱으로 늘지 않도록 앞쪽 MergeLookahead개까지만 본다
        // (더 먼 점은 다음 구간에서 다시 본다. 어느 쪽이든 각 구간은 IsSegmentClear를 통과한다).
        int i = 0;
        while (i + 1 < pts.Count)
        {
            int next = i + 1;
            for (int j = Math.Min(pts.Count - 1, i + MergeLookahead); j > i + 1; --j)
            {
                if (IsSegmentClear(pts[i], pts[j]))
                {
                    next = j;
                    break;
                }
            }
            path.Add(pts[next]);
            i = next;
        }
        return true;
    }
}

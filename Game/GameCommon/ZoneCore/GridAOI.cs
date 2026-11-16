// GridAOI.cs : implementation of the GridAOI class.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Game.ZoneCore;

/// <summary>AOI 셀 하나. 관찰자(watcher)와 그 외 엔티티를 따로 보관한다. 관찰자만 훑는 순회는 그 외 엔티티를 건드리지 않는다.</summary>
internal sealed class AoiCell<TEntity> where TEntity : ZoneEntity
{
    public readonly List<TEntity> Watchers = new List<TEntity>();   // IsAoiWatcher()가 true인 엔티티 (플레이어 등)
    public readonly List<TEntity> Others = new List<TEntity>();     // 그 외 엔티티 (몬스터 등)
}

/// <summary>
/// 격자(셀) 기반 관심영역(AOI) 관리자.
///
/// 존 영역을 cellSize 크기의 정사각 셀로 나누고, 엔티티는 자신이 속한 셀 기준 (2*viewCells+1)^2 셀 범위를 시야로 가진다.
/// 거리가 (viewCells - 2*hysteresis)*cellSize 이내인 엔티티끼리는 항상 서로 보이고(최소 보장 시야),
/// 실제로 보이는 최대 거리는 (viewCells+1)*cellSize 근처까지 늘어난다.
/// 따라서 (viewCells - 2*hysteresis)*cellSize를 클라이언트가 기대하는 시야 거리 이상으로 잡아야 한다.
///
/// 엔티티가 셀 경계를 넘을 때만 (새 시야 - 옛 시야)에 진입 통지, (옛 시야 - 새 시야)에 이탈 통지를 양방향으로 발생시킨다.
/// 같은 셀 안의 이동은 비용이 거의 없다(셀 인덱스 계산만 수행).
///
/// 관찰자(watcher): 통지를 받아 무언가를 하는 엔티티(플레이어처럼 시야 안 엔티티의 스폰/디스폰을 알려야 하는 쪽).
/// 두 엔티티 중 하나라도 관찰자일 때만 서로에게 통지하고, 둘 다 관찰자가 아니면(몬스터끼리 등) 통지하지 않는다.
///
/// 히스테리시스: 셀 경계에서 왔다 갔다 하는 엔티티가 매 틱 진입/이탈 통지를 쏟아내지 않도록,
/// 엔티티는 현재 셀을 경계 바깥으로 hysteresis*cellSize 이상 벗어나야 소속 셀을 바꾼다.
///
/// 스레드 안전하지 않다. 소유 존의 틱 스레드에서만 호출해야 한다.
/// 통지 콜백 안에서 Add/Remove/Update를 호출하면 안 된다(디버그 빌드에서 assert).
/// </summary>
public sealed class GridAOI<TEntity> where TEntity : ZoneEntity
{
    private float _minX;                // 영역 최소 X
    private float _minY;                // 영역 최소 Y
    private float _cellSize = 1.0f;     // 셀 한 변의 길이
    private float _invCellSize;         // 셀 크기의 역수 (나눗셈 회피)
    private float _hysteresis;          // 셀 변경 히스테리시스 (셀 크기 대비 비율)
    private int _cols;                  // 가로 셀 수
    private int _rows;                  // 세로 셀 수
    private int _view = 1;              // 시야 반경 (셀 단위)
    private int _entityCount;           // 등록된 엔티티 수
    private bool _dispatching;          // 통지 콜백 실행 중 여부 (재진입 방지)
    private AoiCell<TEntity>[] _cells = Array.Empty<AoiCell<TEntity>>();

    /// <summary>
    /// 영역과 셀 크기를 지정해 격자를 구성한다.
    /// </summary>
    /// <param name="cellSize">셀 한 변의 길이</param>
    /// <param name="viewCells">시야 반경(셀 단위, 1이면 3x3)</param>
    /// <param name="hysteresis">셀 변경 히스테리시스(셀 크기 대비 비율, 0이면 끔, 0.5 미만이어야 함)</param>
    /// <returns>성공 시 true, 인자가 유효하지 않으면 false</returns>
    public bool Init(float minX, float minY, float maxX, float maxY, float cellSize, int viewCells = 1, float hysteresis = 0.1f)
    {
        if (!(cellSize > 0.0f) || !(maxX > minX) || !(maxY > minY) || viewCells < 1 || !(hysteresis >= 0.0f) || !(hysteresis < 0.5f))
            return false;

        double cols = Math.Ceiling((double)(maxX - minX) / cellSize);
        double rows = Math.Ceiling((double)(maxY - minY) / cellSize);
        if (cols * rows > 16.0 * 1024.0 * 1024.0)
            return false;       // 셀 수가 비정상적으로 많은 설정(셀 크기 오설정)을 거부

        _minX = minX;
        _minY = minY;
        _cellSize = cellSize;
        _invCellSize = 1.0f / cellSize;
        _cols = Math.Max(1, (int)cols);
        _rows = Math.Max(1, (int)rows);
        _view = viewCells;
        _hysteresis = hysteresis;

        _cells = new AoiCell<TEntity>[(long)_cols * _rows];
        for (int i = 0; i < _cells.Length; ++i)
            _cells[i] = new AoiCell<TEntity>();
        _entityCount = 0;
        return true;
    }

    /// <summary>엔티티를 AOI에 등록하고, 시야 안의 엔티티와 양방향 진입 통지를 발생시킨다. 이미 등록되었거나 Init 전이면 false.</summary>
    public bool Add(TEntity entity)
    {
        Debug.Assert(!_dispatching, "GridAOI.Add called inside AOI callback");

        if (entity == null || _cols == 0 || entity._aoiNode.CellIdx >= 0)
            return false;

        entity._aoiNode.Watcher = entity.IsAoiWatcher();

        int cellIdx = CellIndexOf(entity._pos);
        InsertToCell(entity, cellIdx);
        ++_entityCount;

        NotifyCells(entity, cellIdx, -1, true);
        return true;
    }

    /// <summary>엔티티를 AOI에서 제거하고, 시야 안의 엔티티와 양방향 이탈 통지를 발생시킨다.</summary>
    public void Remove(TEntity entity)
    {
        Debug.Assert(!_dispatching, "GridAOI.Remove called inside AOI callback");

        if (entity == null || entity._aoiNode.CellIdx < 0)
            return;

        NotifyCells(entity, entity._aoiNode.CellIdx, -1, false);

        EraseFromCell(entity);
        --_entityCount;
    }

    /// <summary>
    /// 엔티티의 위치가 바뀐 뒤 호출한다. 소속 셀이 바뀐 경우에만 (옛 시야 - 새 시야)에 이탈 통지, (새 시야 - 옛 시야)에 진입 통지를 보낸다.
    /// 순간이동처럼 두 시야가 겹치지 않는 경우도 같은 규칙으로 처리된다.
    /// </summary>
    /// <returns>소속 셀이 바뀌었으면 true</returns>
    public bool Update(TEntity entity)
    {
        Debug.Assert(!_dispatching, "GridAOI.Update called inside AOI callback");

        if (entity == null || entity._aoiNode.CellIdx < 0)
            return false;

        int oldCell = entity._aoiNode.CellIdx;

        // 현재 셀을 히스테리시스만큼 확장한 영역 안이면 셀을 바꾸지 않는다.
        if (_hysteresis > 0.0f)
        {
            float fx = (entity._pos.X - _minX) * _invCellSize;
            float fy = (entity._pos.Y - _minY) * _invCellSize;
            float cx = (float)(oldCell % _cols);
            float cy = (float)(oldCell / _cols);

            if (fx >= cx - _hysteresis && fx < cx + 1.0f + _hysteresis &&
                fy >= cy - _hysteresis && fy < cy + 1.0f + _hysteresis)
                return false;
        }

        int newCell = CellIndexOf(entity._pos);
        if (newCell == oldCell)
            return false;

        EraseFromCell(entity);
        InsertToCell(entity, newCell);

        NotifyCells(entity, oldCell, newCell, false);
        NotifyCells(entity, newCell, oldCell, true);
        return true;
    }

    /// <summary>엔티티의 시야 셀 안에 있는 다른 엔티티를 순회한다. (자기 자신 제외) 순회 중 AOI 구조를 변경하면 안 된다.</summary>
    public void ForEachNeighbor(ZoneEntity entity, Action<TEntity> fn) => ForEachNeighborImpl(entity, fn, true);

    /// <summary>
    /// 엔티티의 시야 셀 안에 있는 관찰자(IsAoiWatcher)만 순회한다. (자기 자신 제외)
    /// 패킷 수신자를 모으는 용도처럼 관찰자만 필요할 때, 시야 안의 그 외 엔티티(몬스터 등)를 건드리지 않아 빠르다.
    /// </summary>
    public void ForEachWatcherNeighbor(ZoneEntity entity, Action<TEntity> fn) => ForEachNeighborImpl(entity, fn, false);

    /// <summary>
    /// 임의 좌표 기준 원형 범위 안의 엔티티를 순회한다. 범위 스킬/어그로 탐색용.
    /// 셀로 후보를 줄인 뒤 정확한 거리 비교를 수행한다. 히스테리시스로 엔티티가 소속 셀 밖에 있을 수 있어 후보 셀 범위를 그만큼 넓혀 탐색한다.
    /// </summary>
    /// <param name="radius">반경 (NaN/음수면 아무것도 순회하지 않음)</param>
    public void ForEachInRadius(Vec2 center, float radius, Action<TEntity> fn) => ForEachInRadiusImpl(center, radius, fn, true);

    /// <summary>임의 좌표 기준 원형 범위 안의 관찰자(IsAoiWatcher)만 순회한다. 선공 대상(플레이어) 탐색용.</summary>
    public void ForEachWatcherInRadius(Vec2 center, float radius, Action<TEntity> fn) => ForEachInRadiusImpl(center, radius, fn, false);

    /// <summary>등록된 엔티티 수.</summary>
    public int EntityCount => _entityCount;

    /// <summary>시야 반경(셀 단위).</summary>
    public int ViewCells => _view;

    /// <summary>
    /// 최소 보장 시야 반경(월드 단위). 이 거리 안의 두 엔티티는 항상 서로의 시야에 있다.
    /// 클라이언트와 합의된 시야 거리 이상이어야 한다. (viewCells - 2 * hysteresis) * cellSize
    /// </summary>
    public float GuaranteedViewRadius => ((float)_view - 2.0f * _hysteresis) * _cellSize;

    private void ForEachNeighborImpl(ZoneEntity entity, Action<TEntity> fn, bool includeOthers)
    {
        if (entity._aoiNode.CellIdx < 0)
            return;

        int cx = entity._aoiNode.CellIdx % _cols;
        int cy = entity._aoiNode.CellIdx / _cols;

        for (int y = Math.Max(cy - _view, 0); y <= Math.Min(cy + _view, _rows - 1); ++y)
        {
            for (int x = Math.Max(cx - _view, 0); x <= Math.Min(cx + _view, _cols - 1); ++x)
            {
                AoiCell<TEntity> cell = _cells[(long)y * _cols + x];
                VisitList(cell.Watchers, entity, fn);
                if (includeOthers)
                    VisitList(cell.Others, entity, fn);
            }
        }
    }

    private void ForEachInRadiusImpl(Vec2 center, float radius, Action<TEntity> fn, bool includeOthers)
    {
        if (_cols == 0 || !(radius >= 0.0f) || !float.IsFinite(center.X) || !float.IsFinite(center.Y))
            return;

        float radiusSq = radius * radius;
        float reach = radius + _hysteresis * _cellSize;
        int x0 = CellX(center.X - reach);
        int x1 = CellX(center.X + reach);
        int y0 = CellY(center.Y - reach);
        int y1 = CellY(center.Y + reach);

        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                AoiCell<TEntity> cell = _cells[(long)y * _cols + x];
                VisitInRadius(cell.Watchers, center, radiusSq, fn);
                if (includeOthers)
                    VisitInRadius(cell.Others, center, radiusSq, fn);
            }
        }
    }

    private static void VisitList(List<TEntity> list, ZoneEntity self, Action<TEntity> fn)
    {
        for (int i = 0; i < list.Count; ++i)
        {
            if (!ReferenceEquals(list[i], self))
                fn(list[i]);
        }
    }

    private static void VisitInRadius(List<TEntity> list, Vec2 center, float radiusSq, Action<TEntity> fn)
    {
        for (int i = 0; i < list.Count; ++i)
        {
            if (Vec2.DistanceSq(list[i]._pos, center) <= radiusSq)
                fn(list[i]);
        }
    }

    /// <summary>셀 공간 실수 좌표를 [0, count-1] 범위의 셀 인덱스로 변환한다. NaN/Inf/거대한 값도 안전하게 가장자리 셀로 보정한다.</summary>
    private static int ClampCell(float f, int count)
    {
        if (!(f > 0.0f))
            return 0;
        if (f >= (float)count)
            return count - 1;
        return (int)f;
    }

    private int CellX(float x) => ClampCell((x - _minX) * _invCellSize, _cols);
    private int CellY(float y) => ClampCell((y - _minY) * _invCellSize, _rows);
    private int CellIndexOf(Vec2 pos) => CellY(pos.Y) * _cols + CellX(pos.X);

    /// <summary>엔티티를 셀 목록 끝에 삽입하고 소속 정보를 기록한다.</summary>
    private void InsertToCell(TEntity entity, int cellIdx)
    {
        AoiCell<TEntity> owner = _cells[cellIdx];
        List<TEntity> cell = entity._aoiNode.Watcher ? owner.Watchers : owner.Others;

        entity._aoiNode.CellIdx = cellIdx;
        entity._aoiNode.Slot = (uint)cell.Count;
        cell.Add(entity);
    }

    /// <summary>엔티티를 소속 셀에서 O(1)로 제거한다. (마지막 원소와 교체 후 pop)</summary>
    private void EraseFromCell(TEntity entity)
    {
        AoiCell<TEntity> owner = _cells[entity._aoiNode.CellIdx];
        List<TEntity> cell = entity._aoiNode.Watcher ? owner.Watchers : owner.Others;

        TEntity last = cell[cell.Count - 1];
        cell[(int)entity._aoiNode.Slot] = last;
        last._aoiNode.Slot = entity._aoiNode.Slot;
        cell.RemoveAt(cell.Count - 1);

        entity._aoiNode.CellIdx = -1;
        entity._aoiNode.Slot = AoiNode.InvalidSlot;
    }

    /// <summary>
    /// centerCell 기준 시야 셀 중 excludeCell 기준 시야에 속하지 않는 셀의 엔티티들과 양방향으로 진입/이탈 통지를 주고받는다.
    /// </summary>
    /// <param name="excludeCell">제외할 시야의 중심 셀 (-1이면 제외 없음)</param>
    /// <param name="enter">true면 진입 통지, false면 이탈 통지</param>
    private void NotifyCells(TEntity entity, int centerCell, int excludeCell, bool enter)
    {
        int cx = centerCell % _cols;
        int cy = centerCell / _cols;
        int ex = excludeCell >= 0 ? excludeCell % _cols : 0;
        int ey = excludeCell >= 0 ? excludeCell / _cols : 0;

        // 관찰자가 아닌 엔티티는 주변 관찰자하고만 통지를 주고받는다. (두 엔티티 모두 관찰자가 아니면 통지가 필요 없다)
        bool scanOthers = entity._aoiNode.Watcher;

        // 통지 콜백에서 예외가 나가도 플래그가 켜진 채 남지 않게 한다.
        _dispatching = true;
        try
        {
            for (int y = Math.Max(cy - _view, 0); y <= Math.Min(cy + _view, _rows - 1); ++y)
            {
                for (int x = Math.Max(cx - _view, 0); x <= Math.Min(cx + _view, _cols - 1); ++x)
                {
                    if (excludeCell >= 0 && Math.Abs(x - ex) <= _view && Math.Abs(y - ey) <= _view)
                        continue;

                    AoiCell<TEntity> cell = _cells[(long)y * _cols + x];
                    Dispatch(entity, cell.Watchers, enter);
                    if (scanOthers)
                        Dispatch(entity, cell.Others, enter);
                }
            }
        }
        finally
        {
            _dispatching = false;
        }
    }

    /// <summary>목록의 각 엔티티와 entity 사이에 양방향 진입/이탈 통지를 주고받는다. (자기 자신은 건너뜀)</summary>
    private static void Dispatch(TEntity entity, List<TEntity> list, bool enter)
    {
        for (int i = 0; i < list.Count; ++i)
        {
            TEntity other = list[i];
            if (ReferenceEquals(other, entity))
                continue;

            if (enter)
            {
                entity.OnAoiEnter(other);
                other.OnAoiEnter(entity);
            }
            else
            {
                entity.OnAoiLeave(other);
                other.OnAoiLeave(entity);
            }
        }
    }
}

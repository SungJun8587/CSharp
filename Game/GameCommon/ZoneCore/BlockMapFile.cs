// BlockMapFile.cs : Block.json 읽기 (서버와 봇 클라이언트가 공유)
using System;
using Core.Util;
using Game.Generated;

namespace Game.ZoneCore;

/// <summary>Block.json에서 한 존의 장애물을 읽어 차단 맵(BlockMap)을 만든다.</summary>
public static class BlockMapFile
{
    /// <summary>
    /// Block.json에서 한 존의 장애물을 읽어 차단 맵을 만든다.
    /// 형식: {"Block": [ {"ZoneId","Type","X","Y","W","H","R"}, ... ]}
    ///   Type = "Rect"  : X,Y = 왼쪽 아래 모서리, W,H = 너비/높이 (R은 0)
    ///   Type = "Circle": X,Y = 중심, R = 반경 (W,H는 0)
    /// 모든 존이 한 파일을 공유하므로 다른 존의 행도 먼저 검증한다(오타가 해당 존을 띄울 때서야 드러나지 않게).
    /// </summary>
    /// <param name="file">Block.json 경로</param>
    /// <param name="zoneId">이 존의 ID (다른 존의 행은 검증만 하고 쓰지 않음)</param>
    /// <param name="cellSize">차단 맵 셀 한 변의 길이</param>
    /// <param name="map">[out] 차단 맵. 이 존의 행이 없으면 null</param>
    /// <param name="margin">
    /// 장애물을 사방으로 키우는 여유 폭(엔티티 반경 대신, 0 이상). 엔티티를 점으로 다루므로 몸 크기를 이 값으로 반영한다.
    /// 서버와 봇이 같은 값을 써야 같은 판정이 되므로 기본값을 공용 상수로 둔다. Rect는 사방으로, Circle은 반경에 더한다
    /// </param>
    /// <returns>성공(이 존의 행이 없는 경우 포함) 시 true, 파일/형식/값 오류나 셀 수 초과 시 false (원인은 로그)</returns>
    public static bool Load(string file, uint zoneId, float minX, float minY, float maxX, float maxY, float cellSize,
        out BlockMap? map, float margin = BlockMap.DefaultMargin)
    {
        map = null;

        if (!(float.IsFinite(margin) && margin >= 0.0f))
        {
            LogManager.Error("[BlockMap] margin must be finite and >= 0. margin={0}", (double)margin);
            return false;
        }

        if (!JsonTableLoader.TryLoadFile(file, out BlockTable? table, out string error))
        {
            LogManager.Error("[BlockMap] load failed. {0}", error);
            return false;
        }

        return Build(table!, file, zoneId, minX, minY, maxX, maxY, cellSize, out map, margin);
    }

    /// <summary>읽어 둔 BlockTable에서 한 존의 차단 맵을 만든다. 검증 규칙은 <see cref="Load"/>와 같다.</summary>
    public static bool Build(BlockTable table, string what, uint zoneId, float minX, float minY, float maxX, float maxY, float cellSize,
        out BlockMap? map, float margin = BlockMap.DefaultMargin)
    {
        map = null;

        for (int i = 0; i < table.Block.Count; ++i)
        {
            BlockRow r = table.Block[i];

            if (!double.IsFinite(r.X) || !double.IsFinite(r.Y))
            {
                LogManager.Error("[BlockMap] row {0}: X/Y must be finite. path={1}", i, what);
                return false;
            }

            if (r.Type == "Rect")
            {
                if (!(double.IsFinite(r.W) && double.IsFinite(r.H) && r.W > 0.0 && r.H > 0.0))
                {
                    LogManager.Error("[BlockMap] row {0}: Rect needs W > 0 and H > 0", i);
                    return false;
                }
            }
            else if (r.Type == "Circle")
            {
                if (!(double.IsFinite(r.R) && r.R > 0.0))
                {
                    LogManager.Error("[BlockMap] row {0}: Circle needs R > 0", i);
                    return false;
                }
            }
            else
            {
                LogManager.Error("[BlockMap] row {0}: Type must be \"Rect\" or \"Circle\". value={1}", i, r.Type);
                return false;
            }
        }

        BlockMap built = new BlockMap();
        if (!built.Init(minX, minY, maxX, maxY, cellSize))
        {
            LogManager.Error("[BlockMap] init failed (area invalid or too many cells). zoneId={0}", zoneId);
            return false;
        }

        uint used = 0;
        foreach (BlockRow r in table.Block)
        {
            if (r.ZoneId != zoneId)
                continue;

            if (r.Type == "Rect")
                built.AddRect((float)(r.X - margin), (float)(r.Y - margin), (float)(r.W + 2.0 * margin), (float)(r.H + 2.0 * margin));
            else
                built.AddCircle((float)r.X, (float)r.Y, (float)(r.R + margin));
            ++used;
        }

        if (used == 0)
            return true;        // 이 존에는 장애물이 없다

        LogManager.Info("[BlockMap] loaded. zoneId={0}, shapes={1}, margin={2:F2}, blockedCells={3}",
            zoneId, used, (double)margin, built.BlockedCellCount);
        map = built;
        return true;
    }
}

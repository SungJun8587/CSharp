// ZoneLinkTable.cs : 존 가장자리 관문(GameData/ZoneLink.json) 읽기와 위치 판정. 뷰어의 ZoneGates와 같은 규칙이다.
using System;
using System.Collections.Generic;
using System.IO;
using Core.Util;
using Game.ZoneCore;

namespace Game.ZoneServer;

/// <summary>관문 하나. Edge: W/E는 x가 최소/최대인 가장자리, N/S는 y가 최소/최대인 가장자리. Pos는 가장자리를 따라간 중심 좌표, Width는 폭.</summary>
public sealed class ZoneGateLink
{
    public char Edge;
    public float Pos;
    public float Width;
    public uint TargetZone;
    public float TargetX;
    public float TargetY;
}

public static class ZoneLinkTable
{
    // 파일 형식. 다른 게임 데이터와 같은 엄격한 읽기(JsonTableLoader)를 쓴다: 모르는 키, 잘못된 형식, 범위를 벗어난 정수는 오류다.
    private sealed class LinkFile
    {
        public List<LinkRow> ZoneLink { get; set; } = new List<LinkRow>();
    }

    private sealed class LinkRow
    {
        public uint ZoneId { get; set; }
        public string Edge { get; set; } = string.Empty;
        public float Pos { get; set; }
        public float Width { get; set; }
        public uint TargetZone { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
    }

    /// <summary>
    /// 이 존에서 나가는 관문을 읽는다. 파일이 없으면 빈 목록(관문 없음)이고, 있는데 형식이 틀리면 null이다.
    /// 행은 {"ZoneId","Edge","Pos","Width","TargetZone","TargetX","TargetY"}이며 ZoneId가 zoneId인 것만 쓴다.
    /// </summary>
    public static List<ZoneGateLink>? Load(string dir, uint zoneId)
    {
        List<ZoneGateLink> list = new List<ZoneGateLink>();
        string path = Path.Combine(dir, "ZoneLink.json");
        if (!File.Exists(path))
            return list;

        if (!JsonTableLoader.TryLoadFile(path, out LinkFile? file, out string error))
            return Fail(path, error);

        for (int i = 0; i < file!.ZoneLink.Count; ++i)
        {
            LinkRow r = file.ZoneLink[i];
            int row = i + 1;
            if (r.ZoneId != zoneId)
                continue;
            if (r.Edge.Length != 1 || "WENS".IndexOf(r.Edge[0]) < 0)
                return Fail(path, "행 " + row + ": Edge는 W/E/N/S 중 하나");
            if (!float.IsFinite(r.Pos) || !float.IsFinite(r.Width) || r.Width <= 0f || !float.IsFinite(r.TargetX) || !float.IsFinite(r.TargetY))
                return Fail(path, "행 " + row + ": 숫자 열(Width는 양수, 모두 유한한 값)");

            list.Add(new ZoneGateLink { Edge = r.Edge[0], Pos = r.Pos, Width = r.Width, TargetZone = r.TargetZone, TargetX = r.TargetX, TargetY = r.TargetY });
        }
        return list;
    }

    private static List<ZoneGateLink>? Fail(string path, string why)
    {
        LogManager.Error("[ZoneLinkTable] {0}: {1}", path, why);
        return null;
    }

    /// <summary>점이 관문 안쪽 가장자리(깊이 reach 이내)이고 관문 폭 안이면 그 관문, 아니면 null. (뷰어는 깊이 3에서 반응한다.)</summary>
    public static ZoneGateLink? Find(List<ZoneGateLink> gates, Vec2 p, float minX, float minY, float maxX, float maxY, float reach)
    {
        for (int i = 0; i < gates.Count; ++i)
        {
            ZoneGateLink g = gates[i];
            float inward, along;
            switch (g.Edge)
            {
                case 'W': inward = p.X - minX; along = p.Y; break;
                case 'E': inward = maxX - p.X; along = p.Y; break;
                case 'N': inward = p.Y - minY; along = p.X; break;
                default: inward = maxY - p.Y; along = p.X; break;
            }
            if (inward <= reach && MathF.Abs(along - g.Pos) <= g.Width * 0.5f)
                return g;
        }
        return null;
    }
}

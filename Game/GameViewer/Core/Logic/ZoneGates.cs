//***************************************************************************
// ZoneGates.cs : 존 사이 관문. GameData 의 ZoneLink 표(존 가장자리 관문 위치, 이어지는 존, 도착 지점)를 그리는 쪽이 쓰기 좋게 계산해 준다.
// - Weight: 한 점이 관문에 얼마나 가까운지(0~1)와 건너편 존의 지형 종류. 바닥을 건너편 지형으로 섞는 데 쓴다.
// - Entered: 점이 관문 안쪽 가장자리에 닿았는지(존 이동을 시작할 자리).
// Edge: W/E 는 x 가 최소/최대인 가장자리, N/S 는 y 가 최소/최대인 가장자리.
//***************************************************************************
using System;
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    public sealed class ZoneGate
    {
        public char Edge;
        public float Pos, Width;
        public uint TargetZone;
        public float TargetX, TargetY;
    }

    public static class ZoneGates
    {
        public const float BlendDepth = 30f;      // 가장자리에서 안쪽으로 건너편 지형이 번져 들어오는 깊이
        public const float AlongFall = 22f;       // 관문 폭 밖으로 번지는 거리
        public const float OutsideReach = 46f;    // 가장자리 밖으로 길이 이어지는 거리

        public static List<ZoneGate> Current = new List<ZoneGate>();

        /// <summary>가장자리까지의 안쪽 거리(바깥이면 음수)와 가장자리를 따라간 좌표.</summary>
        public static void Locate(ZoneGate g, float x, float y, float minX, float minY, float maxX, float maxY, out float inward, out float along)
        {
            switch (g.Edge)
            {
                case 'W': inward = x - minX; along = y; break;
                case 'E': inward = maxX - x; along = y; break;
                case 'N': inward = y - minY; along = x; break;
                default: inward = maxY - y; along = x; break;
            }
        }

        private static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        /// <summary>점에 닿는 건너편 지형의 세기(0~1). 가장 센 관문의 값을 돌려준다.</summary>
        public static float Weight(IList<ZoneGate> gates, float x, float y, float minX, float minY, float maxX, float maxY, out MapKind kind, out bool outside)
        {
            kind = MapKind.Forest; outside = false;
            float best = 0f;
            if (gates == null) return 0f;
            for (int i = 0; i < gates.Count; ++i)
            {
                ZoneGate g = gates[i];
                float inward, along; Locate(g, x, y, minX, minY, maxX, maxY, out inward, out along);
                float off = Math.Max(0f, Math.Abs(along - g.Pos) - g.Width * 0.5f);
                float a = Smooth(1f - off / AlongFall);
                if (a <= 0f) continue;
                float t;
                if (inward < 0f) { if (-inward > OutsideReach) continue; t = 1f; }
                else t = Smooth(1f - inward / BlendDepth);
                float w = t * a;
                if (w > best) { best = w; kind = ZoneTheme.For(g.TargetZone); outside = inward < 0f; }
            }
            return best;
        }

        /// <summary>점이 관문 안쪽 가장자리(깊이 depth 이내)에 있고 관문 폭 안이면 그 관문.</summary>
        public static ZoneGate Entered(IList<ZoneGate> gates, float x, float y, float minX, float minY, float maxX, float maxY, float depth)
        {
            if (gates == null) return null;
            for (int i = 0; i < gates.Count; ++i)
            {
                ZoneGate g = gates[i];
                float inward, along; Locate(g, x, y, minX, minY, maxX, maxY, out inward, out along);
                if (inward <= depth && Math.Abs(along - g.Pos) <= g.Width * 0.5f) return g;
            }
            return null;
        }

        /// <summary>가장자리 위 관문 중심의 월드 좌표.</summary>
        public static void CenterOf(ZoneGate g, float minX, float minY, float maxX, float maxY, out float x, out float y)
        {
            switch (g.Edge)
            {
                case 'W': x = minX; y = g.Pos; break;
                case 'E': x = maxX; y = g.Pos; break;
                case 'N': x = g.Pos; y = minY; break;
                default: x = g.Pos; y = maxY; break;
            }
        }
    }
}

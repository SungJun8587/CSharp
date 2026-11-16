//***************************************************************************
// ZoneTheme.cs : 존마다 맵 분위기(숲/사막/설원/화산/늪/폐허/해안/수정굴/천상/암흑). 그리는 쪽(App)이 현재 존의 종류를 보고 바닥/장애물/공기 표현을 고른다.
// 존 번호 -> 종류 표는 여기 한 곳에만 둔다. 표에 없는 존은 숲이다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    public enum MapKind { Forest, Desert, Snow, Volcano, Swamp, Ruins, Coast, Crystal, Sky, Dark }

    public static class ZoneTheme
    {
        private static readonly Dictionary<uint, MapKind> ById = new Dictionary<uint, MapKind>
        {
            { 1, MapKind.Forest },
            { 2, MapKind.Desert },
            { 3, MapKind.Snow },
            { 4, MapKind.Volcano },
            { 5, MapKind.Swamp },
            { 6, MapKind.Ruins },
            { 7, MapKind.Coast },
            { 8, MapKind.Crystal },
            { 9, MapKind.Sky },
            { 10, MapKind.Dark },
        };

        /// <summary>고를 수 있는 존 번호(시작 화면의 맵 선택에 쓴다).</summary>
        public static readonly uint[] Zones = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

        public static string NameOf(uint zoneId)
        {
            switch (For(zoneId))
            {
                case MapKind.Desert: return "사막";
                case MapKind.Snow: return "설원";
                case MapKind.Volcano: return "화산";
                case MapKind.Swamp: return "늪";
                case MapKind.Ruins: return "폐허";
                case MapKind.Coast: return "해안";
                case MapKind.Crystal: return "수정굴";
                case MapKind.Sky: return "천상";
                case MapKind.Dark: return "암흑";
                default: return "숲";
            }
        }

        /// <summary>웅덩이 자리(존 번호 -> x,y,w,h)는 ZonePools.gen.cs (tools/make_zones.py 가 만든다).
        /// 서버에는 같은 좌표의 Rect 장애물로 들어 있어(통행 불가) 뷰어는 이 좌표와 같은 Rect 를 바위 대신 웅덩이로 그린다.
        /// 웅덩이의 종류(물/얼음/용암/늪물)는 존 종류가 정한다.</summary>
        public static bool IsPool(uint zoneId, float x, float y, float w, float h)
        {
            float[][] list;
            if (!ZonePools.All.TryGetValue(zoneId, out list)) return false;
            for (int i = 0; i < list.Length; ++i)
                if (System.Math.Abs(list[i][0] - x) < 0.01f && System.Math.Abs(list[i][1] - y) < 0.01f && System.Math.Abs(list[i][2] - w) < 0.01f && System.Math.Abs(list[i][3] - h) < 0.01f) return true;
            return false;
        }

        /// <summary>지금 그리는 존의 번호(그리기를 시작할 때 SceneRenderer 가 정한다).</summary>
        public static uint CurrentZoneId = 1;

        /// <summary>지금 그리는 존의 종류. 장면 그리기를 시작할 때 SceneRenderer 가 정한다.</summary>
        public static MapKind Current = MapKind.Forest;

        public static MapKind For(uint zoneId)
        {
            MapKind k;
            return ById.TryGetValue(zoneId, out k) ? k : MapKind.Forest;
        }

        /// <summary>장애물(원형) 반지름으로 소품 크기를 고른다(설원/화산/늪): 작은 것 / 중간 / 큰 것.</summary>
        public enum PropSize { Small, Mid, Large }
        public static PropSize SizeOf(float radius)
        {
            if (radius <= 2.0f) return PropSize.Small;
            if (radius < 3.2f) return PropSize.Mid;
            return PropSize.Large;
        }

        /// <summary>장애물(원형) 반지름으로 사막 소품을 고른다: 선인장 / 고목 / 야자수.</summary>
        public enum DesertProp { Cactus, DeadTree, Palm }
        public static DesertProp PropOf(float radius)
        {
            if (radius <= 2.0f) return DesertProp.Cactus;
            if (radius < 3.2f) return DesertProp.DeadTree;
            return DesertProp.Palm;
        }
    }
}

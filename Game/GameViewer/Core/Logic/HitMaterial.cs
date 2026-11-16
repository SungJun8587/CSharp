using System;

namespace GameViewer.Core.Logic
{
    /// <summary>몸의 재질. 맞을 때 튀는 조각과 죽을 때 터지는 모양을 가른다.</summary>
    public enum HitMat { Flesh, Gel, Plant, Wood, Bone, Stone, Metal, Shell, Fire, Spirit }

    public static class HitMaterial
    {
        /// <summary>몬스터 템플릿(보스판 101~ 포함)의 재질. 몬스터가 아니면 Flesh.</summary>
        public static HitMat Of(uint templateId, bool isMonster)
        {
            if (!isMonster) return HitMat.Flesh;
            switch ((int)(templateId % 100))
            {
                case 1: return HitMat.Gel;                      // 슬라임
                case 2: case 3: return HitMat.Plant;            // 버섯, 선인장
                case 4: case 11: case 13: return HitMat.Metal;  // 폭탄, 암흑/강철 기사
                case 9: case 16: case 20: case 24: case 25: return HitMat.Shell;   // 게, 거미, 전갈, 소라게, 사마귀
                case 10: return HitMat.Wood;                    // 나무 정령
                case 14: return HitMat.Fire;                    // 불꽃
                case 17: return HitMat.Bone;                    // 해골
                case 18: return HitMat.Stone;                   // 골렘
                case 19: return HitMat.Spirit;                  // 유령
                default: return HitMat.Flesh;                   // 늑대, 고블린, 지렁이, 박쥐, 설인, 도마뱀, 두꺼비, 독수리
            }
        }

        /// <summary>조각의 두 가지 색(0xRRGGBB).</summary>
        public static void Colors(HitMat m, out int c1, out int c2)
        {
            switch (m)
            {
                case HitMat.Gel: c1 = 0x6EE16E; c2 = 0xB8F5B0; break;
                case HitMat.Plant: c1 = 0x78BE50; c2 = 0xD8483C; break;
                case HitMat.Wood: c1 = 0x96643C; c2 = 0x5E8A3C; break;
                case HitMat.Bone: c1 = 0xEEE8D4; c2 = 0xC8BFA4; break;
                case HitMat.Stone: c1 = 0x969088; c2 = 0x6E6A66; break;
                case HitMat.Metal: c1 = 0xC8CEDC; c2 = 0xFFD870; break;
                case HitMat.Shell: c1 = 0xD27858; c2 = 0xEBD2AA; break;
                case HitMat.Fire: c1 = 0xFFAA3C; c2 = 0xFFE28A; break;
                case HitMat.Spirit: c1 = 0xAAD2FF; c2 = 0xE6F2FF; break;
                default: c1 = 0xAA9682; c2 = 0xD8C8B0; break;
            }
        }
    }
}

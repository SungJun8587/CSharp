//***************************************************************************
// SkillFx.cs : 스킬 연출 양식 표. 스킬 ID마다 어떤 모양/색으로 그릴지 정한다(그리기는 App 의 SkillEffects 가 맡는다).
// 표에 없는 스킬은 데이터(범위/투사체/상태 효과)로 알맞은 양식을 고른다. 서버 데이터가 바뀌어도 연출이 비지 않게 한다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Data
{
    public enum SkillFxStyle
    {
        Default,    // 기본 (기존 연출)
        Blade,      // 묵직한 베기: 겹친 초승달 궤적 + 타격 섬광
        Pierce,     // 급소 찌르기: 돌진 선 + 교차하는 가는 칼날 + 핏빛 파편
        Lance,      // 창 찌르기: 대상 너머까지 꿰뚫는 굵은 줄기 + 속도선 + 퍼지는 고리와 방사선. 직선 스킬은 창끝 쐐기와 충격 고리가 사거리를 따라 달린다
        Whirl,      // 회전 베기: 중심을 도는 칼날 호
        Arrow,      // 화살: 길게 빛나는 궤적 + 적중 섬광
        Volley,     // 화살비: 위에서 쏟아지는 화살
        Fireball,   // 불덩이: 소용돌이치는 불씨 + 착탄 폭발
        Blast,      // 대폭발: 화구, 충격파, 불꽃, 연기, 파편
        Roar,       // 함성: 퍼져 나가는 충격 고리
        Quake,      // 대지 강타: 균열, 흙먼지, 돌조각
        Poison,     // 독 안개: 피어오르는 구름과 거품
        Heal,       // 치유: 빛기둥과 떠오르는 십자
        Nova,       // 충격파: 날카로운 확산 고리와 방사선
    }

    public sealed class SkillFxInfo
    {
        public SkillFxStyle Style;
        public int Color1, Color2;      // 0xRRGGBB. 1: 주색, 2: 보조(밝은 쪽)
        public int BurstMs;             // 범위 폭발이 화면에 남는 시간
        public float Shake;             // 화면 흔들림 세기(px). 0 이면 없음
        public SkillFxInfo(SkillFxStyle s, int c1, int c2, int burstMs, float shake) { Style = s; Color1 = c1; Color2 = c2; BurstMs = burstMs; Shake = shake; }
    }

    public static class SkillFxTable
    {
        private static readonly Dictionary<uint, SkillFxInfo> ById = new Dictionary<uint, SkillFxInfo>
        {
            { 1,  new SkillFxInfo(SkillFxStyle.Blade,    0xFF9A2E, 0xFFE9A0, 0, 0f) },       // 강타
            { 2,  new SkillFxInfo(SkillFxStyle.Whirl,    0x5CC8FF, 0xE6F8FF, 800, 0f) },     // 회전베기
            { 3,  new SkillFxInfo(SkillFxStyle.Arrow,    0x6FE0FF, 0xFFFFFF, 0, 0f) },       // 정밀사격
            { 4,  new SkillFxInfo(SkillFxStyle.Volley,   0xE6D8A8, 0xFFFFFF, 1000, 0f) },    // 화살비
            { 5,  new SkillFxInfo(SkillFxStyle.Fireball, 0xFF6A1E, 0xFFF0A0, 0, 0f) },       // 파이어볼
            { 6,  new SkillFxInfo(SkillFxStyle.Blast,    0xFF5A14, 0xFFE08A, 1100, 5f) },    // 폭발
            { 7,  new SkillFxInfo(SkillFxStyle.Roar,     0xFFC850, 0xFFF4CC, 800, 0f) },     // 전투 함성
            { 8,  new SkillFxInfo(SkillFxStyle.Quake,    0xC89650, 0xFFE2A8, 1100, 6f) },    // 대지 강타 (보스)
            { 9,  new SkillFxInfo(SkillFxStyle.Roar,     0xFF5A4A, 0xFFD0B8, 1000, 4f) },    // 포효 (보스)
            { 10, new SkillFxInfo(SkillFxStyle.Poison,   0x6CD23C, 0xD8FF9A, 1500, 0f) },    // 독 안개 (보스)
            { 11, new SkillFxInfo(SkillFxStyle.Poison,   0x8CE05A, 0xF0FFB0, 1100, 3f) },    // 점액 폭발 (보스)
            { 12, new SkillFxInfo(SkillFxStyle.Heal,     0x78FF9E, 0xF0FFE0, 900, 0f) },     // 치유
            { 13, new SkillFxInfo(SkillFxStyle.Heal,     0xFFE680, 0xFFFFF0, 1100, 0f) },    // 치유의 빛
            { 14, new SkillFxInfo(SkillFxStyle.Blade,    0xFF6A1E, 0xFFE08A, 0, 0f) },       // 화염 부채
            { 15, new SkillFxInfo(SkillFxStyle.Arrow,    0xFFB45A, 0xFFFFFF, 0, 0f) },       // 관통 사격
            { 16, new SkillFxInfo(SkillFxStyle.Nova,     0xB48CFF, 0xF0E6FF, 900, 4f) },     // 충격파
            { 17, new SkillFxInfo(SkillFxStyle.Heal,     0x6CC8FF, 0xE6F8FF, 900, 0f) },     // 수호의 방패
            { 18, new SkillFxInfo(SkillFxStyle.Lance,    0x34D4E0, 0xE8FFFF, 0, 3f) },       // 관통 찌르기 (직선)
            { 19, new SkillFxInfo(SkillFxStyle.Whirl,    0x40C8D8, 0xE0FFFF, 800, 3f) },     // 회전 창격
            { 20, new SkillFxInfo(SkillFxStyle.Arrow,    0xB4F060, 0xF4FFD0, 0, 0f) },       // 속박 사격
            { 21, new SkillFxInfo(SkillFxStyle.Arrow,    0x5CD43C, 0xE4FFB8, 0, 0f) },       // 독화살
            { 22, new SkillFxInfo(SkillFxStyle.Roar,     0xFFA830, 0xFFE8A0, 800, 0f) },     // 집중
            { 23, new SkillFxInfo(SkillFxStyle.Blade,    0xFFC850, 0xFFF4C0, 0, 0f) },       // 일제 사격 (부채꼴)
            { 24, new SkillFxInfo(SkillFxStyle.Lance,    0xE04048, 0xFFE0E0, 0, 2f) },       // 찢는 창끝
            { 25, new SkillFxInfo(SkillFxStyle.Nova,     0x58E0C0, 0xE0FFF4, 700, 0f) },     // 질풍 돌격
            { 26, new SkillFxInfo(SkillFxStyle.Lance,    0xFFB84A, 0xFFF4D8, 0, 3f) },       // 갑옷 꿰기
            { 27, new SkillFxInfo(SkillFxStyle.Blade,    0xFFE080, 0xFFFFFF, 0, 4f) },       // 제압 휘두르기 (부채꼴)
        };
        /// <summary>지속 피해 상태의 불씨 색. 표에 없으면 주황(화상). 독은 초록, 출혈은 붉은색.</summary>
        public static int DotColor(uint statusId)
        {
            switch (statusId)
            {
                case 2: return 0xE03038;
                case 11: return 0x7CE040;
                default: return 0xFF8228;
            }
        }

        private static readonly SkillFxInfo Plain = new SkillFxInfo(SkillFxStyle.Default, 0xFFE282, 0xFFFFFF, 0, 0f);

        /// <summary>스킬의 연출 양식. 표에 없으면 데이터로 고른다(투사체 -> 화살/불덩이, 범위 -> 회전/폭발, 그 외 -> 베기).</summary>
        public static SkillFxInfo For(GameData data, uint skillId)
        {
            SkillFxInfo info;
            SkillRow sk = data != null ? data.Skill(skillId) : null;
            // 이름/모양이 표와 같은 종류일 때만 쓴다: 서버가 번호를 다르게 매긴 데이터에서 엉뚱한 연출이 나오지 않게 한다.
            if (ById.TryGetValue(skillId, out info) && (sk == null || Compatible(info.Style, sk))) return info;
            if (sk == null) return Plain;
            if (sk.ProjectileSpeed > 0) return sk.ProjectileSpeed >= 50 ? new SkillFxInfo(SkillFxStyle.Arrow, 0x6FE0FF, 0xFFFFFF, 0, 0f) : new SkillFxInfo(SkillFxStyle.Fireball, 0xFF6A1E, 0xFFF0A0, 0, 0f);
            if (sk.IsBurst) return sk.Radius >= 5 ? new SkillFxInfo(SkillFxStyle.Blast, 0xFF5A14, 0xFFE08A, 1100, 4f) : new SkillFxInfo(SkillFxStyle.Whirl, 0x5CC8FF, 0xE6F8FF, 800, 0f);
            return new SkillFxInfo(SkillFxStyle.Blade, 0xFFB050, 0xFFEEC0, 0, 0f);
        }

        private static bool Compatible(SkillFxStyle style, SkillRow sk)
        {
            bool proj = sk.ProjectileSpeed > 0;
            switch (style)
            {
                case SkillFxStyle.Arrow: case SkillFxStyle.Fireball: return proj || !sk.IsBurst;     // 투사체이거나 단일 대상
                case SkillFxStyle.Blade: case SkillFxStyle.Pierce: case SkillFxStyle.Lance: return !sk.IsBurst && !proj;
                default: return true;                                                                 // 범위/치유 양식은 모양 제한을 두지 않는다
            }
        }
    }
}

//***************************************************************************
// MonsterTraits.cs : 몬스터 종류(템플릿 ID)별 연출 성질. 공격 동작 방식, 날아다니는지, 가만히 있어도 움직이는지.
// 값은 이미지 에셋 목록(Assets/sprites.json 의 "attack", "flying", "idleAnim")에서 읽어 등록한다. 등록이 없는 종류는 달려들어 무는 기본 동작.
// Core 는 그림을 모르므로 이 표만 보고 동작을 고른다.
//***************************************************************************
using System.Collections.Generic;

namespace GameViewer.Core.Logic
{
    public static class MonsterTraits
    {
        private struct Row { public AttackStyle Style; public bool Flying, IdleAnim; }
        private static readonly Dictionary<uint, Row> Rows = new Dictionary<uint, Row>();
        private static readonly object Lock = new object();

        public static void Set(uint tpl, AttackStyle style, bool flying, bool idleAnim)
        {
            Row r; r.Style = style; r.Flying = flying; r.IdleAnim = idleAnim;
            lock (Lock) Rows[tpl] = r;
        }

        public static void Clear() { lock (Lock) Rows.Clear(); }

        /// <summary>종류의 공격 동작. 등록이 없으면 Lunge.</summary>
        public static AttackStyle StyleOf(uint tpl) { Row r; lock (Lock) return Rows.TryGetValue(tpl, out r) ? r.Style : AttackStyle.Lunge; }
        public static bool IsFlying(uint tpl) { Row r; lock (Lock) return Rows.TryGetValue(tpl, out r) && r.Flying; }
        public static bool AnimatesIdle(uint tpl) { Row r; lock (Lock) return Rows.TryGetValue(tpl, out r) && r.IdleAnim; }

        /// <summary>"lunge"/"hop"/"slam" (대소문자 무시)를 해석한다.</summary>
        public static bool TryParseStyle(string s, out AttackStyle style)
        {
            style = AttackStyle.Lunge;
            if (s == null) return false;
            switch (s.ToLowerInvariant())
            {
                case "lunge": style = AttackStyle.Lunge; return true;
                case "hop": style = AttackStyle.Hop; return true;
                case "slam": style = AttackStyle.Slam; return true;
            }
            return false;
        }
    }
}

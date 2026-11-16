//***************************************************************************
// StatusLook.cs : 상태 효과(Status 데이터)를 화면에서 어떻게 분류/표시할지 정한다 — 이로운지 해로운지, 대표 글자, 색 계열.
//***************************************************************************
using System;
using GameViewer.Core.Data;

namespace GameViewer.Core.Logic
{
    public static class StatusLook
    {
        /// <summary>해로운 효과인가: 지속 피해이거나 공격/방어/이동 속도가 깎인다.</summary>
        public static bool IsDebuff(StatusRow r)
        {
            if (r == null) return false;
            if (r.IsDot) return true;
            return r.AttackPercent < 0 || r.DefensePercent < 0 || r.SpeedPercent < 0;
        }

        /// <summary>아이콘 가운데 글자(이름 첫 글자).</summary>
        public static string Glyph(StatusRow r, uint id)
        {
            if (r != null && !string.IsNullOrEmpty(r.Name)) return r.Name.Substring(0, 1);
            return "?";
        }

        /// <summary>같은 id 는 항상 같은 색 계열(0..5)이 나오도록 한다.</summary>
        public static int Hue(uint id) { return (int)(id * 2654435761u >> 24) % 6; }

        /// <summary>남은 시간 문자열: 10초 이상은 정수, 미만은 소수 한 자리.</summary>
        public static string Remaining(long leftMs)
        {
            if (leftMs <= 0) return "0.0";
            double s = leftMs / 1000.0;
            return s >= 10 ? ((int)s).ToString() : s.ToString("0.0");
        }
    }
}

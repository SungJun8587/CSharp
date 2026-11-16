//***************************************************************************
// SkillTip.cs : 스킬/기본 공격 툴팁에 넣을 줄을 데이터(Skill/Status/Hero 표)에서 만든다. 서버가 쓰는 같은 표라 숫자가 서버 값과 일치한다.
// 순수 문자열 규칙이라 그리는 쪽(WinForms/Unity)과 무관하게 시험할 수 있다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using GameViewer.Core.Data;

namespace GameViewer.Core.Logic
{
    public enum TipKind { Title, Stat, Effect, Note }

    public struct TipLine
    {
        public string Text; public TipKind Kind;
        public TipLine(string t, TipKind k) { Text = t; Kind = k; }
    }

    public static class SkillTip
    {
        private static string N(double v) { return v.ToString("0.#", CultureInfo.InvariantCulture); }
        private static string Sec(uint ms) { return N(ms / 1000.0) + "초"; }

        public static string ShapeName(SkillRow sk)
        {
            if (sk.IsSelf) return "자신 중심";
            if (sk.IsArea) return "범위 (지정 지점)";
            if (sk.IsCone) return "부채꼴";
            if (sk.IsLine) return "직선";
            return "단일 대상";
        }

        public static List<TipLine> Build(GameData d, SkillRow sk, string keyLabel)
        {
            List<TipLine> l = new List<TipLine>();
            l.Add(new TipLine(sk.Name + (string.IsNullOrEmpty(keyLabel) ? "" : "   [" + keyLabel + "]"), TipKind.Title));
            l.Add(new TipLine(ShapeName(sk) + " · " + (sk.IsAlly ? "아군" : (sk.IsSelf ? "자신" : "적")), TipKind.Stat));
            string reach = "";
            if (!sk.IsSelf) reach += "사거리 " + N(sk.Range);
            if (sk.Radius > 0) reach += (reach.Length > 0 ? " · " : "") + "반경 " + N(sk.Radius);
            if (sk.IsCone && sk.Angle > 0) reach += (reach.Length > 0 ? " · " : "") + "각도 " + N(sk.Angle) + "°";
            if (sk.MaxTargets > 0 && !sk.IsSingle) reach += (reach.Length > 0 ? " · " : "") + "최대 " + sk.MaxTargets + "명";
            if (reach.Length > 0) l.Add(new TipLine(reach, TipKind.Stat));
            string timing = "재사용 " + Sec(sk.CooldownMs);
            timing += sk.CastTimeMs > 0 ? " · 시전 " + Sec(sk.CastTimeMs) : " · 즉시";
            if (sk.ProjectileSpeed > 0) timing += " · 투사체";
            l.Add(new TipLine(timing, TipKind.Stat));
            if (sk.DamagePercent > 0 || sk.FlatDamage > 0)
                l.Add(new TipLine("피해 공격력의 " + sk.DamagePercent + "%" + (sk.FlatDamage > 0 ? " + " + sk.FlatDamage : ""), TipKind.Effect));
            if (sk.Heals)
                l.Add(new TipLine("치유 최대 체력의 " + sk.HealPercent + "%" + (sk.FlatHeal > 0 ? " + " + sk.FlatHeal : ""), TipKind.Effect));
            if (sk.StatusId != 0)
            {
                StatusRow st = d.Status(sk.StatusId);
                if (st == null) l.Add(new TipLine("효과 #" + sk.StatusId, TipKind.Effect));
                else
                {
                    l.Add(new TipLine("효과 " + st.Name + (st.DurationMs > 0 ? " (" + Sec(st.DurationMs) + ")" : ""), TipKind.Effect));
                    string detail = StatusDetail(st);
                    if (detail.Length > 0) l.Add(new TipLine("  " + detail, TipKind.Note));
                }
            }
            return l;
        }

        /// <summary>상태 효과 한 줄 설명(지속 피해, 능력치 증감).</summary>
        public static string StatusDetail(StatusRow st)
        {
            List<string> parts = new List<string>();
            if (st.IsDot && st.DamagePercent > 0) parts.Add((st.TickMs > 0 ? N(st.TickMs / 1000.0) + "초마다 " : "") + "공격력의 " + st.DamagePercent + "% 피해");
            if (st.AttackPercent != 0) parts.Add("공격 " + Pct(st.AttackPercent));
            if (st.DefensePercent != 0) parts.Add("방어 " + Pct(st.DefensePercent));
            if (st.SpeedPercent != 0) parts.Add("이동 " + Pct(st.SpeedPercent));
            if (parts.Count == 0 && !string.IsNullOrEmpty(st.Kind) && !st.IsDot) parts.Add(st.Kind);
            return string.Join(", ", parts.ToArray());
        }

        private static string Pct(int p) { return (p > 0 ? "+" : "") + p + "%"; }

        public static List<TipLine> BuildBasic(HeroRow h, string keyLabel)
        {
            List<TipLine> l = new List<TipLine>();
            l.Add(new TipLine("기본 공격" + (string.IsNullOrEmpty(keyLabel) ? "" : "   [" + keyLabel + "]"), TipKind.Title));
            if (h == null) { l.Add(new TipLine("영웅 정보 없음", TipKind.Note)); return l; }
            l.Add(new TipLine("단일 대상 · 적", TipKind.Stat));
            l.Add(new TipLine("사거리 " + N(h.AttackRange) + " · 간격 " + Sec(h.AttackIntervalMs), TipKind.Stat));
            l.Add(new TipLine("피해 공격력 " + h.Attack, TipKind.Effect));
            l.Add(new TipLine("대상이 멀면 다가가서 공격합니다", TipKind.Note));
            return l;
        }
    }
}

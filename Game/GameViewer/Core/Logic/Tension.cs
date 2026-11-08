//***************************************************************************
// Tension.cs : 긴박감 연출의 규칙(순수 계산). 화면/소리 장치를 모르므로 테스트할 수 있다.
//  - Level/HeartPeriodMs/Pulse : 체력이 낮을수록 화면 가장자리가 붉게 맥동하고 심장 박동이 빨라진다.
//  - HeartbeatClock            : 박동 소리를 낼 시각을 정한다(죽었거나 안전하면 조용).
//  - KillStreak                : 짧은 시간 안의 연속 처치 횟수와 칭호.
//  - Threats                   : 화면 밖에서 나를 향해 달려오는 몬스터 목록(가장자리 화살표용).
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public static class Tension
    {
        public const float LowHpStart = 0.40f, LowHpFull = 0.05f;

        /// <summary>체력 비율 -> 긴장도 0~1. 40% 이하에서 시작해 5% 이하에서 최대.</summary>
        public static float Level(int hp, int maxHp)
        {
            if (maxHp <= 0 || hp <= 0) return 0f;
            float r = hp / (float)maxHp;
            if (r >= LowHpStart) return 0f;
            float l = (LowHpStart - r) / (LowHpStart - LowHpFull);
            return l > 1f ? 1f : l;
        }

        /// <summary>한 박동의 길이(ms). 긴장도 0 에서 1000ms(60회/분), 1 에서 430ms(약 140회/분).</summary>
        public static int HeartPeriodMs(float level)
        {
            if (level < 0f) level = 0f; if (level > 1f) level = 1f;
            return (int)(1000f - 570f * level);
        }

        /// <summary>
        /// 박동 한 주기 안의 세기 0~1: 쿵(주기 시작) - 쿵(0.2 지점, 조금 약하게) 두 번 뛰고 가라앉는다.
        /// </summary>
        public static float Pulse(long now, int periodMs)
        {
            if (periodMs <= 0) return 0f;
            float p = (float)(((now % periodMs) + periodMs) % periodMs) / periodMs;
            float lub = (float)Math.Exp(-p * 12.0);
            float dub = p >= 0.2f ? 0.6f * (float)Math.Exp(-(p - 0.2f) * 12.0) : 0f;
            return Math.Max(lub, dub);
        }

        /// <summary>
        /// 화면 밖에서 나를 향해 오는 몬스터(보스 제외)를 가까운 순으로 모은다.
        /// "달려온다"는 서버 AI 상태를 모르므로 이동 중이고 도착점이 내 근처인지로 추정한다.
        /// </summary>
        public static void Threats(WorldModel w, float selfX, float selfY, long now, float maxDist, float onScreenHalfW, float onScreenHalfH, List<Entity> outList, int max)
        {
            outList.Clear();
            float best2 = maxDist * maxDist;
            foreach (KeyValuePair<ulong, Entity> kv in w.Entities)
            {
                Entity e = kv.Value;
                if (!e.IsMonster || e.Dead || e.IsBoss || !e.Moving || e.Speed <= 0f) continue;
                float x, y; e.PosAt(now, out x, out y);
                float dx = x - selfX, dy = y - selfY;
                if (dx * dx + dy * dy > best2) continue;
                if (Math.Abs(dx) < onScreenHalfW && Math.Abs(dy) < onScreenHalfH) continue;        // 화면 안이면 직접 보인다
                float tx = e.DestX - selfX, ty = e.DestY - selfY;
                if (tx * tx + ty * ty > 5f * 5f) continue;                                         // 내게 오는 게 아님
                outList.Add(e);
            }
            // 가까운 순 정렬 후 자르기
            outList.Sort(delegate (Entity a, Entity b)
            {
                float ax, ay, bx, by; a.PosAt(now, out ax, out ay); b.PosAt(now, out bx, out by);
                float da = (ax - selfX) * (ax - selfX) + (ay - selfY) * (ay - selfY);
                float db = (bx - selfX) * (bx - selfX) + (by - selfY) * (by - selfY);
                return da.CompareTo(db);
            });
            if (outList.Count > max) outList.RemoveRange(max, outList.Count - max);
        }
    }

    /// <summary>박동 소리를 낼 시각을 정한다. 긴장도가 0 이거나 죽었으면 멈추고, 다시 시작하면 바로 한 번 뛴다.</summary>
    public sealed class HeartbeatClock
    {
        private long _next = long.MinValue;
        private bool _active;

        /// <summary>지금 박동 소리를 내야 하면 true.</summary>
        public bool Tick(long now, float level, bool alive)
        {
            if (!alive || level <= 0f) { _active = false; return false; }
            if (!_active) { _active = true; _next = now; }
            if (now < _next) return false;
            _next = now + Tension.HeartPeriodMs(level);
            return true;
        }

        public void Reset() { _active = false; }
    }

    /// <summary>연속 처치. 마지막 처치 후 WindowMs 안에 또 처치하면 이어진다.</summary>
    public sealed class KillStreak
    {
        public const int WindowMs = 7000;
        public int Count { get; private set; }
        public int Best { get; private set; }
        public long LastMs { get; private set; }

        /// <summary>처치를 기록하고 현재 연속 횟수를 돌려준다.</summary>
        public int Register(long now)
        {
            if (Count > 0 && now - LastMs <= WindowMs) ++Count; else Count = 1;
            LastMs = now;
            if (Count > Best) Best = Count;
            return Count;
        }

        /// <summary>now 시점에 이어지고 있는 연속 횟수(시간이 지나 끊겼으면 0).</summary>
        public int Active(long now) { return Count > 0 && now - LastMs <= WindowMs ? Count : 0; }

        /// <summary>남은 시간 비율 1 -> 0.</summary>
        public float Remaining(long now)
        {
            if (Active(now) == 0) return 0f;
            float r = 1f - (now - LastMs) / (float)WindowMs;
            return r < 0f ? 0f : r;
        }

        public void Reset() { Count = 0; }

        public static string Title(int n)
        {
            if (n <= 1) return "";
            if (n == 2) return "더블 킬!";
            if (n == 3) return "트리플 킬!";
            if (n == 4) return "쿼드라 킬!";
            if (n < 10) return n + "연속 처치 - 학살!";
            if (n < 20) return n + "연속 처치 - 폭주!";
            return n + "연속 처치 - 전설!";
        }

        /// <summary>단계 0~3: 클수록 연출이 화려하다.</summary>
        public static int Tier(int n) { return n < 3 ? 0 : (n < 5 ? 1 : (n < 10 ? 2 : 3)); }
    }
}

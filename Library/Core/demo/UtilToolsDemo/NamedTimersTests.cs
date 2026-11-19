// NamedTimersTests.cs : NamedTimers 단위 테스트.
using System;
using System.Collections.Generic;
using Core.Util;

namespace Core.Demo;

internal static class NamedTimersTests
{
    public static void Run(Action<string, bool> check)
    {
        // 한 번짜리: Exist/EndMs/RemainingMs, 실행 뒤 제거
        {
            var t = new NamedTimers<string>(1000, 10);
            int fired = 0;
            t.Set("buff", 500, () => fired++);
            check("NamedTimers: Exist 와 Count", t.Exist("buff") && !t.Exist("x") && t.Count == 1);
            check("NamedTimers: EndMs = 기준 + 지연", t.EndMs("buff") == 1500 && t.RemainingMs("buff") == 500);
            t.Advance(1200);
            check("NamedTimers: 진행하면 남은 시간이 줄어든다", fired == 0 && t.RemainingMs("buff") == 300 && t.RemainingMs("buff", 1450) == 50);
            t.Advance(1499);
            check("NamedTimers: 기한 전에는 실행되지 않는다", fired == 0 && t.Exist("buff"));
            t.Advance(1500);
            check("NamedTimers: 기한에 한 번 실행되고 제거된다", fired == 1 && !t.Exist("buff") && t.Count == 0 && t.RemainingMs("buff") == 0 && t.EndMs("buff") == 0);
            t.Advance(5000);
            check("NamedTimers: 다시 실행되지 않는다", fired == 1);
        }

        // 틱 단위 올림이 EndMs 에 반영된다
        {
            var t = new NamedTimers<string>(0, 10);
            t.Set("a", 25, () => { });
            check("NamedTimers: EndMs 는 틱 올림(25ms → 30ms)", t.EndMs("a") == 30);
        }

        // 같은 이름 교체
        {
            var t = new NamedTimers<string>(0, 10);
            var log = new List<string>();
            t.Set("k", 100, () => log.Add("old"));
            t.Advance(50);
            t.Set("k", 100, () => log.Add("new"));       // 기준은 CurrentMs=50 → 150
            check("NamedTimers: 교체하면 끝 시각이 바뀐다", t.EndMs("k") == 150 && t.Count == 1);
            t.Advance(120);
            check("NamedTimers: 기존 타이머는 실행되지 않는다", log.Count == 0);
            t.Advance(150);
            check("NamedTimers: 새 타이머만 실행된다", log.Count == 1 && log[0] == "new");
        }

        // 취소
        {
            var t = new NamedTimers<string>(0, 10);
            int n = 0;
            t.Set("a", 100, () => n++);
            check("NamedTimers: Cancel 은 있었으면 true", t.Cancel("a") && !t.Cancel("a") && !t.Exist("a"));
            t.Advance(500);
            check("NamedTimers: 취소하면 실행되지 않는다", n == 0);
            t.Set("b", 100, () => n++); t.Set("c", 100, () => n++);
            t.Clear();
            t.Advance(1000);
            check("NamedTimers: Clear", n == 0 && t.Count == 0);
        }

        // 콜백 안에서 같은 이름으로 다시 걸기(한 번짜리), 다른 이름 취소
        {
            var t = new NamedTimers<string>(0, 10);
            int n = 0, o = 0;
            Action? again = null;
            again = () => { n++; if (n < 3) t.Set("chain", 100, again!); };
            t.Set("chain", 100, again);
            t.Set("other", 150, () => o++);
            t.Advance(100);
            check("NamedTimers: 콜백에서 같은 이름 재등록", n == 1 && t.Exist("chain") && t.EndMs("chain") == 200);
            t.Advance(1000);
            check("NamedTimers: 재등록이 이어지고 다른 타이머도 실행", n == 3 && o == 1 && !t.Exist("chain") && !t.Exist("other"));

            var t2 = new NamedTimers<string>(0, 10);
            int ran = 0;      // 같은 틱의 실행 순서는 보장되지 않으므로 서로 취소시켜 정확히 하나만 실행되는지 본다
            t2.Set("first", 100, () => { ran++; t2.Cancel("second"); });
            t2.Set("second", 100, () => { ran++; t2.Cancel("first"); });
            t2.Advance(100);
            check("NamedTimers: 콜백에서 같은 틱의 다른 타이머 취소", ran == 1 && t2.Count == 0);
        }

        // 반복
        {
            var t = new NamedTimers<string>(0, 10);
            int n = 0;
            t.SetRepeating("tick", 100, () => n++);
            check("NamedTimers: 반복 EndMs 는 첫 만료", t.EndMs("tick") == 100);
            t.Advance(100);
            check("NamedTimers: 반복은 실행 후에도 남는다", n == 1 && t.Exist("tick") && t.EndMs("tick") == 200 && t.RemainingMs("tick") == 100);
            for (long ms = 110; ms <= 450; ms += 10) t.Advance(ms);
            check("NamedTimers: 제때 진행하면 간격마다 실행되고 다음 만료 시각이 맞다", n == 4 && t.EndMs("tick") == 500);
            t.Advance(900);     // 한 번에 따라잡으면 TimerWheel 처럼 밀린 횟수는 건너뛴다
            check("NamedTimers: 따라잡을 때 밀린 횟수는 건너뛰고 위상은 유지한다", n == 5 && t.EndMs("tick") == 1000);
            t.Advance(10_050);
            check("NamedTimers: 오래 쉬어도 위상을 유지하고 다음 시각은 미래다", t.EndMs("tick") == 10_100 && t.RemainingMs("tick") > 0);
            t.Cancel("tick");
            int before = n; t.Advance(20_000);
            check("NamedTimers: 반복 취소", n == before && !t.Exist("tick"));

            // 반복 콜백 안에서 자기 자신을 취소/교체
            var t2 = new NamedTimers<string>(0, 10);
            int c = 0, repl = 0;
            t2.SetRepeating("r", 50, () => { c++; if (c == 2) t2.Cancel("r"); });
            for (long ms = 10; ms <= 1000; ms += 10) t2.Advance(ms);
            check("NamedTimers: 반복 콜백에서 자기 취소", c == 2 && !t2.Exist("r"));
            t2.SetRepeating("s", 50, () => { t2.Set("s", 50, () => repl++); });
            for (long ms = 1010; ms <= 2000; ms += 10) t2.Advance(ms);
            check("NamedTimers: 반복 콜백에서 한 번짜리로 교체", repl == 1 && !t2.Exist("s"));
        }

        // 예외 격리: 이름과 함께 보고되고 다른 타이머는 실행
        {
            var errors = new List<(string, string)>();
            var t = new NamedTimers<string>(0, 10, (k, e) => errors.Add((k, e.Message)));
            int ok = 0;
            t.Set("bad", 100, () => throw new InvalidOperationException("boom"));
            t.Set("good", 100, () => ok++);
            t.SetRepeating("badrep", 100, () => throw new InvalidOperationException("rep"));
            t.Advance(250);
            check("NamedTimers: 예외가 이름과 함께 보고된다", errors.Exists(e => e.Item1 == "bad" && e.Item2 == "boom") && errors.Exists(e => e.Item1 == "badrep"));
            check("NamedTimers: 예외가 나도 다른 타이머는 실행, 반복은 유지", ok == 1 && t.Exist("badrep") && !t.Exist("bad"));

            var t2 = new NamedTimers<string>(0, 10, (k, e) => throw new Exception("reporter failed"));
            int after = 0;
            t2.Set("bad", 100, () => throw new Exception("x"));
            t2.Set("good", 100, () => after++);
            t2.Advance(100);
            check("NamedTimers: 보고처가 던져도 나머지 실행", after == 1);
        }

        // 키 비교자 / enum 키
        {
            var t = new NamedTimers<string>(0, 10, comparer: StringComparer.OrdinalIgnoreCase);
            t.Set("Buff", 100, () => { });
            check("NamedTimers: 대소문자 무시 비교자", t.Exist("BUFF") && t.Cancel("buff"));
            var e = new NamedTimers<DayOfWeek>(0, 10);
            e.Set(DayOfWeek.Friday, 100, () => { });
            check("NamedTimers: enum 키", e.Exist(DayOfWeek.Friday) && !e.Exist(DayOfWeek.Monday));
        }

        // 잘못된 인자
        {
            var t = new NamedTimers<string>(0, 10);
            bool a = false, b = false;
            try { t.Set("x", 10, null!); } catch (ArgumentNullException) { a = true; }
            try { t.SetRepeating("x", 0, () => { }); } catch (ArgumentOutOfRangeException) { b = true; }
            check("NamedTimers: 잘못된 인자는 예외", a && b && t.Count == 0);
        }
    }
}

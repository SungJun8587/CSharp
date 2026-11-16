//***************************************************************************
// LogFilter.cs : 로그 창 탭(전체/전투/시스템/채팅)별 걸러내기.
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public enum LogTab { All = 0, Combat = 1, System = 2, Chat = 3 }

    public static class LogFilter
    {
        public static readonly string[] Names = { "전체", "전투", "시스템", "채팅" };

        public static bool Match(LogLine l, LogTab tab)
        {
            switch (tab)
            {
                case LogTab.Combat: return l.Cat == LogCat.Combat;
                case LogTab.System: return l.Cat == LogCat.System;
                case LogTab.Chat: return l.Cat == LogCat.Chat;
                default: return true;
            }
        }

        /// <summary>맨 아래(가장 최근) 줄부터 최대 max 줄을 시간 순서(오래된 것이 위)로 out 에 담는다. scroll = 위로 거슬러 올라간 줄 수.</summary>
        public static void Select(List<LogLine> log, LogTab tab, int max, int scroll, List<LogLine> outLines)
        {
            outLines.Clear();
            int skipped = 0;
            for (int i = log.Count - 1; i >= 0 && outLines.Count < max; --i)
            {
                if (!Match(log[i], tab)) continue;
                if (skipped < scroll) { ++skipped; continue; }
                outLines.Add(log[i]);
            }
            outLines.Reverse();
        }

        public static int Count(List<LogLine> log, LogTab tab)
        {
            int n = 0;
            for (int i = 0; i < log.Count; ++i) if (Match(log[i], tab)) ++n;
            return n;
        }
    }
}

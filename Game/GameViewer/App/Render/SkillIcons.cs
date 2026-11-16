//***************************************************************************
// SkillIcons.cs : Assets/skillicons/skill_<id>.png, basic.png 를 읽어 두는 캐시. 없으면 null (벡터 문양으로 대신 그린다).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace GameViewer.App.Render
{
    public static class SkillIcons
    {
        public static string Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
        private static readonly Dictionary<uint, Bitmap> Cache = new Dictionary<uint, Bitmap>();

        /// <summary>id 0 = 기본 공격.</summary>
        public static Bitmap Get(uint id)
        {
            Bitmap b;
            lock (Cache)
            {
                if (Cache.TryGetValue(id, out b)) return b;
                try
                {
                    string path = Path.Combine(Path.Combine(Dir, "skillicons"), id == 0 ? "basic.png" : "skill_" + id + ".png");
                    if (File.Exists(path)) using (Bitmap raw = new Bitmap(path)) b = new Bitmap(raw);
                }
                catch (Exception) { b = null; }
                Cache[id] = b;
                return b;
            }
        }
    }
}

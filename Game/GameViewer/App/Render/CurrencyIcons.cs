//***************************************************************************
// CurrencyIcons.cs : Assets/currencyicons/currency_<재화 ID>.png 를 읽어 두는 캐시. 없으면 null (코드로 그린 동전/보석으로 대신 그린다).
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace GameViewer.App.Render
{
    public static class CurrencyIcons
    {
        public static string Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
        private static readonly Dictionary<uint, Bitmap> Cache = new Dictionary<uint, Bitmap>();

        public static Bitmap Get(uint currencyId)
        {
            Bitmap b;
            lock (Cache)
            {
                if (Cache.TryGetValue(currencyId, out b)) return b;
                try
                {
                    string path = Path.Combine(Path.Combine(Dir, "currencyicons"), "currency_" + currencyId + ".png");
                    if (File.Exists(path)) using (Bitmap raw = new Bitmap(path)) b = new Bitmap(raw);
                }
                catch (Exception) { b = null; }
                Cache[currencyId] = b;
                return b;
            }
        }
    }
}

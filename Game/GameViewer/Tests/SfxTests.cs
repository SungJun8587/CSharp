using System;
using GameViewer.Core.Audio;
using GameViewer.Core.Data;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    public static class SfxTests
    {
        public static void SynthIsSane()
        {
            foreach (SfxKind k in Enum.GetValues(typeof(SfxKind)))
            {
                float[] a = SfxSynth.Make(k, SfxSynth.DefaultRate);
                float[] b = SfxSynth.Make(k, SfxSynth.DefaultRate);
                T.Check(a.Length > 500 && a.Length < SfxSynth.DefaultRate, k + ": length " + a.Length);
                T.Eq(a.Length, b.Length, k + ": deterministic length");
                bool same = true, inRange = true; float peak = 0;
                for (int i = 0; i < a.Length; ++i)
                {
                    if (a[i] != b[i]) same = false;
                    if (a[i] < -1f || a[i] > 1f || float.IsNaN(a[i])) inRange = false;
                    peak = Math.Max(peak, Math.Abs(a[i]));
                }
                T.Check(same, k + ": same samples every time");
                T.Check(inRange, k + ": samples within -1..1");
                T.Check(peak > 0.1f, k + ": audible (peak " + peak.ToString("0.00") + ")");
                T.Check(Math.Abs(a[a.Length - 1]) < 0.02f, k + ": ends quietly (no click)");
            }
            bool threw = false;
            try { SfxSynth.Make(SfxKind.Hit, 100); } catch (ArgumentOutOfRangeException) { threw = true; }
            T.Check(threw, "rejects a silly sample rate");
        }

        public static void WavHeader()
        {
            float[] s = SfxSynth.Make(SfxKind.Hit, SfxSynth.DefaultRate);
            byte[] w = SfxSynth.ToWav16(s, SfxSynth.DefaultRate);
            T.Eq(44 + s.Length * 2, w.Length, "wav size");
            T.Eq("RIFF", System.Text.Encoding.ASCII.GetString(w, 0, 4), "RIFF");
            T.Eq("WAVE", System.Text.Encoding.ASCII.GetString(w, 8, 4), "WAVE");
            T.Eq("data", System.Text.Encoding.ASCII.GetString(w, 36, 4), "data chunk");
            T.Eq(36 + s.Length * 2, BitConverter.ToInt32(w, 4), "riff length");
            T.Eq(s.Length * 2, BitConverter.ToInt32(w, 40), "data length");
            T.Eq(SfxSynth.DefaultRate, BitConverter.ToInt32(w, 24), "sample rate");
            T.Eq(1, (int)BitConverter.ToInt16(w, 22), "mono");
            T.Eq(16, (int)BitConverter.ToInt16(w, 34), "16 bit");
        }

        public static void Rules()
        {
            WorldModel w = new WorldModel(GameData.Load(Program.Dir("GameData"), 1), 9000UL);
            SfxKind k; float g;
            ViewEffect dmg = new ViewEffect { Kind = EffectKind.Damage, Variant = 2, Value = 5, X = 100, Y = 100 };
            T.Check(SfxRules.TryMap(dmg, w, 0, 0, out k, out g) && k == SfxKind.Hurt && g == 1f, "damage to me is heard from anywhere, full volume");
            ViewEffect dealt = new ViewEffect { Kind = EffectKind.Damage, Variant = 3, Value = 5, X = 2, Y = 0 };
            T.Check(SfxRules.TryMap(dealt, w, 0, 0, out k, out g) && k == SfxKind.Hit, "damage I deal");
            ViewEffect other = new ViewEffect { Kind = EffectKind.Damage, Variant = 0, Value = 5, X = 5, Y = 0 };
            T.Check(SfxRules.TryMap(other, w, 0, 0, out k, out g) && k == SfxKind.HitSoft && g > 0.7f, "nearby hit by others is quiet but clear");
            other.X = 40;
            T.Check(!SfxRules.TryMap(other, w, 0, 0, out k, out g), "far hit by others is silent");
            other.X = 20;
            T.Check(SfxRules.TryMap(other, w, 0, 0, out k, out g) && g < 0.7f && g > 0f, "mid-range hit is attenuated (" + g.ToString("0.00") + ")");
            ViewEffect spark = new ViewEffect { Kind = EffectKind.Spark, X = 1, Y = 1 };
            T.Check(!SfxRules.TryMap(spark, w, 0, 0, out k, out g), "sparks stay silent (the damage event already sounds)");
            ViewEffect banner = new ViewEffect { Kind = EffectKind.Banner, X = 999, Y = 999 };
            T.Check(SfxRules.TryMap(banner, w, 0, 0, out k, out g) && k == SfxKind.Banner && g == 1f, "banner ignores distance");
            ViewEffect ring = new ViewEffect { Kind = EffectKind.Ring, X = 3, Y = 0 };
            T.Check(SfxRules.TryMap(ring, w, 0, 0, out k, out g) && k == SfxKind.Boom, "area skill boom");
        }
    }
}

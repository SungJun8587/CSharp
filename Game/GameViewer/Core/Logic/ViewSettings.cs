//***************************************************************************
// ViewSettings.cs : 사용자가 고르는 표시/소리 옵션(그래픽 품질, 볼륨, 정보 표시). 파일(settings.json)로 저장/복원한다.
// 순수 데이터라 WinForms/Unity 어디서든 쓴다. 파일이 없거나 깨져 있으면 기본값으로 시작한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameViewer.Core.Data;

namespace GameViewer.Core.Logic
{
    public enum GfxQuality { Low = 0, Medium = 1, High = 2 }

    public sealed class ViewSettings
    {
        public GfxQuality Quality = GfxQuality.High;
        public int VolumePercent = 100;         // 효과음 크기 0..100
        public bool SfxOn = true;
        public bool ShowFps = true;             // 정보 패널의 FPS/프레임 시간/수신량 줄
        public bool ShowDamageNumbers = true;
        public bool ShowScreenFx = true;        // 저체력 붉은 테두리/피격 번쩍임 (민감한 사람은 끌 수 있다)
        public bool WeatherOn = true;           // 날씨(비/안개/번개)와 낮밤 색조
        public bool ReduceShake = false;        // 화면 흔들림 약하게
        public bool BgmOn = true;               // 배경음악
        public int BgmVolumePercent = 60;       // 배경음악 크기 0..100 (효과음 크기와 따로)
        public int FeedbackPercent = 100;       // 타격감 세기(히트스톱/피격 번쩍임/흔들림) 0..150
        public string LastServer = "";          // 마지막으로 고른 서버 이름
        public KeyBindings Keys = new KeyBindings();

        /// <summary>품질 단계별로 그리는 것: 낮음은 대기 연출과 비네트를 생략, 중간은 대기 연출만 생략, 높음은 전부.</summary>
        public bool DrawAir { get { return Quality == GfxQuality.High; } }
        public bool DrawVignette { get { return Quality != GfxQuality.Low; } }

        public float Gain { get { return SfxOn ? Math.Max(0, Math.Min(100, VolumePercent)) / 100f : 0f; } }
        public float ShakeScale { get { return (ReduceShake ? 0.35f : 1f) * FeedbackScale; } }
        public float FeedbackScale { get { return Math.Max(0, Math.Min(150, FeedbackPercent)) / 100f; } }
        public float BgmGain { get { return BgmOn ? Math.Max(0, Math.Min(100, BgmVolumePercent)) / 100f : 0f; } }

        public void Normalize()
        {
            if (VolumePercent < 0) VolumePercent = 0;
            if (VolumePercent > 100) VolumePercent = 100;
            if (BgmVolumePercent < 0) BgmVolumePercent = 0;
            if (BgmVolumePercent > 100) BgmVolumePercent = 100;
            if (FeedbackPercent < 0) FeedbackPercent = 0;
            if (FeedbackPercent > 150) FeedbackPercent = 150;
            if (LastServer == null) LastServer = "";
            if ((int)Quality < 0 || (int)Quality > 2) Quality = GfxQuality.High;
        }

        public static string QualityName(GfxQuality q)
        {
            switch (q) { case GfxQuality.Low: return "낮음"; case GfxQuality.Medium: return "중간"; default: return "높음"; }
        }

        public void CycleQuality() { Quality = (GfxQuality)(((int)Quality + 2) % 3); }      // 높음 -> 중간 -> 낮음 -> 높음
        public void StepBgm(int delta)
        {
            BgmVolumePercent += delta; Normalize();
            if (delta > 0 && BgmVolumePercent > 0) BgmOn = true;
        }
        public void StepFeedback(int delta) { FeedbackPercent += delta; Normalize(); }

        public void StepVolume(int delta)
        {
            VolumePercent += delta; Normalize();
            if (delta > 0 && VolumePercent > 0) SfxOn = true;
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"Quality\": " + ((int)Quality).ToString(CultureInfo.InvariantCulture) + ",\r\n");
            sb.Append("  \"VolumePercent\": " + VolumePercent.ToString(CultureInfo.InvariantCulture) + ",\r\n");
            sb.Append("  \"SfxOn\": " + B(SfxOn) + ",\r\n");
            sb.Append("  \"ShowFps\": " + B(ShowFps) + ",\r\n");
            sb.Append("  \"ShowDamageNumbers\": " + B(ShowDamageNumbers) + ",\r\n");
            sb.Append("  \"ShowScreenFx\": " + B(ShowScreenFx) + ",\r\n");
            sb.Append("  \"WeatherOn\": " + B(WeatherOn) + ",\r\n");
            sb.Append("  \"ReduceShake\": " + B(ReduceShake) + ",\r\n");
            sb.Append("  \"BgmOn\": " + B(BgmOn) + ",\r\n");
            sb.Append("  \"BgmVolumePercent\": " + BgmVolumePercent.ToString(CultureInfo.InvariantCulture) + ",\r\n");
            sb.Append("  \"FeedbackPercent\": " + FeedbackPercent.ToString(CultureInfo.InvariantCulture) + ",\r\n");
            sb.Append("  \"LastServer\": \"" + LastServer.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\r\n");
            sb.Append("  \"Keys\": " + Keys.ToJson() + "\r\n");
            sb.Append("}\r\n");
            return sb.ToString();
        }
        private static string B(bool b) { return b ? "true" : "false"; }

        /// <summary>JSON 문자열에서 읽는다. 깨졌으면 false(현재 값 유지). 없는 키는 현재 값 유지.</summary>
        public bool FromJson(string text)
        {
            try
            {
                Dictionary<string, object> o = MiniJson.AsObject(MiniJson.Parse(text));
                if (o == null) return false;
                Quality = (GfxQuality)(int)MiniJson.Num(o, "Quality", (int)Quality);
                VolumePercent = (int)MiniJson.Num(o, "VolumePercent", VolumePercent);
                SfxOn = MiniJson.Bool(o, "SfxOn", SfxOn);
                ShowFps = MiniJson.Bool(o, "ShowFps", ShowFps);
                ShowDamageNumbers = MiniJson.Bool(o, "ShowDamageNumbers", ShowDamageNumbers);
                ShowScreenFx = MiniJson.Bool(o, "ShowScreenFx", ShowScreenFx);
                WeatherOn = MiniJson.Bool(o, "WeatherOn", WeatherOn);
                ReduceShake = MiniJson.Bool(o, "ReduceShake", ReduceShake);
                BgmOn = MiniJson.Bool(o, "BgmOn", BgmOn);
                BgmVolumePercent = (int)MiniJson.Num(o, "BgmVolumePercent", BgmVolumePercent);
                FeedbackPercent = (int)MiniJson.Num(o, "FeedbackPercent", FeedbackPercent);
                LastServer = MiniJson.Str(o, "LastServer", LastServer);
                object ko;
                if (o.TryGetValue("Keys", out ko)) Keys.FromObject(MiniJson.AsObject(ko));
                Normalize();
                return true;
            }
            catch (Exception) { return false; }
        }

        public static ViewSettings Load(string path)
        {
            ViewSettings s = new ViewSettings();
            try { if (File.Exists(path)) s.FromJson(File.ReadAllText(path, Encoding.UTF8)); } catch (Exception) { }
            return s;
        }

        /// <summary>저장한다. 쓰기 위치가 막혀 있어도 게임은 계속되어야 하므로 실패는 false 로만 알린다.</summary>
        public bool Save(string path)
        {
            try { File.WriteAllText(path, ToJson(), new UTF8Encoding(true)); return true; }
            catch (Exception) { return false; }
        }
    }
}

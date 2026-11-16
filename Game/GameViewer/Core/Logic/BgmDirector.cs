//***************************************************************************
// BgmDirector.cs : 지금 어떤 배경음악을 틀지 정하는 규칙. 존 종류가 곡을 정하고, 보스가 시야에 있으면 전투 곡으로 바꾼다.
// 전투가 끝나도 바로 평온한 곡으로 돌아가지 않고 잠시(CalmDelayMs) 기다려, 보스가 시야에서 잠깐 사라질 때마다 곡이 바뀌는 일을 막는다.
// 오디오 장치를 모르는 순수 규칙이라 WinForms/Unity 어디서든 쓴다.
//***************************************************************************
using System;
using GameViewer.Core.World;

namespace GameViewer.Core.Logic
{
    public sealed class BgmDirector
    {
        public const long CalmDelayMs = 6000;

        public bool Playing { get; private set; }
        public MapKind Kind { get; private set; }
        public bool Battle { get; private set; }
        private long _calmSince = -1;

        /// <summary>시야에 살아 있는 보스가 있는가.</summary>
        public static bool BossAlive(WorldModel w)
        {
            foreach (System.Collections.Generic.KeyValuePair<ulong, Entity> kv in w.Entities)
                if (kv.Value.IsBoss && !kv.Value.Dead) return true;
            return false;
        }

        /// <summary>한 틱 진행. 곡(재생 여부/존 종류/전투 여부)이 바뀌었으면 true.</summary>
        public bool Update(long now, bool inZone, MapKind zone, bool danger)
        {
            bool wasPlaying = Playing; MapKind wasKind = Kind; bool wasBattle = Battle;
            if (!inZone)
            {
                Playing = false; _calmSince = -1;
                return wasPlaying;
            }
            if (!Playing || zone != Kind)
            {
                Playing = true; Kind = zone; Battle = danger; _calmSince = -1;
            }
            else if (danger) { Battle = true; _calmSince = -1; }
            else if (Battle)
            {
                if (_calmSince < 0) _calmSince = now;
                if (now - _calmSince >= CalmDelayMs) { Battle = false; _calmSince = -1; }
            }
            return Playing != wasPlaying || Kind != wasKind || Battle != wasBattle;
        }
    }
}

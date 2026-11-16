using System.Collections.Generic;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;

namespace GameViewer.Tests
{
    public static class PingTests
    {
        public static void Meter()
        {
            PingMeter m = new PingMeter();
            T.Check(m.Due(0) && !m.HasSample, "first ping is due immediately");
            uint s1 = m.Begin(1000);
            T.Check(!m.Due(1000 + PingMeter.IntervalMs - 1) && m.Due(1000 + PingMeter.IntervalMs), "next ping after interval");
            T.Check(m.OnPong(s1, 1042) && m.LastMs == 42 && m.AvgMs == 42 && m.MaxMs == 42, "rtt measured by the client clock");
            T.Check(!m.OnPong(s1, 1100), "duplicate pong ignored");
            T.Check(!m.OnPong(999, 1100), "unknown seq ignored");
            // 평균/최대는 최근 Window 개만
            long t = 5000;
            for (int i = 0; i < PingMeter.Window + 3; ++i) { uint s = m.Begin(t); m.OnPong(s, t + 10 + i * 10); t += 3000; }
            T.Check(m.MaxMs == 10 + (PingMeter.Window + 2) * 10, "max over window");
            T.Check(m.AvgMs > 42, "old samples dropped from the average");
            // 손실
            PingMeter l = new PingMeter();
            l.Begin(0);
            l.Expire(PingMeter.TimeoutMs);
            T.Check(l.Lost == 0, "not lost before timeout");
            l.Expire(PingMeter.TimeoutMs + 1);
            T.Check(l.Lost == 1, "lost after timeout");
            l.Expire(PingMeter.TimeoutMs + 1000);
            T.Check(l.Lost == 1, "counted once");
            // 품질 단계
            PingMeter q = new PingMeter();
            uint a = q.Begin(0); q.OnPong(a, 30); T.Check(q.Quality == 0, "30ms good");
            PingMeter q2 = new PingMeter(); a = q2.Begin(0); q2.OnPong(a, 100); T.Check(q2.Quality == 1, "100ms fair");
            PingMeter q3 = new PingMeter(); a = q3.Begin(0); q3.OnPong(a, 400); T.Check(q3.Quality == 2, "400ms poor");
            q3.Reset(); T.Check(!q3.HasSample && q3.Lost == 0 && q3.Due(0), "reset");
        }

        public static void Packets()
        {
            byte[] b = Build.ZcPingReq(7, 123456789012UL);
            T.Eq(Build.ZcPingReqSize, b.Length, "ping req size");
            T.Eq((int)ZonePacketType.ZcPingReq, b[2] | (b[3] << 8), "ping req type");
            ZcPongNotify p = new ZcPongNotify { Seq = 7, ClientMs = 123456789012UL, ServerTimeMs = 99 };
            ZcPongNotify q = ZcPongNotify.Parse(p.ToBytes());
            T.Check(q.Seq == 7 && q.ClientMs == 123456789012UL && q.ServerTimeMs == 99, "pong round trip");
        }

        /// <summary>세션이 존 안에서 주기적으로 핑을 보내고, 응답을 받으면 RTT 가 기록된다.</summary>
        public static void Session()
        {
            ViewerConfig c = new ViewerConfig(); c.DataDir = Program.Dir("GameData"); c.ZoneId = 1; c.HeroId = 1; c.CharId = 5;
            ViewerSession s = new ViewerSession(c, GameData.Load(c.DataDir, c.ZoneId));
            List<byte[]> sent = new List<byte[]>();
            s.SendOverride = delegate (byte[] p) { sent.Add(p); return true; };
            long now = 100000;
            s.Update(now);
            T.Eq(0, sent.Count, "no ping before entering the zone");
            s.EnterOffline(100, 100, "test", now);
            s.Update(now);
            int pings = 0; byte[] last = null;
            foreach (byte[] p in sent) if ((p[2] | (p[3] << 8)) == (int)ZonePacketType.ZcPingReq) { ++pings; last = p; }
            T.Eq(1, pings, "ping sent on entering the zone");
            s.Update(now + 500);
            pings = 0; foreach (byte[] p in sent) if ((p[2] | (p[3] << 8)) == (int)ZonePacketType.ZcPingReq) ++pings;
            T.Eq(1, pings, "no second ping within the interval");
            // 서버처럼 응답한다
            uint seq = new PacketReader(last, 4).U32();
            ulong cms = new PacketReader(last, 8).U64();
            byte[] pong = new ZcPongNotify { Seq = seq, ClientMs = cms, ServerTimeMs = 1 }.ToBytes();
            Frame f = new Frame(); f.Type = (ushort)ZonePacketType.ZcPongNotify; f.Data = pong;
            s.InjectFrame(f, now + 57);
            T.Check(s.World.Ping.HasSample && s.World.Ping.LastMs == 57, "rtt recorded from pong");
            s.Update(now + PingMeter.IntervalMs + 10);
            pings = 0; foreach (byte[] p in sent) if ((p[2] | (p[3] << 8)) == (int)ZonePacketType.ZcPingReq) ++pings;
            T.Eq(2, pings, "next ping after the interval");
        }
    }
}

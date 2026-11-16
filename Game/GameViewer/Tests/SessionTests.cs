using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using GameViewer.Core;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Protocol;
using Pk = GameViewer.Core.Protocol.ServerPackets;
using GameViewer.Core.World;

namespace GameViewer.Tests
{
    /// <summary>테스트용 가짜 Gateway. 한 연결을 받아 스크립트를 실행하고, 클라이언트가 보낸 패킷을 모아둔다.</summary>
    public sealed class FakeGateway : IDisposable
    {
        readonly TcpListener _l;
        readonly List<Frame> _got = new List<Frame>();
        readonly object _lock = new object();
        NetworkStream _st;
        readonly Thread _accept;
        readonly ManualResetEvent _connected = new ManualResetEvent(false);
        public int Port { get { return ((IPEndPoint)_l.LocalEndpoint).Port; } }

        public FakeGateway()
        {
            _l = new TcpListener(IPAddress.Loopback, 0);
            _l.Start();
            _accept = new Thread(AcceptLoop); _accept.IsBackground = true; _accept.Start();
        }

        void AcceptLoop()
        {
            try
            {
                TcpClient c = _l.AcceptTcpClient();
                c.NoDelay = true;
                _st = c.GetStream();
                _connected.Set();
                byte[] buf = new byte[4096];
                PacketFramer fr = new PacketFramer(4096);
                List<Frame> tmp = new List<Frame>();
                while (true)
                {
                    int n = _st.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    tmp.Clear(); fr.Push(buf, 0, n, tmp);
                    lock (_lock) { _got.AddRange(tmp); }
                }
            }
            catch { }
        }

        public bool WaitConnected(int ms) { return _connected.WaitOne(ms); }

        public void Send(byte[] pkt, bool byteByByte)
        {
            if (byteByByte) { for (int i = 0; i < pkt.Length; ++i) { _st.Write(pkt, i, 1); _st.Flush(); } }
            else _st.Write(pkt, 0, pkt.Length);
        }
        public void Send(byte[] pkt) { Send(pkt, false); }

        /// <summary>조건에 맞는 패킷이 올 때까지 기다린다(그 앞의 패킷은 버린다). 없으면 null.</summary>
        public Frame? Wait(ushort type, Func<Frame, bool> pred, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                lock (_lock)
                {
                    for (int i = 0; i < _got.Count; ++i)
                    {
                        if (_got[i].Type == type && (pred == null || pred(_got[i])))
                        {
                            Frame f = _got[i];
                            _got.RemoveRange(0, i + 1);
                            return f;
                        }
                    }
                }
                Thread.Sleep(5);
            }
            return null;
        }

        public int Count(ushort type)
        {
            lock (_lock) { int n = 0; foreach (Frame f in _got) if (f.Type == type) ++n; return n; }
        }

        public void CloseClient() { try { _st.Close(); } catch { } }
        public void Dispose() { try { _l.Stop(); } catch { } try { if (_st != null) _st.Close(); } catch { } }
    }

    public static class SessionTests
    {
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static long Now { get { return Clock.ElapsedMilliseconds + 100000; } }

        static ViewerConfig Cfg(int port, uint hero)
        {
            ViewerConfig c = new ViewerConfig();
            c.Port = port; c.CharId = 777; c.ZoneId = 1; c.HeroId = hero; c.DataDir = Program.Dir("GameData");
            c.ConnectTimeoutMs = 1500; c.MoveIntervalMs = 120;
            return c;
        }

        static ViewerSession NewSession(ViewerConfig c)
        {
            GameData d = GameData.Load(c.DataDir, c.ZoneId);
            return new ViewerSession(c, d);
        }

        static bool Pump(ViewerSession s, AutoPilot pilot, Func<bool> cond, int timeoutMs)
        {
            long end = Now + timeoutMs;
            while (Now < end)
            {
                s.Update(Now);
                if (pilot != null) pilot.Update(Now);
                if (cond()) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        // 세션을 계속 돌리면서(Update 가 있어야 로그인 패킷이 나간다) 서버가 받을 패킷을 기다린다.
        static Frame? Expect(FakeGateway gw, ViewerSession s, ushort type, int timeoutMs)
        {
            long end = Now + timeoutMs;
            while (Now < end)
            {
                s.Update(Now);
                Frame? f = gw.Wait(type, null, 10);
                if (f.HasValue) return f;
            }
            return null;
        }

        // 로그인 -> 입장 -> 자기 자신 + 몬스터 스폰까지 진행한다.
        static ViewerSession Enter(FakeGateway gw, uint hero, bool fragmented)
        {
            ViewerSession s = NewSession(Cfg(gw.Port, hero));
            s.Start(Now);
            T.Check(gw.WaitConnected(2000), "client connected");
            Frame? login = Expect(gw, s, (ushort)GatewayPacketType.GwLoginReq, 2000);
            T.Check(login.HasValue, "server got login req");
            if (login.HasValue)
            {
                T.Eq(777UL, new PacketReader(login.Value.Data, 4).U64(), "login charId");
                gw.Send(Pk.LoginRes(true, GatewayResult.Ok, 55), fragmented);
            }
            Frame? enter = Expect(gw, s, (ushort)GatewayPacketType.GwEnterZoneReq, 2000);
            T.Check(enter.HasValue, "server got enter req");
            if (enter.HasValue)
            {
                PacketReader r = new PacketReader(enter.Value.Data, 4);
                T.Eq(1u, r.U32(), "enter zone"); T.Eq(hero, r.U32(), "enter hero");
                gw.Send(Pk.EnterRes(true, GatewayResult.Ok, 1, 20, 20));
                gw.Send(Pk.Spawn(777, EntityType.Player, 20, 20, 20, 20, 6, false, 150, 150, false, 1));
                gw.Send(Pk.Spawn(0x8000000000000001UL, EntityType.Monster, 24, 20, 24, 20, 3, false, 120, 120, false, 1));
            }
            Pump(s, null, delegate { return s.State == SessionState.InZone && s.World.Entities.Count == 2; }, 3000);
            return s;
        }

        public static void EndToEnd()
        {
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = Enter(gw, 1, true);
                T.Eq(SessionState.InZone, s.State, "in zone");
                T.Eq(2, s.World.Entities.Count, "self + monster");
                T.Eq(55UL, s.ClientSessionId, "client session id");
                T.Eq(150, s.World.Self.Hp, "self hp from spawn");
                T.Eq(3, s.MySkills.Count, "hero 1 has 3 skills");

                // 이동 요청: 현재 위치(20,20) -> 목적지(40,20)
                long t0 = Now;
                T.Check(s.MoveTo(40, 20, t0, false), "move sent");
                T.Check(!s.MoveTo(41, 20, t0 + 10, false), "move throttled");
                Frame? mv = gw.Wait((ushort)ZonePacketType.ZcMoveReq, null, 2000);
                T.Check(mv.HasValue, "server got move");
                if (mv.HasValue)
                {
                    PacketReader r = new PacketReader(mv.Value.Data, 4);
                    T.Near(20, r.F32(), 1e-3, "move posX"); T.Near(20, r.F32(), 1e-3, "move posY");
                    T.Near(40, r.F32(), 1e-3, "move destX"); T.Near(20, r.F32(), 1e-3, "move destY");
                }
                T.Check(s.MoveTo(5000, -50, t0 + 500, false), "out of range dest is clamped, still sent");
                Frame? mv2 = gw.Wait((ushort)ZonePacketType.ZcMoveReq, null, 2000);
                if (mv2.HasValue)
                {
                    PacketReader r = new PacketReader(mv2.Value.Data, 4);
                    r.F32(); r.F32();
                    T.Near(1000, r.F32(), 1e-3, "dest clamped to world max X"); T.Near(0, r.F32(), 1e-3, "dest clamped to world min Y");
                }

                // 공격
                T.Check(s.Attack(0x8000000000000001UL, Now), "attack sent");
                Frame? at = gw.Wait((ushort)ZonePacketType.ZcAttackReq, null, 2000);
                T.Check(at.HasValue && new PacketReader(at.Value.Data, 4).U64() == 0x8000000000000001UL, "server got attack with target");

                // 스킬 + 쿨다운
                long t1 = Now;
                T.Check(s.UseSkill(1, 0x8000000000000001UL, 0, 0, t1), "skill sent");
                Frame? sk = gw.Wait((ushort)ZonePacketType.ZcSkillReq, null, 2000);
                T.Check(sk.HasValue && new PacketReader(sk.Value.Data, 4).U32() == 1u, "server got skill 1");
                gw.Send(Pk.Skill(777, 1, 0x8000000000000001UL, 24, 20, 1));
                Pump(s, null, delegate { return s.SkillCooldownLeftMs(1, Now) > 0; }, 2000);
                T.Check(s.SkillCooldownLeftMs(1, Now) > 3000, "cooldown ~4000ms after own skill notify");
                int skillsBefore = gw.Count((ushort)ZonePacketType.ZcSkillReq);
                T.Check(!s.UseSkill(1, 0x8000000000000001UL, 0, 0, Now + 200), "same skill blocked by local cooldown");
                T.Check(s.UseSkill(2, 0, 24, 20, Now + 300), "other skill allowed");
                Thread.Sleep(50);
                T.Eq(skillsBefore + 1, gw.Count((ushort)ZonePacketType.ZcSkillReq), "only one extra skill packet on the wire");

                // 보낸 스킬 요청의 답이 오기 전에는 다른 스킬도 보내지 않고(자동 추적도 멈춘다), 답이 오면 풀린다
                T.Check(s.SkillPending(Now + 310), "waiting for the server's answer to the skill request");
                T.Check(!s.UseSkill(7, 0, 24, 20, Now + 450), "no second skill while the first is unanswered");
                gw.Send(Pk.Skill(777, 2, 0, 24, 20, 0));
                Pump(s, null, delegate { return !s.SkillPending(Now + 460); }, 2000);
                T.Check(!s.SkillPending(Now + 460), "answer releases the wait");
                T.Check(s.UseSkill(7, 0, 24, 20, Now + 470), "next skill allowed after the answer");

                // 서버 이벤트가 월드에 반영된다 (분할 전송 포함)
                gw.Send(Pk.Damage(0x8000000000000001UL, 777, 20, 130, 150), true);
                Pump(s, null, delegate { return s.World.Self.Hp == 130; }, 2000);
                T.Eq(130, s.World.Self.Hp, "damage applied through the socket");

                // 사망하면 조작 불가
                gw.Send(Pk.Death(777, 0x8000000000000001UL));
                Pump(s, null, delegate { return s.World.Self.Dead; }, 2000);
                T.Check(!s.CanAct, "cannot act while dead");
                T.Check(!s.MoveTo(1, 1, Now + 1000, true), "move refused while dead");
                gw.Send(Pk.Respawn(777, 20, 20, 150, 150, 1000));
                Pump(s, null, delegate { return !s.World.Self.Dead; }, 2000);
                T.Check(s.CanAct, "can act after respawn");

                // 서버가 연결을 끊는다
                gw.CloseClient();
                Pump(s, null, delegate { return s.State == SessionState.Disconnected; }, 3000);
                T.Eq(SessionState.Disconnected, s.State, "disconnect detected");
                s.Dispose();
            }
        }

        public static void Failures()
        {
            // 로그인 실패
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = NewSession(Cfg(gw.Port, 1));
                s.Start(Now);
                Expect(gw, s, (ushort)GatewayPacketType.GwLoginReq, 2000);
                gw.Send(Pk.LoginRes(false, GatewayResult.AlreadyLoggedIn, 0));
                Pump(s, null, delegate { return s.State == SessionState.Failed; }, 2000);
                T.Eq(SessionState.Failed, s.State, "login failure");
                T.Check(s.StatusText.Contains("AlreadyLoggedIn"), "reason shown: " + s.StatusText);
                s.Dispose();
            }
            // 입장 실패
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = NewSession(Cfg(gw.Port, 9));
                s.Start(Now);
                Expect(gw, s, (ushort)GatewayPacketType.GwLoginReq, 2000);
                gw.Send(Pk.LoginRes(true, GatewayResult.Ok, 1));
                Expect(gw, s, (ushort)GatewayPacketType.GwEnterZoneReq, 2000);
                gw.Send(Pk.EnterRes(false, GatewayResult.InvalidHero, 1, 0, 0));
                Pump(s, null, delegate { return s.State == SessionState.Failed; }, 2000);
                T.Check(s.State == SessionState.Failed && s.StatusText.Contains("InvalidHero"), "enter failure: " + s.StatusText);
                s.Dispose();
            }
            // 존 소실
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = Enter(gw, 1, false);
                gw.Send(Pk.ZoneLost(1));
                Pump(s, null, delegate { return s.State == SessionState.Failed; }, 2000);
                T.Eq(SessionState.Failed, s.State, "zone lost -> failed");
                s.Dispose();
            }
            // 서버가 없음
            int dead;
            { TcpListener l = new TcpListener(IPAddress.Loopback, 0); l.Start(); dead = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); }
            {
                ViewerSession s = NewSession(Cfg(dead, 1));
                s.Start(Now);
                Pump(s, null, delegate { return s.State == SessionState.Failed; }, 4000);
                T.Eq(SessionState.Failed, s.State, "connection refused -> failed");
                T.Check(s.StatusText.Contains("접속 실패"), "message: " + s.StatusText);
                s.Dispose();
            }
            // 프로토콜 위반(잘못된 크기)
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = Enter(gw, 1, false);
                gw.Send(new byte[] { 2, 0, 0x10, 0x27 });
                Pump(s, null, delegate { return s.State == SessionState.Disconnected; }, 3000);
                T.Eq(SessionState.Disconnected, s.State, "invalid size -> disconnected");
                T.Check(s.StatusText.Contains("protocol"), "protocol error shown: " + s.StatusText);
                s.Dispose();
            }
        }

        public static void AutoPilotChasesAndAttacks()
        {
            using (FakeGateway gw = new FakeGateway())
            {
                ViewerSession s = Enter(gw, 1, false);
                T.Eq(SessionState.InZone, s.State, "in zone");
                AutoPilot pilot = new AutoPilot(s);

                // 1) 멀리 있는 몬스터를 향해 이동 요청 (몬스터 (24,20), 영웅 사거리 2.5 -> 정지 거리 ~2.1)
                //    먼저 몬스터를 멀리 옮긴다.
                gw.Send(Pk.Stop(0x8000000000000001UL, 40, 20, true));
                Pump(s, null, delegate { float mx, my; s.World.Find(0x8000000000000001UL).PosAt(Now, out mx, out my); return mx > 39f; }, 2000);
                pilot.SetAuto(true);
                Pump(s, pilot, delegate { return gw.Count((ushort)ZonePacketType.ZcMoveReq) > 0; }, 3000);
                Frame? mv = gw.Wait((ushort)ZonePacketType.ZcMoveReq, null, 1000);
                T.Check(mv.HasValue, "pilot sends move toward the monster");
                if (mv.HasValue)
                {
                    PacketReader r = new PacketReader(mv.Value.Data, 4);
                    r.F32(); r.F32();
                    float dx = r.F32();
                    T.Check(dx > 36f && dx < 40f, "dest short of the monster: " + dx);
                }

                // 2) 서버가 영웅을 몬스터 근처에 세운다 -> 스킬1, 스킬2(범위), 기본 공격 순서
                gw.Send(Pk.Stop(777, 38, 20, true));
                Frame? s1 = null;
                Pump(s, pilot, delegate { return gw.Count((ushort)ZonePacketType.ZcSkillReq) > 0; }, 3000);
                s1 = gw.Wait((ushort)ZonePacketType.ZcSkillReq, null, 1000);
                T.Check(s1.HasValue && new PacketReader(s1.Value.Data, 4).U32() == 1u, "first skill is skill 1 (single, in range)");
                gw.Send(Pk.Skill(777, 1, 0x8000000000000001UL, 40, 20, 1));
                Pump(s, pilot, delegate { return gw.Count((ushort)ZonePacketType.ZcSkillReq) > 0; }, 3000);
                Frame? s2 = gw.Wait((ushort)ZonePacketType.ZcSkillReq, null, 1000);
                T.Check(s2.HasValue && new PacketReader(s2.Value.Data, 4).U32() == 2u, "next skill is area skill 2");
                if (s2.HasValue)
                {
                    PacketReader r = new PacketReader(s2.Value.Data, 4);
                    r.U32(); r.U64();
                    T.Near(40, r.F32(), 1e-3, "area skill centered on monster x");
                }
                gw.Send(Pk.Skill(777, 2, 0, 40, 20, 1));
                // 자기 중심 버프(스킬 7)는 교전 직전에 한 번 건다: 다른 스킬 요청 다음에 온다
                int before = gw.Count((ushort)ZonePacketType.ZcSkillReq);
                Pump(s, pilot, delegate { return gw.Count((ushort)ZonePacketType.ZcSkillReq) > before || gw.Count((ushort)ZonePacketType.ZcAttackReq) > 0; }, 3000);
                if (gw.Count((ushort)ZonePacketType.ZcSkillReq) > before) gw.Send(Pk.Skill(777, 7, 0, 38, 20, 1));
                Pump(s, pilot, delegate { return gw.Count((ushort)ZonePacketType.ZcAttackReq) > 0; }, 3000);
                T.Check(gw.Count((ushort)ZonePacketType.ZcAttackReq) > 0, "basic attack when skills are cooling down");

                // 3) 몬스터 사망 -> 남은 몬스터가 없으니 스폰 지점(300,300)으로 탐색 이동
                gw.Send(Pk.Death(0x8000000000000001UL, 777));
                Frame? roam = null;
                long end = Now + 4000;
                while (Now < end && !roam.HasValue)
                {
                    s.Update(Now); pilot.Update(Now);
                    roam = gw.Wait((ushort)ZonePacketType.ZcMoveReq, delegate (Frame f)
                    {
                        PacketReader r = new PacketReader(f.Data, 4); r.F32(); r.F32(); return r.F32() > 200f;
                    }, 20);
                }
                T.Check(roam.HasValue, "pilot roams toward a spawn area after the kill");
                T.Check(pilot.Note.Length > 0, "pilot note: " + pilot.Note);

                // 4) 끄면 더 이상 보내지 않는다
                pilot.SetAuto(false);
                Thread.Sleep(50);
                int moves = gw.Count((ushort)ZonePacketType.ZcMoveReq);
                Pump(s, pilot, delegate { return false; }, 600);
                T.Eq(moves, gw.Count((ushort)ZonePacketType.ZcMoveReq), "no packets while off");
                s.Dispose();
            }
        }
    }
}

//***************************************************************************
// ViewerSession.cs : 접속 흐름(로그인 -> 존 입장) + 월드 모델 + 명령(이동/공격/스킬)을 묶은 뷰어의 중심 객체.
// UI/Unity 는 Start() 후 매 프레임 Update(now)를 부르고, WorldModel 을 읽어 그리며, MoveTo/Attack/UseSkill 로 조작한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Threading;
using GameViewer.Core.Data;
using GameViewer.Core.Logic;
using GameViewer.Core.Net;
using GameViewer.Core.Protocol;
using GameViewer.Core.World;

namespace GameViewer.Core
{
    public enum SessionState { Idle, Connecting, LoggingIn, Entering, InZone, Failed, Disconnected }

    public sealed class ViewerSession : IDisposable
    {
        public readonly ViewerConfig Config;
        public readonly GameData Data;
        public readonly WorldModel World;
        public GatewayClient Net { get; private set; }

        /// <summary>true 면 연결이 끊기거나 접속에 실패했을 때 백오프로 자동 재접속한다(로그인/입장이 거부된 경우는 제외). 오프라인 데모에서는 쓰지 않는다.</summary>
        public bool AutoReconnect;
        public readonly ReconnectPolicy Reconnect = new ReconnectPolicy();
        public long ConnectStartedMs;           // 현재 접속 시도를 시작한 시각(진행 표시용)
        public long InZoneSinceMs = -1;         // 존에 들어온 시각(입장 연출용, 아직이면 -1)
        public int ReconnectCount;              // 이번 실행에서 자동 재접속에 성공한 횟수
        private bool _wasInZone;                // 한 번이라도 입장해서 월드가 있다(재접속이면 화면을 유지한 채 다시 붙는다)

        public SessionState State { get; private set; }
        public string StatusText { get; private set; }
        public ulong ClientSessionId { get; private set; }
        public uint ZoneId { get { return Config.ZoneId; } }

        /// <summary>설정하면 패킷을 소켓 대신 이 함수로 보낸다(오프라인 데모용). 반환값 = 보냈는지.</summary>
        public Func<byte[], bool> SendOverride;

        private readonly Dictionary<uint, long> _skillReadyAt = new Dictionary<uint, long>();
        private long _lastMoveSentMs = -100000;
        private long _lastAttackSentMs = -100000;
        private long _lastSkillSentMs = -100000;
        private long _skillPendingUntil;            // 보낸 스킬 요청의 답을 기다리는 동안(이 시각까지) 다른 요청을 보내지 않는다
        private const long SkillResponseWaitMs = 500;
        private volatile int _connectState;        // 0 진행 중/미시작, 1 성공, 2 실패
        private string _connectError;

        public ViewerSession(ViewerConfig config, GameData data)
        {
            Config = config; Data = data;
            Net = new GatewayClient();
            World = new WorldModel(data, config.CharId);
            World.SelfSkillUsed += OnSelfSkillUsed;
            World.SelfSkillResponded += delegate (long now) { _skillPendingUntil = 0; };
            State = SessionState.Idle; StatusText = "대기";
        }

        /// <summary>영웅 ID (서버가 스폰 알림으로 알려준 값이 있으면 그것, 없으면 설정값 해석).</summary>
        public uint HeroId
        {
            get
            {
                if (World.Self != null && World.Self.IsPlayer && World.Self.TemplateId != 0) return World.Self.TemplateId;
                return Data.ResolveHeroId(Config.HeroId);
            }
        }

        /// <summary>스킬바 순서가 적용된 내 스킬 목록(칸 번호 = 목록 위치).</summary>
        public List<uint> MySkills { get { return BarOrder.Apply(HeroId, Data.SkillsOf(HeroId)); } }
        public readonly GameViewer.Core.Logic.SkillBarOrder BarOrder = new GameViewer.Core.Logic.SkillBarOrder();
        public bool SwapSkillSlots(int i, int j) { return BarOrder.Swap(HeroId, Data.SkillsOf(HeroId), i, j); }

        // ------------------------------------------------------------------ 접속

        /// <summary>비동기로 접속을 시작한다. 진행은 Update() 가 이어간다.</summary>
        public void Start(long now)
        {
            State = SessionState.Connecting;
            ConnectStartedMs = now;
            StatusText = Config.Host + ":" + Config.Port + " 접속 중...";
            _connectState = 0;
            GatewayClient net = Net;
            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = net.Connect(Config.Host, Config.Port, Config.ConnectTimeoutMs, out err);
                if (net != Net) { if (ok) net.Close(); return; }        // 그 사이 새 연결로 바뀌었다
                _connectError = err;
                _connectState = ok ? 1 : 2;
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>매 프레임 호출: 수신 패킷 처리 + 월드 정리.</summary>
        public void Update(long now)
        {
            if (State == SessionState.Connecting)
            {
                if (_connectState == 1)
                {
                    State = SessionState.LoggingIn; StatusText = "로그인 중...";
                    Net.Send(Build.GwLoginReq(Config.CharId));
                }
                else if (_connectState == 2)
                {
                    State = SessionState.Failed; StatusText = "접속 실패: " + _connectError;
                    World.AddLog(now, 2, StatusText);
                    ScheduleReconnect(now);
                }
            }

            // 끊김 여부는 큐를 비우기 전에 읽는다: 비운 뒤에 읽으면 그 사이 도착한 마지막 패킷이 큐에 남은 채 끊김으로 처리돼 버려진다
            bool alive = Net.Connected;
            Frame f;
            while (Net.TryDequeue(out f))
                Dispatch(f, now);

            if (SendOverride == null && !alive && (State == SessionState.LoggingIn || State == SessionState.Entering || State == SessionState.InZone))
            {
                State = SessionState.Disconnected;
                StatusText = "연결 끊김: " + (Net.DisconnectReason ?? "unknown");
                World.AddLog(now, 2, StatusText);
                ScheduleReconnect(now);
            }

            if (State == SessionState.InZone)
            {
                World.Ping.Expire(now);
                if (World.Ping.Due(now)) Send(Build.ZcPingReq(World.Ping.Begin(now), (ulong)now));
            }

            if (Reconnect.TryTake(now)) BeginReconnect(now);
            World.Update(now);
        }

        // ------------------------------------------------------------------ 자동 재접속

        private void ScheduleReconnect(long now)
        {
            if (!AutoReconnect || SendOverride != null) return;
            if (Reconnect.OnFailure(now))
                World.AddLog(now, 1, "재접속 " + Reconnect.Attempt + "/" + Reconnect.MaxAttempts + " 예약 (" + (Reconnect.RemainingMs(now) / 1000) + "초 후)");
            else
                World.AddLog(now, 2, "자동 재접속을 포기했습니다 - F5 로 다시 시도하세요");
        }

        /// <summary>새 소켓으로 처음부터 다시 접속한다. (이전 수신 스레드가 새 연결 상태를 덮어쓰지 않도록 클라이언트 객체를 바꾼다)</summary>
        private void BeginReconnect(long now)
        {
            GatewayClient old = Net;
            Net = new GatewayClient();
            old.Close();
            ClientSessionId = 0;
            Start(now);
            World.AddLog(now, 0, "재접속 시도 " + Reconnect.Attempt + "/" + Reconnect.MaxAttempts);
        }

        /// <summary>F5: 사용자가 직접 재접속한다 (횟수를 처음부터 다시 센다).</summary>
        public void ReconnectNow(long now)
        {
            if (SendOverride != null) return;
            Reconnect.Manual();
            BeginReconnect(now);
        }

        /// <summary>재접속 중이라 월드(마지막 화면)를 유지하고 있는가.</summary>
        public bool Reconnecting { get { return _wasInZone && State != SessionState.InZone; } }

        /// <summary>접속 진행 단계: 0 접속 1 로그인 2 입장 3 완료 (표시용).</summary>
        public int ConnectStep
        {
            get
            {
                switch (State)
                {
                    case SessionState.Connecting: return 0;
                    case SessionState.LoggingIn: return 1;
                    case SessionState.Entering: return 2;
                    case SessionState.InZone: return 3;
                    default: return -1;
                }
            }
        }

        private void Dispatch(Frame f, long now)
        {
            try
            {
                if (f.Type >= ProtocolConst.ClientRelayTypeMin)
                {
                    World.Apply(f, now);
                    return;
                }

                switch ((GatewayPacketType)f.Type)
                {
                    case GatewayPacketType.GwLoginRes:
                    {
                        GwLoginRes r = GwLoginRes.Parse(f.Data);
                        if (!r.Success) { Fail(now, "로그인 실패: " + r.Reason); break; }
                        ClientSessionId = r.ClientSessionId;
                        State = SessionState.Entering; StatusText = "존 " + Config.ZoneId + " 입장 중...";
                        Net.Send(Build.GwEnterZoneReq(Config.ZoneId, Config.HeroId));
                        break;
                    }
                    case GatewayPacketType.GwEnterZoneRes:
                    {
                        GwEnterZoneRes r = GwEnterZoneRes.Parse(f.Data);
                        if (!r.Success) { Fail(now, "입장 실패: " + r.Reason); break; }
                        bool again = _wasInZone;
                        World.Clear();
                        World.InitSelf(r.SpawnX, r.SpawnY, Data.ResolveHeroId(Config.HeroId), now);
                        World.Ping.Reset();
                        State = SessionState.InZone; StatusText = "존 " + r.ZoneId + " 입장 완료";
                        _wasInZone = true; InZoneSinceMs = now;
                        if (again) { ++ReconnectCount; World.AddLog(now, 0, "재접속 성공"); }
                        Reconnect.Reset();
                        break;
                    }
                    case GatewayPacketType.GwZoneLostNotify:
                        Fail(now, "존 연결 끊김 (zone " + GwZoneLostNotify.Parse(f.Data).ZoneId + ")");
                        ScheduleReconnect(now);
                        break;
                    case GatewayPacketType.GwLeaveZoneRes:
                        World.AddLog(now, 0, "존 퇴장 응답");
                        break;
                    default:
                        World.UnknownPacketCount++;
                        break;
                }
            }
            catch (PacketFormatException)
            {
                World.MalformedPacketCount++;
            }
            catch (Exception e)
            {
                // 파싱은 됐지만 처리 중 예기치 못한 오류가 난 패킷 하나 때문에 타이머 틱(=창)이 죽지 않게 버리고, 처음 한 번만 로그를 남긴다
                World.MalformedPacketCount++;
                if (!_dispatchFailLogged) { _dispatchFailLogged = true; World.AddLog(now, 2, "패킷 처리 오류(type " + f.Type + "): " + e.GetType().Name + " " + e.Message); }
            }
        }
        private bool _dispatchFailLogged;

        private void Fail(long now, string text)
        {
            State = SessionState.Failed; StatusText = text;
            World.AddLog(now, 2, text);
        }

        // ------------------------------------------------------------------ 조작

        public bool CanAct { get { return State == SessionState.InZone && World.Self != null && !World.Self.Dead; } }

        /// <summary>내 현재(보간) 위치.</summary>
        public bool SelfPos(long now, out float x, out float y)
        {
            x = 0; y = 0;
            if (World.Self == null) return false;
            World.Self.PosAt(now, out x, out y);
            return true;
        }

        /// <summary>목적지로 이동 요청. 너무 잦은 호출은 무시(false).</summary>
        public bool MoveTo(float destX, float destY, long now, bool force)
        {
            if (!CanAct) return false;
            if (!force && now - _lastMoveSentMs < Config.MoveIntervalMs) return false;
            destX = Clamp(destX, Data.WorldMinX, Data.WorldMaxX);
            destY = Clamp(destY, Data.WorldMinY, Data.WorldMaxY);
            float px, py; World.Self.PosAt(now, out px, out py);
            if (!Send(Build.ZcMoveReq(px, py, destX, destY))) return false;
            _lastMoveSentMs = now;
            return true;
        }

        /// <summary>제자리에 멈춘다(목적지를 현재 위치로 요청).</summary>
        public bool StopMoving(long now)
        {
            float px, py;
            if (!CanAct || !SelfPos(now, out px, out py)) return false;
            return MoveTo(px, py, now, true);
        }

        public bool Attack(ulong targetId, long now)
        {
            if (!CanAct || targetId == 0) return false;
            if (now - _lastAttackSentMs < 100) return false;
            if (!Send(Build.ZcAttackReq(targetId))) return false;
            _lastAttackSentMs = now;
            return true;
        }

        /// <summary>스킬 시전 요청. 로컬 쿨다운 중이면 보내지 않는다(서버도 거부하므로 불필요한 거부 로그 방지).</summary>
        public bool UseSkill(uint skillId, ulong targetId, float x, float y, long now)
        {
            if (!CanAct) return false;
            if (SkillCooldownLeftMs(skillId, now) > 0) return false;
            if (World.Self.Casting) return false;
            if (SkillPending(now)) return false;
            if (now - _lastSkillSentMs < 100) return false;
            if (!Send(Build.ZcSkillReq(skillId, targetId, x, y))) return false;
            _lastSkillSentMs = now;
            _skillPendingUntil = now + SkillResponseWaitMs;
            return true;
        }

        /// <summary>
        /// 보낸 스킬 요청의 답(시전 시작/발동/거부/취소)이 아직 오지 않았다.
        /// 이 사이에 이동·정지 요청을 보내면 서버가 막 시작한 시전을 "이동"으로 취소하고, 같은 스킬을 또 보내면 거부 로그만 쌓인다.
        /// 시전 시간이 있는 스킬은 서버가 시전을 시작하면서 스스로 멈추므로 클라이언트가 먼저 멈출 필요도 없다.
        /// </summary>
        public bool SkillPending(long now) { return now < _skillPendingUntil; }

        public long SkillCooldownLeftMs(uint skillId, long now)
        {
            long ready;
            if (!_skillReadyAt.TryGetValue(skillId, out ready)) return 0;
            long left = ready - now;
            return left > 0 ? left : 0;
        }

        private void OnSelfSkillUsed(uint skillId, bool castStarted, long now)
        {
            SkillRow sk = Data.Skill(skillId);
            if (sk == null) return;
            _skillReadyAt[skillId] = now + sk.CooldownMs;
        }

        private bool Send(byte[] packet) { return SendOverride != null ? SendOverride(packet) : Net.Send(packet); }

        // ------------------------------------------------------------------ 오프라인(데모)

        /// <summary>서버 없이 존에 들어간 상태로 만든다. 이후 패킷은 InjectFrame 으로 넣는다.</summary>
        public void EnterOffline(float spawnX, float spawnY, string statusText, long now)
        {
            World.Clear();
            World.InitSelf(spawnX, spawnY, Data.ResolveHeroId(Config.HeroId), now);
            World.Ping.Reset();
            State = SessionState.InZone; StatusText = statusText;
            _wasInZone = true; InZoneSinceMs = now;
        }

        /// <summary>서버에서 온 것처럼 존 패킷 한 개를 월드에 적용한다.</summary>
        public void InjectFrame(Frame f, long now) { World.Apply(f, now); }

        public void Leave()
        {
            if (State == SessionState.InZone) Send(Build.GwLeaveZoneReq());
        }

        public void Dispose() { Net.Close(); }

        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}

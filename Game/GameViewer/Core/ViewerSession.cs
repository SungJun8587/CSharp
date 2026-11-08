//***************************************************************************
// ViewerSession.cs : 접속 흐름(로그인 -> 존 입장) + 월드 모델 + 명령(이동/공격/스킬)을 묶은 뷰어의 중심 객체.
// UI/Unity 는 Start() 후 매 프레임 Update(now)를 부르고, WorldModel 을 읽어 그리며, MoveTo/Attack/UseSkill 로 조작한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Threading;
using GameViewer.Core.Data;
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
        public readonly GatewayClient Net = new GatewayClient();

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

        public List<uint> MySkills { get { return Data.SkillsOf(HeroId); } }

        // ------------------------------------------------------------------ 접속

        /// <summary>비동기로 접속을 시작한다. 진행은 Update() 가 이어간다.</summary>
        public void Start(long now)
        {
            State = SessionState.Connecting;
            StatusText = Config.Host + ":" + Config.Port + " 접속 중...";
            _connectState = 0;
            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = Net.Connect(Config.Host, Config.Port, Config.ConnectTimeoutMs, out err);
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
                }
            }

            Frame f;
            while (Net.TryDequeue(out f))
                Dispatch(f, now);

            if (SendOverride == null && !Net.Connected && (State == SessionState.LoggingIn || State == SessionState.Entering || State == SessionState.InZone))
            {
                State = SessionState.Disconnected;
                StatusText = "연결 끊김: " + (Net.DisconnectReason ?? "unknown");
                World.AddLog(now, 2, StatusText);
            }

            World.Update(now);
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
                            World.Clear();
                            World.InitSelf(r.SpawnX, r.SpawnY, Data.ResolveHeroId(Config.HeroId), now);
                            State = SessionState.InZone; StatusText = "존 " + r.ZoneId + " 입장 완료";
                            break;
                        }
                    case GatewayPacketType.GwZoneLostNotify:
                        Fail(now, "존 연결 끊김 (zone " + GwZoneLostNotify.Parse(f.Data).ZoneId + ")");
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
        }

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
            State = SessionState.InZone; StatusText = statusText;
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

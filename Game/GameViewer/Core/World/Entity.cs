//***************************************************************************
// Entity.cs : 뷰어가 아는 엔티티 한 개의 상태. 위치는 "이동 시작점 + 방향 * 속도 * 경과시간"으로 보간한다.
//***************************************************************************
using System;
using System.Collections.Generic;
using GameViewer.Core.Data;
using GameViewer.Core.Protocol;

namespace GameViewer.Core.World
{
    public sealed class StatusInstance
    {
        public uint StatusId;
        public ulong CasterId;
        public long ExpireMs;       // 뷰어 로컬 시계 기준
    }

    public sealed class Entity
    {
        public ulong Id;
        public byte Type;               // EntityType
        public uint TemplateId;         // 몬스터 종류 / 영웅 ID
        public int Hp, MaxHp;
        public bool Dead;
        public long DeadAtMs;

        // 이동: (X,Y)는 MoveStartMs 시점의 위치
        public float X, Y, DestX, DestY, Speed;
        public bool Moving;
        public long MoveStartMs;

        public float FaceX = 1, FaceY = 0;      // 바라보는 방향(단위벡터 아님, 길이 0이면 무시)
        public long HitFlashMs = -100000;       // 마지막 피격 시각
        public long AttackAnimMs = -100000;     // 마지막 공격 동작 시각
        public byte AttackKind;                 // 마지막 공격의 종류: 0 근접 기본, 1 스킬, 2 원거리(발사)
        public float HitDirX, HitDirY;          // 마지막 피격의 방향(공격자 -> 피격자 단위벡터, 0이면 방향 모름)
        public long InvulnUntilMs;

        // 시전 중 스킬
        public uint CastSkillId;
        public long CastStartMs;
        public uint CastTimeMs;
        public ulong CastTargetId;
        public float CastX, CastY;

        // 보스 표시용 (서버 패킷에는 없으므로 보스 데이터와 체력 비율로 뷰어가 추정한다)
        public BossRow Boss;                    // 보스 정의 (보스가 아니면 null)
        public int BossPhase;                   // 현재 페이즈(1부터). 한 번 올라가면 내려가지 않는다(서버 규칙과 같음)
        public long PhaseChangedMs = -100000;
        public float GhostHp;                   // 체력바 뒤에서 천천히 줄어드는 잔상 (피해량 표시)
        public long GhostSinceMs;

        public readonly Dictionary<uint, StatusInstance> Statuses = new Dictionary<uint, StatusInstance>();

        public bool IsMonster { get { return Type == (byte)EntityType.Monster; } }
        public bool IsPlayer { get { return Type == (byte)EntityType.Player; } }
        public bool Casting { get { return CastSkillId != 0; } }

        /// <summary>now 시각의 보간 위치.</summary>
        public void PosAt(long now, out float x, out float y)
        {
            if (!Moving || Speed <= 0f) { x = X; y = Y; return; }
            float dx = DestX - X, dy = DestY - Y;
            float total = (float)Math.Sqrt(dx * dx + dy * dy);
            float walked = Speed * (now - MoveStartMs) / 1000f;
            if (walked <= 0f || total <= 0.0001f) { x = X; y = Y; return; }
            if (walked >= total) { x = DestX; y = DestY; return; }
            float t = walked / total;
            x = X + dx * t; y = Y + dy * t;
        }

        /// <summary>지금 걷고 있는지(아직 목적지에 닿기 전). walkedUnits 는 이동 시작 후 걸은 거리(걸음 애니메이션용).</summary>
        public bool WalkingAt(long now, out float walkedUnits)
        {
            walkedUnits = 0f;
            if (!Moving || Speed <= 0f) return false;
            float dx = DestX - X, dy = DestY - Y;
            float total = (float)Math.Sqrt(dx * dx + dy * dy);
            walkedUnits = Speed * (now - MoveStartMs) / 1000f;
            return walkedUnits > 0f && walkedUnits < total;
        }

        /// <summary>now 시각 위치에 멈춘다.</summary>
        public void StopAt(float x, float y)
        {
            X = x; Y = y; DestX = x; DestY = y; Moving = false;
        }

        public void StopHere(long now)
        {
            float x, y; PosAt(now, out x, out y); StopAt(x, y);
        }

        public void Face(float tx, float ty, long now)
        {
            float x, y; PosAt(now, out x, out y);
            float dx = tx - x, dy = ty - y;
            if (dx * dx + dy * dy > 0.0001f) { FaceX = dx; FaceY = dy; }
        }

        public bool HasStatus(uint id) { return Statuses.ContainsKey(id); }
        public bool IsBoss { get { return Boss != null; } }
        public bool IsEnraged { get { return Boss != null && Boss.EnrageStatusId != 0 && Statuses.ContainsKey(Boss.EnrageStatusId); } }
    }
}

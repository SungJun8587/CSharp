// BattleZone.Duel.cs : 결투. 서로 수락한 두 플레이어 사이에서만 공격이 가능해지도록 CanAttack만 열어 준다.
// 피해/사거리/스킬/사망 처리는 몬스터 전투와 같은 기존 코드를 그대로 쓴다(스킬 범위 공격도 CanAttack을 통해 상대만 맞는다).
using System;
using System.Collections.Generic;
using Game.Generated;
using Game.ZoneCommon;
using Game.ZoneCore;

namespace Game.ZoneServer;

public sealed partial class BattleZone
{
    private const uint DuelRequestTtlMs = 15000;        // 신청에 응답할 수 있는 시간
    private const uint DuelCountdownMs = 3000;          // 수락 뒤 시작까지
    private const uint DuelMaxMs = 5 * 60 * 1000;       // 결투 제한 시간(넘으면 무승부로 끝)
    private const float DuelRequestRange = 40.0f;       // 신청/수락할 수 있는 거리
    private const float DuelLeaveRange = 120.0f;        // 이보다 멀어지면 결투가 끝난다(도망 방지)

    private bool _allowPvp;
    private bool _allowDuel = true;

    /// <summary>존 규칙을 정한다. 구동 전에 부른다(AllowPvp는 존 설정에도 같은 값이 들어간다).</summary>
    public void SetRules(bool allowPvp, bool allowDuel)
    {
        _allowPvp = allowPvp;
        _allowDuel = allowDuel;
    }

    /// <summary>ZcDuelNotify(ZoneRules)의 RemainMs에 담는 규칙 비트: 1 = 자유 PvP, 2 = 결투 신청 가능.</summary>
    private uint RuleFlags => (_allowPvp ? 1u : 0u) | (_allowDuel ? 2u : 0u);

    private enum DuelPhase : byte { Pending, Countdown, Active }

    private sealed class Duel
    {
        public ZonePlayer A = null!;        // 신청한 쪽
        public ZonePlayer B = null!;        // 신청받은 쪽
        public DuelPhase Phase;
        public ulong DeadlineMs;            // Pending: 만료 시각, Countdown: 시작 시각, Active: 제한 시각

        public ZonePlayer Other(ZonePlayer p) => ReferenceEquals(p, A) ? B : A;
    }

    private readonly Dictionary<ulong, Duel> _duelOf = new Dictionary<ulong, Duel>();      // 캐릭터 ID -> 신청 중이거나 결투 중인 결투
    private readonly List<Duel> _duels = new List<Duel>();

    /// <summary>진행 중인(Active) 결투 상대끼리만 서로 공격할 수 있다. 그 밖의 플레이어 간 공격은 기본 정책(AllowPvp)을 따른다.</summary>
    protected override bool CanAttack(ZoneEntity attacker, ZoneEntity target)
    {
        if (base.CanAttack(attacker, target))
            return true;
        if (_duels.Count == 0 || attacker is not ZonePlayer a || target is not ZonePlayer t || ReferenceEquals(a, t))
            return false;
        return _duelOf.TryGetValue(a.Id, out Duel? d) && d.Phase == DuelPhase.Active && ReferenceEquals(d.Other(a), t);
    }

    private void HandleDuel(ZonePlayer player, ReadOnlySpan<byte> packet)
    {
        if (!ZcDuelReq.TryRead(packet, out ZcDuelReq req))
        {
            Reject(player, Limits.MalformedWeight);
            return;
        }

        switch ((DuelAction)req.Action)
        {
            case DuelAction.Request: DuelRequest(player, req.TargetId); break;
            case DuelAction.Accept: DuelAccept(player, req.TargetId); break;
            case DuelAction.Decline: DuelDecline(player, req.TargetId); break;
            case DuelAction.Cancel: DuelCancel(player); break;
            default: Reject(player, Limits.MalformedWeight); break;
        }
    }

    private void DuelRequest(ZonePlayer from, ulong targetId)
    {
        if (!_allowDuel)
        {
            SendDuel(from, DuelState.RejectNotAllowed, targetId);
            return;
        }
        ZonePlayer? to = FindEntityBase(targetId) as ZonePlayer;
        if (to == null || ReferenceEquals(to, from) || to.IsLeaving)
        {
            SendDuel(from, DuelState.RejectNoTarget, targetId);
            return;
        }
        if (!from.IsAlive || !to.IsAlive)
        {
            SendDuel(from, DuelState.RejectDead, targetId);
            return;
        }
        if (_duelOf.ContainsKey(from.Id) || _duelOf.ContainsKey(to.Id))
        {
            SendDuel(from, DuelState.RejectBusy, targetId);
            return;
        }
        if (Vec2.DistanceSq(from.Pos, to.Pos) > DuelRequestRange * DuelRequestRange)
        {
            SendDuel(from, DuelState.RejectTooFar, targetId);
            return;
        }

        Duel d = new Duel { A = from, B = to, Phase = DuelPhase.Pending, DeadlineMs = NowMs + DuelRequestTtlMs };
        _duelOf[from.Id] = d;
        _duelOf[to.Id] = d;
        _duels.Add(d);
        SendDuel(from, DuelState.RequestSent, to.Id);
        SendDuel(to, DuelState.Requested, from.Id, 0, DuelRequestTtlMs);
    }

    private void DuelAccept(ZonePlayer accepter, ulong challengerId)
    {
        if (!_duelOf.TryGetValue(accepter.Id, out Duel? d) || d.Phase != DuelPhase.Pending || !ReferenceEquals(d.B, accepter) || d.A.Id != challengerId)
        {
            SendDuel(accepter, DuelState.RejectNoRequest, challengerId);
            return;
        }
        if (!d.A.IsAlive || !accepter.IsAlive || Vec2.DistanceSq(d.A.Pos, accepter.Pos) > DuelRequestRange * DuelRequestRange)
        {
            FinishDuel(d, DuelState.Cancelled, 0);
            return;
        }

        d.Phase = DuelPhase.Countdown;
        d.DeadlineMs = NowMs + DuelCountdownMs;
        SendDuel(d.A, DuelState.Countdown, d.B.Id, 0, DuelCountdownMs);
        SendDuel(d.B, DuelState.Countdown, d.A.Id, 0, DuelCountdownMs);
    }

    private void DuelDecline(ZonePlayer decliner, ulong challengerId)
    {
        if (!_duelOf.TryGetValue(decliner.Id, out Duel? d) || d.Phase != DuelPhase.Pending || !ReferenceEquals(d.B, decliner) || d.A.Id != challengerId)
        {
            SendDuel(decliner, DuelState.RejectNoRequest, challengerId);
            return;
        }
        FinishDuel(d, DuelState.Declined, 0);
    }

    /// <summary>신청/카운트다운 중이면 취소, 진행 중이면 기권(남은 쪽이 이긴다).</summary>
    private void DuelCancel(ZonePlayer player)
    {
        if (!_duelOf.TryGetValue(player.Id, out Duel? d))
            return;
        if (d.Phase == DuelPhase.Pending && ReferenceEquals(d.B, player))
            FinishDuel(d, DuelState.Declined, 0);
        else
            FinishDuel(d, DuelState.Cancelled, d.Phase == DuelPhase.Active ? d.Other(player).Id : 0UL);
    }

    /// <summary>결투 상태를 정리하고 양쪽에 알린다. 이미 존을 떠난 쪽에는 보내지 않는다.</summary>
    private void FinishDuel(Duel d, DuelState state, ulong winnerId)
    {
        _duelOf.Remove(d.A.Id);
        _duelOf.Remove(d.B.Id);
        _duels.Remove(d);
        if (!d.A.IsLeaving)
            SendDuel(d.A, state, d.B.Id, winnerId);
        if (!d.B.IsLeaving)
            SendDuel(d.B, state == DuelState.Declined ? DuelState.Cancelled : state, d.A.Id, winnerId);
    }

    /// <summary>OnEntityDied에서 호출: 진행 중 결투의 한쪽이 쓰러지면 끝낸다. 신청/카운트다운 중이면 취소한다.</summary>
    private void DuelOnDied(ZoneEntity victim)
    {
        if (_duels.Count == 0 || victim is not ZonePlayer p || !_duelOf.TryGetValue(p.Id, out Duel? d))
            return;
        if (d.Phase == DuelPhase.Active)
            FinishDuel(d, DuelState.Ended, d.Other(p).Id);
        else
            FinishDuel(d, DuelState.Cancelled, 0);
    }

    /// <summary>HandleLeave에서 호출: 존을 떠나는 플레이어의 결투를 정리한다.</summary>
    private void DuelOnLeft(ZonePlayer p)
    {
        if (_duels.Count == 0 || !_duelOf.TryGetValue(p.Id, out Duel? d))
            return;
        p.MarkLeaving();            // FinishDuel이 떠나는 쪽에는 보내지 않게 한다(HandleLeave가 이어서 같은 표시를 한다)
        FinishDuel(d, DuelState.OpponentLeft, d.Phase == DuelPhase.Active ? d.Other(p).Id : 0UL);
    }

    /// <summary>매 틱: 만료, 시작, 거리 이탈을 처리한다. 결투가 없으면 바로 돌아온다.</summary>
    private void DuelUpdate()
    {
        if (_duels.Count == 0)
            return;

        ulong now = NowMs;
        for (int i = _duels.Count - 1; i >= 0; --i)
        {
            Duel d = _duels[i];
            switch (d.Phase)
            {
                case DuelPhase.Pending:
                    if (now >= d.DeadlineMs)
                        FinishDuel(d, DuelState.Expired, 0);
                    break;
                case DuelPhase.Countdown:
                    if (!d.A.IsAlive || !d.B.IsAlive || TooFarForDuel(d))
                        FinishDuel(d, DuelState.Cancelled, 0);
                    else if (now >= d.DeadlineMs)
                    {
                        d.Phase = DuelPhase.Active;
                        d.DeadlineMs = now + DuelMaxMs;
                        SendDuel(d.A, DuelState.Started, d.B.Id);
                        SendDuel(d.B, DuelState.Started, d.A.Id);
                    }
                    break;
                default:
                    if (TooFarForDuel(d))
                        FinishDuel(d, DuelState.OutOfRange, 0);
                    else if (now >= d.DeadlineMs)
                        FinishDuel(d, DuelState.Expired, 0);
                    break;
            }
        }
    }

    private static bool TooFarForDuel(Duel d) => Vec2.DistanceSq(d.A.Pos, d.B.Pos) > DuelLeaveRange * DuelLeaveRange;

    private void SendDuel(ZonePlayer to, DuelState state, ulong opponentId, ulong winnerId = 0, uint remainMs = 0)
        => SendPacket(to, new ZcDuelNotify { State = (byte)state, OpponentId = opponentId, WinnerId = winnerId, RemainMs = remainMs });
}

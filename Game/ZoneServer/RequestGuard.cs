// RequestGuard.cs : 클라이언트 요청 빈도 제한과 위반 점수. 존 스레드에서만 접근한다.
using System;
using Core.Util;

namespace Game.ZoneServer;

/// <summary>요청 종류. 종류마다 따로 빈도를 제한하고 따로 센다.</summary>
public enum RequestKind : byte
{
    Move,
    Attack,
    Skill,
    Pickup,
    Ping,
    Chat,
    Duel,
    SkillBook,  // 스킬 구매/장착
    Other,      // 위의 어느 것도 아닌 타입 (알 수 없는 패킷 포함)
}

/// <summary>
/// 요청 빈도 제한 규칙. 종류마다 토큰 버킷(초당 보충량 Rate, 최대 적립 Burst)을 하나씩 둔다.
/// 버킷이 비어 있는 요청은 버려지고 위반 점수가 오른다. 점수는 시간이 지나면 줄고(DecayPerSec), ViolationLimit에 이르면 추방한다.
/// 정상 클라이언트는 Rate 안에서만 요청하므로 점수가 오르지 않는다. 한계를 살짝 넘는 정도는 버려지기만 하고 점수가 감쇠로 상쇄되어 추방되지 않는다.
/// </summary>
public sealed class RequestLimits
{
    public readonly double[] Rate = { 20, 8, 8, 10, 2, 2, 2, 4, 2 };          // RequestKind 순서: 초당 허용 요청 수
    public readonly double[] Burst = { 40, 16, 16, 20, 6, 5, 4, 8, 4 };       // RequestKind 순서: 한꺼번에 허용되는 최대 요청 수

    public double ViolationLimit = 60;      // 이 점수에 이르면 추방 (0이면 추방하지 않고 버리기만 한다)
    public double DecayPerSec = 5;          // 초당 점수 감소량

    public double DroppedWeight = 1;        // 빈도 제한으로 버려진 요청 하나
    public double MalformedWeight = 5;      // 크기가 모자라 읽을 수 없는 패킷
    public double NonFiniteWeight = 10;     // NaN/Inf 좌표 등 정상 클라이언트가 보낼 수 없는 값
    public double UnknownTypeWeight = 2;    // 알 수 없는 패킷 타입
    public double MismatchWeight = 1;       // 서버 위치와 어긋난 이동 요청 (지연으로 가끔 생길 수 있어 가볍게)
}

/// <summary>플레이어 한 명의 요청 빈도/위반 상태.</summary>
public sealed class RequestGuard
{
    /// <summary>RequestKind 개수. 종류별 배열의 길이.</summary>
    public const int KindCount = (int)RequestKind.Other + 1;

    private readonly TokenBucket[] _buckets = new TokenBucket[KindCount];
    private bool _started;
    private double _score;
    private ulong _scoreMs;

    /// <summary>현재 위반 점수 (감쇠 반영 전 마지막 값).</summary>
    public double Score => _score;

    /// <summary>요청 하나를 받아 줄지 판정한다. 종류별 토큰 버킷(Core.Util.TokenBucket)에서 토큰 하나를 빼며, 없으면 false.</summary>
    public bool TryTake(RequestKind kind, ulong nowMs, RequestLimits limits)
    {
        if (!_started)
        {
            for (int i = 0; i < KindCount; ++i)
                _buckets[i] = new TokenBucket(limits.Burst[i], limits.Rate[i], (long)nowMs);
            _scoreMs = nowMs;
            _started = true;
        }

        return _buckets[(int)kind].TryTake((long)nowMs);
    }

    /// <summary>위반 점수를 더한다. 먼저 경과 시간만큼 감쇠한다.</summary>
    /// <returns>점수가 추방 한계에 이르렀으면 true (한계가 0이면 항상 false)</returns>
    public bool AddViolation(double weight, ulong nowMs, RequestLimits limits)
    {
        if (nowMs > _scoreMs)
        {
            _score = Math.Max(0.0, _score - (nowMs - _scoreMs) * 0.001 * limits.DecayPerSec);
            _scoreMs = nowMs;
        }

        _score += weight;
        return limits.ViolationLimit > 0 && _score >= limits.ViolationLimit;
    }
}

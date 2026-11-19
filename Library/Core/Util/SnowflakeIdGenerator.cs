// SnowflakeIdGenerator.cs : 서버 여러 대에서 겹치지 않는 64비트 ID 생성기.
using System;

namespace Core.Util;

/// <summary>
/// 상위부터 41비트 시각(기준 시각 이후 ms) | 10비트 노드 번호 | 12비트 순번. 시간순으로 커지고, 노드 번호가 다르면 겹치지 않는다.
/// 같은 ms에 4096개까지 만들 수 있고, 그보다 많으면 논리 시각을 한 칸 앞서 간다(실제 시계가 따라올 때까지 ID는 계속 증가한다).
/// <para>시계가 뒤로 가도(NTP 보정 등) ID는 줄어들지 않는다: 마지막 시각을 그대로 쓰고 순번만 올린다.</para>
/// <para>스레드 안전하다. 서버 프로세스마다 서로 다른 <c>nodeId</c>(0~1023)를 설정으로 지정해야 한다.</para>
/// </summary>
public sealed class SnowflakeIdGenerator
{
    public const int NodeBits = 10;
    public const int SequenceBits = 12;
    public const int MaxNodeId = (1 << NodeBits) - 1;
    private const long SequenceMask = (1L << SequenceBits) - 1;

    /// <summary>기본 기준 시각: 2026-01-01 00:00:00 UTC.</summary>
    public static readonly DateTimeOffset DefaultEpoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly long _epochMs;
    private readonly long _nodeShifted;
    private readonly Func<long> _clock;
    private readonly object _lock = new object();
    private long _lastMs;
    private long _sequence;

    /// <param name="nodeId">0~1023. 같은 ID 공간을 쓰는 프로세스끼리 달라야 한다</param>
    /// <param name="epoch">기준 시각(null이면 <see cref="DefaultEpoch"/>). 한 번 정하면 바꾸지 않는다</param>
    /// <param name="clock">현재 UTC unix ms(테스트에서 바꿔 끼운다)</param>
    public SnowflakeIdGenerator(int nodeId, DateTimeOffset? epoch = null, Func<long>? clock = null)
    {
        if (nodeId < 0 || nodeId > MaxNodeId)
            throw new ArgumentOutOfRangeException(nameof(nodeId), "0~" + MaxNodeId);
        _epochMs = (epoch ?? DefaultEpoch).ToUnixTimeMilliseconds();
        _clock = clock ?? TimeUtil.NowMillis;
        _nodeShifted = (long)nodeId << SequenceBits;
        _lastMs = -1;
    }

    public int NodeId => (int)(_nodeShifted >> SequenceBits);

    public ulong Next()
    {
        lock (_lock)
        {
            long now = _clock() - _epochMs;
            if (now < 0)
                throw new InvalidOperationException("시계가 기준 시각보다 앞이다");

            if (now > _lastMs)
            {
                _lastMs = now;
                _sequence = 0;
            }
            else
            {
                // 같은 ms이거나 시계가 뒤로 갔다: 마지막 시각을 유지하고 순번을 올린다.
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                    ++_lastMs;      // 이 ms의 순번을 다 썼다: 논리 시각을 한 칸 앞선다
            }
            return (ulong)((_lastMs << (NodeBits + SequenceBits)) | _nodeShifted | _sequence);
        }
    }

    /// <summary>ID를 (생성 시각 UTC, 노드 번호, 순번)으로 푼다. 같은 기준 시각의 생성기가 만든 ID여야 한다.</summary>
    public (DateTimeOffset Time, int NodeId, int Sequence) Parse(ulong id)
    {
        long ms = (long)(id >> (NodeBits + SequenceBits)) + _epochMs;
        return (DateTimeOffset.FromUnixTimeMilliseconds(ms), (int)((id >> SequenceBits) & MaxNodeId), (int)(id & (ulong)SequenceMask));
    }
}

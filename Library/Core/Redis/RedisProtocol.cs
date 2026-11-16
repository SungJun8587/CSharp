// RedisProtocol.cs : RESP(REdis Serialization Protocol) 데이터 타입과 콜백 형식.
using System;

namespace Core.Redis;

/// <summary>
/// Redis 응답 하나를 표현하는 재귀적 값. SimpleString, Error, Integer, BulkString, Array를 모두 수용한다.
/// 문자열은 UTF-8로 해석한다(바이너리 안전하지 않음 — Redis에 저장하는 값이 UTF-8 텍스트라는 전제).
/// </summary>
public sealed class RedisValue
{
    /// <summary>RESP 타입.</summary>
    public RedisType Type { get; set; } = RedisType.Unknown;

    /// <summary>정수 값, 또는 BulkString/Array의 선언 길이(-1이면 Null).</summary>
    public long Int64Val { get; set; }

    /// <summary>문자열 데이터(SimpleString, Error, BulkString).</summary>
    public string StrVal { get; set; } = string.Empty;

    /// <summary>Array일 때의 하위 요소 목록. 배열이 아니면 빈 배열.</summary>
    public RedisValue[] ArrayVal { get; set; } = Array.Empty<RedisValue>();

    /// <summary>Null Bulk String 또는 Null Array인지 확인한다.</summary>
    public bool IsNull => (Type == RedisType.BulkString || Type == RedisType.Array) && Int64Val == -1;

    public override string ToString() => Type switch
    {
        RedisType.Integer => ":" + Int64Val,
        RedisType.Array => IsNull ? "*-1" : "*" + ArrayVal.Length,
        RedisType.BulkString => IsNull ? "$-1" : "$" + StrVal,
        RedisType.Error => "-" + StrVal,
        RedisType.SimpleString => "+" + StrVal,
        _ => "?"
    };
}

/// <summary>명령 응답 콜백. 에러(서버 에러 또는 연결 문제로 만든 에러)도 값(RedisType.Error)으로 전달된다.</summary>
public delegate void RedisCallback(RedisValue value);

/// <summary>구독 연결이 서버로부터 받은 push 메시지(["message", channel, payload] 또는 ["pmessage", pattern, channel, payload]) 콜백.</summary>
public delegate void RedisPushCallback(RedisValue value);

/// <summary>구독한 채널(또는 패턴)에 메시지가 도착했을 때 호출되는 콜백. channel은 메시지가 발행된 실제 채널(패턴 구독이어도 실제 채널).</summary>
public delegate void RedisMessageCallback(string channel, string payload);

/// <summary>
/// Redis 명령을 보내는 쪽의 공통 인터페이스. 노드 하나(또는 여럿)를 가진 서비스가 구현하며,
/// <see cref="RedisScript"/>, <see cref="RedisRetry"/>, <see cref="RedisServerHeartbeat"/>가 이 인터페이스에만 의존한다.
/// </summary>
public interface IRedisCommandSender
{
    /// <summary>노드를 지정하지 않았을 때 쓰이는 기본 노드 ID.</summary>
    short DefaultNodeId { get; }

    /// <summary>기본 노드로 명령을 비동기 실행한다. false면 콜백은 호출되지 않는다.</summary>
    bool SendCommand(System.Collections.Generic.IReadOnlyList<string> args, RedisCallback? callback);

    /// <summary>지정한 노드로 명령을 비동기 실행한다. false면 콜백은 호출되지 않는다.</summary>
    bool SendCommand(short nodeId, System.Collections.Generic.IReadOnlyList<string> args, RedisCallback? callback);
}

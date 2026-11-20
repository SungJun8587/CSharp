// RedisProtocol.cs : RESP(REdis Serialization Protocol) 데이터 타입과 콜백 형식.
using System;

namespace Core.Redis;

/// <summary>
/// Redis 응답 하나를 표현하는 재귀적 값. SimpleString, Error, Integer, BulkString, Array를 모두 수용한다.
/// <see cref="StrVal"/>은 UTF-8로 해석한 문자열이고, 바이너리(UTF-8이 아닌 바이트)가 들어 있을 수 있는 값은 <see cref="BytesVal"/>로 읽는다 —
/// 원본 바이트가 손상 없이 보존된다.
/// </summary>
public sealed class RedisValue
{
    private byte[]? _rawBytes;      // UTF-8로 해석되지 않는 바이트가 있을 때만 원본을 따로 쥔다(텍스트는 StrVal 에서 다시 만들 수 있다)

    /// <summary>RESP 타입.</summary>
    public RedisType Type { get; set; } = RedisType.Unknown;

    /// <summary>정수 값, 또는 BulkString/Array의 선언 길이(-1이면 Null).</summary>
    public long Int64Val { get; set; }

    /// <summary>문자열 데이터(SimpleString, Error, BulkString).</summary>
    public string StrVal { get; set; } = string.Empty;

    /// <summary>
    /// 문자열 데이터의 원본 바이트(SimpleString, Error, BulkString). UTF-8이 아닌 바이너리도 그대로 돌려준다.
    /// 텍스트 값은 <see cref="StrVal"/>을 UTF-8로 다시 인코딩한 것과 같다. 호출마다 새 배열이다(바이너리 값은 보관된 원본의 복사본).
    /// </summary>
    public byte[] BytesVal
    {
        get
        {
            if (_rawBytes != null)
                return (byte[])_rawBytes.Clone();
            return StrVal.Length == 0 ? Array.Empty<byte>() : System.Text.Encoding.UTF8.GetBytes(StrVal);
        }
    }

    /// <summary>파서가 UTF-8로 해석할 수 없는 바이트를 발견했을 때 원본을 보존한다.</summary>
    internal void SetRawBytes(byte[] raw) => _rawBytes = raw;

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

/// <summary>바이너리 페이로드용 메시지 콜백. payload 는 발행된 바이트 그대로다(UTF-8 이 아니어도 손상 없음).</summary>
public delegate void RedisBinaryMessageCallback(string channel, byte[] payload);

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

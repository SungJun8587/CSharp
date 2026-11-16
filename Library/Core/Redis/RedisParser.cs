// RedisParser.cs : RESP 프로토콜 스트림 파서.
using System;
using System.Text;

namespace Core.Redis;

/// <summary>
/// 소켓에서 받은 원시 바이트를 <see cref="Feed"/>로 누적하고 <see cref="TryParse"/>를 반복 호출해 완성된
/// RESP 값을 순서대로 꺼내는 파서. 데이터가 부족하면 NeedMoreData를 돌려주며 버퍼를 그대로 보존하므로,
/// 다음 Feed() 뒤 다시 호출하면 이어서 파싱한다. 미완성 데이터의 보관 책임은 이 클래스 하나가 가진다.
/// <para>
/// 방어 상한: 누적 크기(<see cref="RedisConst.MaxPendingBufferSize"/>), 배열 원소 수(<see cref="RedisConst.MaxArrayElements"/>),
/// 중첩 깊이(<see cref="RedisConst.MaxParseDepth"/>). Feed()가 false를 돌려주거나 TryParse()가 ProtocolError를
/// 돌려주면 스트림을 신뢰할 수 없으므로 호출부는 연결을 끊어야 한다.
/// </para>
/// 스레드 안전하지 않다 — 연결 하나의 수신 루프만 쓴다.
/// </summary>
/// <example>
/// <code>
/// var parser = new RedisParser();
/// if (!parser.Feed(recvBytes)) { /* 상한 초과 — 연결을 끊는다 */ }
/// for (;;)
/// {
///     var r = parser.TryParse(out var value);
///     if (r == RedisParseResult.Success) { /* 처리 */ continue; }
///     if (r == RedisParseResult.ProtocolError) { /* 연결을 끊는다 */ }
///     break; // NeedMoreData — 다음 Feed()를 기다린다
/// }
/// </code>
/// </example>
public sealed class RedisParser
{
    private const int InitialBufferSize = 4096;

    private byte[] _buf = new byte[InitialBufferSize];
    private int _start;         // 미처리 데이터 시작
    private int _end;           // 미처리 데이터 끝(배타)

    // 직전 TryParse()가 NeedMoreData로 끝났을 때, 그 값을 완성하는 데 최소한 필요한 누적 바이트 수.
    // 큰 Bulk String이 여러 조각으로 도착하는 동안 매 조각마다 처음부터 다시 파싱하는 낭비를 막는다.
    private int _minBytesNeeded;

    private static readonly string s_ok = "OK";
    private static readonly string s_pong = "PONG";
    private static readonly string s_queued = "QUEUED";

    /// <summary>누적 버퍼에 아직 처리하지 않은 바이트 수.</summary>
    public int PendingBytes => _end - _start;

    /// <summary>파서 내부 상태 및 미완성 버퍼를 초기화한다.</summary>
    public void Reset()
    {
        _start = 0;
        _end = 0;
        _minBytesNeeded = 0;
        if (_buf.Length > RedisConst.ParserRetainLimit)
            _buf = new byte[InitialBufferSize];
    }

    /// <summary>수신된 원시 바이트를 내부 누적 버퍼에 추가한다.</summary>
    /// <returns>false: 누적 크기가 상한을 넘어 추가하지 않음 — 이 연결을 끊는 것이 안전하다.</returns>
    public bool Feed(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return true;

        if (data.Length > RedisConst.MaxPendingBufferSize - PendingBytes)
            return false;

        if (_end + data.Length > _buf.Length)
        {
            int pending = PendingBytes;
            if (_start > 0)
            {
                Buffer.BlockCopy(_buf, _start, _buf, 0, pending);
                _start = 0;
                _end = pending;
            }

            if (_end + data.Length > _buf.Length)
            {
                long newSize = Math.Max((long)_buf.Length * 2, (long)_end + data.Length);
                Array.Resize(ref _buf, (int)Math.Min(newSize, RedisConst.MaxPendingBufferSize));
            }
        }

        data.CopyTo(_buf.AsSpan(_end));
        _end += data.Length;
        return true;
    }

    /// <summary>
    /// 누적 버퍼에 이미 들어 있는 데이터만으로 완전한 RESP 값 하나를 꺼낸다. 임시 객체에 완전히 파싱한 뒤
    /// 성공했을 때만 value로 내보내므로, 배열 파싱 도중 데이터가 끊겨도 일부만 채워진 값이 새어나가지 않는다.
    /// 실패해도(NeedMoreData 포함) 버퍼 상태는 보존되어 다음 Feed() 이후 이어서 재시도할 수 있다.
    /// </summary>
    public RedisParseResult TryParse(out RedisValue? value)
    {
        value = null;

        int pending = PendingBytes;
        if (pending == 0)
            return RedisParseResult.NeedMoreData;

        // 직전 시도에서 알아낸 필요 바이트 수에 아직 못 미치면 다시 파싱해도 같은 결과다.
        if (pending < _minBytesNeeded)
            return RedisParseResult.NeedMoreData;
        _minBytesNeeded = 0;

        int offset = 0;
        RedisParseResult result = ParseValue(new ReadOnlySpan<byte>(_buf, _start, pending), ref offset, out RedisValue parsed, 0);
        if (result != RedisParseResult.Success)
            return result;

        _start += offset;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
            if (_buf.Length > RedisConst.ParserRetainLimit)
                _buf = new byte[InitialBufferSize];
        }

        value = parsed;
        return RedisParseResult.Success;
    }

    // RESP 접두 타입에 따라 재귀적으로 값 구조를 파싱한다. 실패하면 offset을 호출 시점으로 되돌린다.
    private RedisParseResult ParseValue(ReadOnlySpan<byte> buf, ref int offset, out RedisValue value, int depth)
    {
        value = null!;

        if (offset >= buf.Length)
            return RedisParseResult.NeedMoreData;

        // 중첩 배열의 재귀 깊이 제한 — 스택 오버플로 방지.
        if (depth > RedisConst.MaxParseDepth)
            return RedisParseResult.ProtocolError;

        int saved = offset;
        byte prefix = buf[offset++];

        RedisParseResult lineResult = ReadLine(buf, ref offset, out ReadOnlySpan<byte> line);
        if (lineResult != RedisParseResult.Success)
        {
            offset = saved;
            return lineResult;
        }

        switch ((char)prefix)
        {
            case '+':
                value = new RedisValue { Type = RedisType.SimpleString, StrVal = DecodeLine(line) };
                return RedisParseResult.Success;

            case '-':
                value = new RedisValue { Type = RedisType.Error, StrVal = DecodeLine(line) };
                return RedisParseResult.Success;

            case ':':
            {
                if (!RedisHelper.ParseInteger(line, out long intVal))
                    break;

                value = new RedisValue { Type = RedisType.Integer, Int64Val = intVal };
                return RedisParseResult.Success;
            }

            case '$':
            {
                if (!RedisHelper.ParseInteger(line, out long len))
                    break;

                // 단일 Bulk String의 상한은 누적 상한과 같다 — 정상 응답이 이보다 큰 값을 보낼 일이 없다.
                if (len < -1 || len > RedisConst.MaxPendingBufferSize)
                    break;

                if (len == -1)
                {
                    value = new RedisValue { Type = RedisType.BulkString, Int64Val = -1 };
                    return RedisParseResult.Success; // Null Bulk String
                }

                if (buf.Length - offset < len + 2)
                {
                    // 이 값은 헤더 뒤로 len + 2바이트가 더 와야 완성된다 — 그 전에는 재파싱할 필요가 없다.
                    _minBytesNeeded = (int)(offset + len + 2);
                    offset = saved;
                    return RedisParseResult.NeedMoreData;
                }

                int dataStart = offset;
                int dataLen = (int)len;
                offset += dataLen;

                // 데이터 뒤에 실제로 CRLF가 오는지 확인한다 — 길이만 믿으면 그 자리가 진짜 "\r\n"이 아닐 때
                // 이후 스트림 전체가 밀려서 깨지는데도 에러 없이 진행한다.
                if (buf[offset] != (byte)'\r' || buf[offset + 1] != (byte)'\n')
                    break;
                offset += 2;

                value = new RedisValue
                {
                    Type = RedisType.BulkString,
                    Int64Val = len,
                    StrVal = dataLen == 0 ? string.Empty : Encoding.UTF8.GetString(buf.Slice(dataStart, dataLen))
                };
                return RedisParseResult.Success;
            }

            case '*':
            {
                if (!RedisHelper.ParseInteger(line, out long count))
                    break;

                if (count < -1 || count > RedisConst.MaxArrayElements)
                    break;

                if (count == -1)
                {
                    value = new RedisValue { Type = RedisType.Array, Int64Val = -1 };
                    return RedisParseResult.Success; // Null Array
                }

                // 원소는 최소 4바이트(":0\r\n")이므로 남은 데이터로 가능한 개수까지만 미리 잡는다 —
                // 선언된 개수만큼 매 시도마다 할당하지 않게 한다.
                int capacity = (int)Math.Min(count, (buf.Length - offset) / 4 + 1);
                RedisValue[] elems = capacity == 0 ? Array.Empty<RedisValue>() : new RedisValue[capacity];
                for (long i = 0; i < count; ++i)
                {
                    if (i >= elems.Length)
                    {
                        offset = saved;
                        return RedisParseResult.NeedMoreData; // 남은 데이터로는 원소를 더 담을 수 없다
                    }

                    RedisParseResult elemResult = ParseValue(buf, ref offset, out RedisValue elem, depth + 1);
                    if (elemResult != RedisParseResult.Success)
                    {
                        offset = saved;
                        return elemResult;
                    }

                    elems[i] = elem;
                }

                value = new RedisValue { Type = RedisType.Array, Int64Val = count, ArrayVal = elems };
                return RedisParseResult.Success;
            }
        }

        offset = saved;
        return RedisParseResult.ProtocolError;
    }

    // "\r\n" 개행 기준으로 한 줄을 읽는다. 개행을 못 찾으면 데이터 부족으로 본다.
    private static RedisParseResult ReadLine(ReadOnlySpan<byte> buf, ref int offset, out ReadOnlySpan<byte> line)
    {
        int idx = buf.Slice(offset).IndexOf("\r\n"u8);
        if (idx < 0)
        {
            line = default;
            return RedisParseResult.NeedMoreData;
        }

        line = buf.Slice(offset, idx);
        offset += idx + 2;
        return RedisParseResult.Success;
    }

    // 가장 흔한 짧은 응답은 문자열을 새로 만들지 않는다.
    private static string DecodeLine(ReadOnlySpan<byte> line)
    {
        if (line.IsEmpty)
            return string.Empty;

        if (line.Length == 2 && line[0] == (byte)'O' && line[1] == (byte)'K')
            return s_ok;
        if (line.SequenceEqual("PONG"u8))
            return s_pong;
        if (line.SequenceEqual("QUEUED"u8))
            return s_queued;

        return Encoding.UTF8.GetString(line);
    }
}

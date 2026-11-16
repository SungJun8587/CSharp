// RedisResultSet.cs : RedisValue 응답에서 타입 안전하게 데이터를 순차 추출하는 클래스.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Core.Util;

namespace Core.Redis;

/// <summary>
/// 파싱 결과(<see cref="RedisValue"/>)를 문자열 컨테이너로 펼쳐 놓고 순차 커서로 정수/문자열을 꺼낸다.
/// SimpleString, BulkString, Integer, Array 응답을 모두 받는다(Null은 빈 결과, 배열 안의 Null은 빈 문자열).
/// 변환에 성공했을 때만 커서가 움직이므로, 실패한 필드가 조용히 소비되어 이후 필드가 밀리는 일이 없다.
/// </summary>
/// <example>
/// <code>
/// service.SendCommand(new[] { "HMGET", "User:1001", "Level", "Exp", "Name" }, res =>
/// {
///     var rs = new RedisResultSet(res);
///     if (rs.IsError || rs.IsEmpty) return;
///
///     if (rs.GetData(out int level) &amp;&amp; rs.GetData(out long exp) &amp;&amp; rs.GetData(out string name))
///     {
///         // 처리
///     }
/// });
/// </code>
/// </example>
public sealed class RedisResultSet
{
    private readonly List<string> _items = new List<string>();
    private int _readCursor;

    /// <param name="value">파싱이 완료된 Redis 응답 객체</param>
    public RedisResultSet(RedisValue value)
    {
        if (value.IsNull)
            return;

        switch (value.Type)
        {
            case RedisType.SimpleString:
            case RedisType.BulkString:
                _items.Add(value.StrVal);
                break;

            case RedisType.Integer:
                _items.Add(value.Int64Val.ToString(CultureInfo.InvariantCulture));
                break;

            case RedisType.Array:
                _items.Capacity = value.ArrayVal.Length;
                foreach (RedisValue item in value.ArrayVal)
                {
                    if (item.IsNull)
                        _items.Add(string.Empty);
                    else if (item.Type == RedisType.Integer)
                        _items.Add(item.Int64Val.ToString(CultureInfo.InvariantCulture));
                    else
                        _items.Add(item.StrVal);
                }
                break;

            // 에러 응답은 어느 분기에도 해당하지 않아 목록이 빈 채로 남는다.
            // "정상적인 빈 결과"와 구분할 수 있도록 IsError로 플래그를 남긴다.
            case RedisType.Error:
                IsError = true;
                break;
        }
    }

    /// <summary>
    /// 원본 응답이 에러였는지(서버 에러 응답 또는 연결 오류). 에러 응답은 데이터를 담지 않아 IsEmpty가 true가 되므로
    /// "정상적인 빈 결과"와 구분해야 하는 호출부는 IsEmpty보다 먼저 IsError를 본다.
    /// </summary>
    public bool IsError { get; }

    /// <summary>결과 데이터가 비어 있는지 여부.</summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>전체 결과 요소의 개수.</summary>
    public int Size => _items.Count;

    /// <summary>
    /// 순차 커서 위치에서 정수를 추출한다(sbyte~ulong 공용). 문자열 전체가 10진 정수이고 T 범위 안일 때만
    /// 성공하며 이때만 커서를 옮긴다. 부호 없는 형식에 음수 문자열이 오면 실패한다.
    /// </summary>
    public bool GetData<T>(out T value) where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        value = T.Zero;
        if (_readCursor >= _items.Count)
            return false;

        if (!RedisHelper.ParseInteger(_items[_readCursor], out value))
        {
            value = T.Zero;
            return false;
        }

        ++_readCursor;
        return true;
    }

    /// <summary>순차 커서 위치에서 문자열을 추출한다. 범위를 벗어나면 빈 문자열과 false.</summary>
    public bool GetData(out string value)
    {
        value = string.Empty;
        if (_readCursor >= _items.Count)
            return false;

        value = _items[_readCursor++];
        return true;
    }

    /// <summary>
    /// 랭킹 등 (Member-Score) 쌍으로 구성된 응답(예: ZRANGE ... WITHSCORES)의 쌍 개수(요소 수 / 2)를 반환한다.
    /// 홀수 개면 기대한 커맨드 형태가 아니라는 신호로, 디버그 빌드에서는 즉시 단정하고 운영 빌드에서는 로그만 남기고
    /// 완성된 쌍의 개수를 돌려준다.
    /// </summary>
    public int GetRankInfoRetCount()
    {
        if (_items.Count == 0)
            return 0;

#if DEBUG
        Debug.Assert(_items.Count % 2 == 0, "RedisResultSet.GetRankInfoRetCount: 응답 요소 수가 짝수가 아님");
#else
        if (_items.Count % 2 != 0)
            LogManager.Error("RedisResultSet.GetRankInfoRetCount: 응답 요소 수({0})가 짝수가 아님", _items.Count);
#endif

        return _items.Count / 2;
    }
}

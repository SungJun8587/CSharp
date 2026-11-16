// RedisHelper.cs : Redis 모듈 안팎에서 반복되던 작은 공용 함수 모음.
using System;
using System.Globalization;
using System.Numerics;

namespace Core.Redis;

/// <summary>에러 응답 생성, 인자 목록 생성, 정수 파싱, 재연결 백오프 계산.</summary>
public static class RedisHelper
{
    /// <summary>
    /// 클라이언트/풀이 직접 만드는 에러 응답(RedisType.Error)을 생성한다. 서버가 돌려준 에러가 아니라
    /// 연결/전송 문제를 콜백에 알리는 값이며, <see cref="IsConnectionError"/>가 접두어로 이 값을 구분한다.
    /// </summary>
    public static RedisValue MakeError(string message) => new RedisValue { Type = RedisType.Error, StrVal = message };

    /// <summary>Redis 명령 인자 목록을 한 줄로 만든다. <c>RedisHelper.MakeArgs("SADD", key, member)</c></summary>
    public static string[] MakeArgs(params string[] args) => args;

    /// <summary>
    /// 응답이 "연결 문제"로 만들어진 에러인지 판별한다. 클라이언트는 연결 끊김/전송 실패/프로토콜 오류를
    /// 알리려고 직접 만든 에러 값을 콜백에 넘기는데, Redis 서버가 돌려준 에러(WRONGTYPE, NOSCRIPT, OOM 등)와
    /// 구분해 재시도처럼 연결 문제에만 의미 있는 처리에 쓴다.
    /// </summary>
    public static bool IsConnectionError(RedisValue value)
    {
        if (value.Type != RedisType.Error)
            return false;

        string s = value.StrVal;
        return s.StartsWith(RedisConst.ErrConnectionClosed, StringComparison.Ordinal)
            || s.StartsWith(RedisConst.ErrSendFailed, StringComparison.Ordinal)
            || s.StartsWith(RedisConst.ErrProtocolError, StringComparison.Ordinal);
    }

    /// <summary>
    /// UTF-8 바이트 전체가 10진 정수일 때만 변환해 돌려준다(예외 없음). 공백, '+' 접두, 뒤따르는 문자,
    /// NUL이 있으면 실패하고, T 범위를 벗어나거나 부호 없는 형식에 음수가 오면 실패한다.
    /// 파서의 길이 헤더와 <see cref="RedisResultSet"/>이 이 함수 하나를 공유한다.
    /// </summary>
    public static bool ParseInteger<T>(ReadOnlySpan<byte> utf8, out T value) where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        value = T.Zero;
        if (utf8.IsEmpty || utf8[0] == (byte)'+' || utf8.IndexOf((byte)0) >= 0)
            return false;

        bool isUnsigned = T.MinValue == T.Zero;
        if (isUnsigned && utf8[0] == (byte)'-')
            return false;

        if (!T.TryParse(utf8, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out T? parsed))
            return false;

        value = parsed;
        return true;
    }

    /// <summary><see cref="ParseInteger{T}(ReadOnlySpan{byte}, out T)"/>의 문자열 버전.</summary>
    public static bool ParseInteger<T>(ReadOnlySpan<char> text, out T value) where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        value = T.Zero;
        if (text.IsEmpty || text[0] == '+' || text.IndexOf('\0') >= 0)
            return false;

        bool isUnsigned = T.MinValue == T.Zero;
        if (isUnsigned && text[0] == '-')
            return false;

        if (!T.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out T? parsed))
            return false;

        value = parsed;
        return true;
    }

    /// <summary>
    /// 재연결 실패 횟수에 따른 지수 백오프 대기 시간(ms)을 계산한다.
    /// 1차 실패=ReconnectBackoffBaseMs, 이후 두 배씩 늘며 상한까지. jitterMs가 양수면 0~jitterMs를 무작위로 더한다.
    /// </summary>
    public static long ComputeReconnectBackoffMs(int failCount, int jitterMs = 0)
    {
        int shift = Math.Max(0, Math.Min(failCount - 1, RedisConst.ReconnectBackoffMaxShift));
        long delayMs = Math.Min(RedisConst.ReconnectBackoffBaseMs << shift, RedisConst.ReconnectBackoffMaxMs);

        if (jitterMs > 0)
            delayMs = Math.Min(delayMs + Random.Shared.Next(0, jitterMs + 1), RedisConst.ReconnectBackoffMaxMs);

        return delayMs;
    }
}

/// <summary>
/// 재연결 실패 횟수와 다음 재시도 시각을 묶은 지수 백오프 상태(풀과 구독 연결 공용).
/// 시각은 <see cref="Core.Util.TimeUtil.MonotonicNowMillis"/> 기준이다. 스레드 안전하지 않다 — 재연결 스레드 하나만 만진다.
/// </summary>
public sealed class ReconnectBackoff
{
    public int FailCount { get; private set; }
    public long NextRetryMs { get; private set; }

    public bool ShouldSkip(long nowMs) => FailCount > 0 && nowMs < NextRetryMs;

    public void OnFail(long nowMs, int jitterMs = 0)
    {
        ++FailCount;
        NextRetryMs = nowMs + RedisHelper.ComputeReconnectBackoffMs(FailCount, jitterMs);
    }

    public void Reset()
    {
        FailCount = 0;
        NextRetryMs = 0;
    }
}

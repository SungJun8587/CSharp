// AuthTokenSigner.cs : HMAC-SHA256 서명 토큰(만료, 키 교체).
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Core.Util;

/// <summary>토큰 검증 결과.</summary>
public enum TokenStatus
{
    Ok,
    Malformed,
    BadSignature,
    Expired,
    /// <summary>한 번만 쓸 수 있는 토큰이 이미 쓰였다.</summary>
    Replayed,
    /// <summary>토큰이 가리키는 키 ID를 이 서명기가 모른다(폐기된 키이거나 다른 환경의 토큰).</summary>
    UnknownKey,
}

/// <summary>
/// 로그인 서버가 발급하고 게임/존 서버가 검증하는 짧은 토큰. 형식: <c>base64url(payload) "." base64url(hmac)</c>.
/// payload = <c>키ID|accountId|만료(unix ms)|nonce(hex)</c>. 키는 서버들이 공유하는 비밀(설정 파일)이며 16바이트 이상이어야 한다.
/// <para><b>키 교체</b>: 서명기는 여러 키를 알 수 있고 발급에는 현재 키만 쓴다. 새 키를 모든 서버에 먼저 <c>검증용</c>으로 배포하고,
/// 발급 서버의 현재 키를 바꾼 뒤, 토큰 만료 시간이 지나면 옛 키를 뺀다.</para>
/// <para>서명 비교는 시간이 일정한 <see cref="CryptographicOperations.FixedTimeEquals"/>를 쓴다. 만료 시각은 <b>벽시계 unix ms</b>
/// (<see cref="TimeUtil.NowMillis"/>)로 비교한다(서버 간 시계 오차만큼 여유를 둔다). 이 클래스는 상태가 없어 스레드 안전하다.
/// 재사용 방지는 <see cref="NonceCache"/>와 함께 쓴다.</para>
/// </summary>
public sealed class AuthTokenSigner
{
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _currentKeyId;

    /// <param name="keys">키ID → 키(16바이트 이상). 키ID에는 '|'와 '.'를 쓸 수 없다</param>
    /// <param name="currentKeyId">발급에 쓸 키ID(keys에 있어야 한다)</param>
    public AuthTokenSigner(IReadOnlyDictionary<string, byte[]> keys, string currentKeyId)
    {
        if (keys == null || keys.Count == 0)
            throw new ArgumentException("키가 없다", nameof(keys));
        _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, byte[]> kv in keys)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Key.IndexOfAny(new[] { '|', '.' }) >= 0)
                throw new ArgumentException("잘못된 키ID: " + kv.Key, nameof(keys));
            if (kv.Value == null || kv.Value.Length < 16)
                throw new ArgumentException("키는 16바이트 이상이어야 한다: " + kv.Key, nameof(keys));
            _keys[kv.Key] = (byte[])kv.Value.Clone();
        }
        if (currentKeyId == null || !_keys.ContainsKey(currentKeyId))
            throw new ArgumentException("현재 키ID가 keys에 없다", nameof(currentKeyId));
        _currentKeyId = currentKeyId;
    }

    /// <summary>키 하나만 쓰는 간단한 경우(키ID는 "k0").</summary>
    public AuthTokenSigner(byte[] key) : this(new Dictionary<string, byte[]> { ["k0"] = key }, "k0") { }

    public AuthTokenSigner(string secret) : this(Encoding.UTF8.GetBytes(secret ?? "")) { }

    /// <summary>토큰을 만든다. accountId에는 '|'를 넣을 수 없다.</summary>
    public string Issue(string accountId, long expireAtMs, out string nonce)
    {
        if (string.IsNullOrEmpty(accountId) || accountId.IndexOf('|') >= 0)
            throw new ArgumentException("잘못된 accountId", nameof(accountId));
        Span<byte> nb = stackalloc byte[12];
        RandomNumberGenerator.Fill(nb);
        nonce = Convert.ToHexString(nb);
        string payload = _currentKeyId + "|" + accountId + "|" + expireAtMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + nonce;
        byte[] pb = Encoding.UTF8.GetBytes(payload);
        byte[] sig = HMACSHA256.HashData(_keys[_currentKeyId], pb);
        return Base64Url(pb) + "." + Base64Url(sig);
    }

    public string Issue(string accountId, long expireAtMs) => Issue(accountId, expireAtMs, out _);

    /// <summary>서명과 만료를 확인한다. nonceCache가 있으면 같은 토큰의 두 번째 사용을 <see cref="TokenStatus.Replayed"/>로 거절한다.</summary>
    public TokenStatus Verify(string token, long nowMs, out string accountId, NonceCache? nonceCache = null)
    {
        accountId = "";
        if (string.IsNullOrEmpty(token) || token.Length > 1024)
            return TokenStatus.Malformed;
        int dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
            return TokenStatus.Malformed;

        byte[]? pb = FromBase64Url(token.AsSpan(0, dot));
        byte[]? sig = FromBase64Url(token.AsSpan(dot + 1));
        if (pb == null || sig == null)
            return TokenStatus.Malformed;
        // 정규 인코딩만 받는다. base64는 마지막 글자의 남는 비트를 바꿔도 같은 바이트로 풀려서, 다른 문자열이 같은 토큰으로 통과하게 된다
        // (토큰 문자열로 중복 판정이나 차단 목록을 만들면 우회된다).
        if (!token.AsSpan(0, dot).SequenceEqual(Base64Url(pb)) || !token.AsSpan(dot + 1).SequenceEqual(Base64Url(sig)))
            return TokenStatus.Malformed;

        string payload = Encoding.UTF8.GetString(pb);
        string[] parts = payload.Split('|');
        if (parts.Length != 4)
            return TokenStatus.Malformed;
        if (!_keys.TryGetValue(parts[0], out byte[]? key))
            return TokenStatus.UnknownKey;

        byte[] expect = HMACSHA256.HashData(key, pb);
        if (!CryptographicOperations.FixedTimeEquals(expect, sig))
            return TokenStatus.BadSignature;

        if (!long.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long exp))
            return TokenStatus.Malformed;

        if (nowMs >= exp)
            return TokenStatus.Expired;

        if (nonceCache != null && !nonceCache.TryUse(parts[3], exp, nowMs))
            return TokenStatus.Replayed;

        accountId = parts[1];
        return TokenStatus.Ok;
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(ReadOnlySpan<char> s)
    {
        string t = s.ToString().Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4)
        {
            case 2: t += "=="; break;
            case 3: t += "="; break;
            case 1: return null;
        }
        try { return Convert.FromBase64String(t); }
        catch (FormatException) { return null; }
    }
}

// RedisScript.cs : Lua 스크립트를 EVALSHA로 실행하는 공용 헬퍼.
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Core.Redis;

/// <summary>
/// EVAL은 매 호출마다 스크립트 본문 전체를 보내고 Redis도 그때마다 본문의 SHA1을 계산한다. 이 클래스는 본문의 SHA1을 생성 시점에
/// 한 번 구해 두고 EVALSHA로 보낸다. Redis가 스크립트 캐시를 갖고 있지 않으면(처음 실행, 재시작, SCRIPT FLUSH) 서버가 NOSCRIPT
/// 에러를 돌려주고, 이때만 EVAL로 한 번 다시 보내 스크립트를 적재한다. 그 뒤의 호출은 다시 EVALSHA로 나간다.
/// SHA는 본문만으로 정해지므로 SCRIPT LOAD를 먼저 보내거나 SHA를 보관·동기화할 필요가 없다.
///
/// <para>NOSCRIPT가 아닌 에러(연결 오류 포함)는 그대로 콜백에 전달된다. 재시도가 필요하면 <see cref="RedisRetry"/>처럼 호출부가 처리한다.</para>
/// <para>생성 후에는 불변이라 여러 스레드에서 동시에 Run()해도 안전하다. 콜백은 서비스가 넘기는 스레드(RedisService는 JobQueue)에서 호출된다.
/// Redis Cluster에서는 스크립트가 쓰는 모든 키를 keys로 선언해야 한다(ARGV로 키를 넘기는 스크립트는 Cluster에서 쓸 수 없다).</para>
/// </summary>
/// <example>
/// <code>
/// static readonly RedisScript Consume = new RedisScript("local v = redis.call('GET', KEYS[1]) ... return v");
/// Consume.Run(redis, new[] { "UploadToken:abc" }, Array.Empty&lt;string&gt;(), res => { ... });
/// </code>
/// </example>
public sealed class RedisScript
{
    /// <param name="body">Lua 스크립트 본문. SHA1은 이 문자열의 UTF-8 바이트로 계산된다.</param>
    public RedisScript(string body)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Sha = ComputeSha1Hex(body);
    }

    /// <summary>스크립트 본문.</summary>
    public string Body { get; }

    /// <summary>본문의 SHA1(소문자 16진 40자) — Redis가 쓰는 스크립트 식별자와 같다.</summary>
    public string Sha { get; }

    /// <summary>문자열의 SHA1을 소문자 16진 문자열로 계산한다(테스트·진단용으로 공개).</summary>
    public static string ComputeSha1Hex(string data)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant();

    /// <summary>기본 노드에서 스크립트를 실행한다.</summary>
    /// <param name="keys">KEYS 배열</param>
    /// <param name="argv">ARGV 배열</param>
    /// <param name="callback">결과 콜백(생략 가능)</param>
    /// <returns>전송 접수 여부(false면 콜백은 호출되지 않는다 — <see cref="IRedisCommandSender.SendCommand(IReadOnlyList{string}, RedisCallback)"/>와 같음)</returns>
    public bool Run(IRedisCommandSender sender, IReadOnlyList<string> keys, IReadOnlyList<string> argv, RedisCallback? callback = null)
        => Run(sender, sender.DefaultNodeId, keys, argv, callback);

    /// <summary>지정한 노드에서 EVALSHA로 실행하고, NOSCRIPT이면 EVAL로 한 번 다시 보낸다.</summary>
    public bool Run(IRedisCommandSender sender, short nodeId, IReadOnlyList<string> keys, IReadOnlyList<string> argv, RedisCallback? callback = null)
    {
        // 보낸 EVALSHA 인자 배열을 콜백이 공유한다 — 폴백 때 키/인자를 다시 모으지 않고, 첫 두 칸(명령, SHA)만 바꿔 EVAL 인자를 만든다.
        var args = new string[3 + keys.Count + argv.Count];
        args[0] = "EVALSHA";
        args[1] = Sha;
        args[2] = keys.Count.ToString();
        for (int i = 0; i < keys.Count; ++i)
            args[3 + i] = keys[i];
        for (int i = 0; i < argv.Count; ++i)
            args[3 + keys.Count + i] = argv[i];

        string body = Body;

        return sender.SendCommand(nodeId, args, res =>
        {
            if (res.Type == RedisType.Error && res.StrVal.StartsWith("NOSCRIPT", StringComparison.Ordinal))
            {
                // 서버에 스크립트가 없다 — EVAL로 보내면 서버가 스크립트를 캐시에 적재하므로 다음부터는 EVALSHA가 통한다.
                var evalArgs = (string[])args.Clone();
                evalArgs[0] = "EVAL";
                evalArgs[1] = body;

                // SendCommand가 false면 콜백이 호출되지 않으므로, 실패 시 원래 콜백으로 직접 알린다.
                if (!sender.SendCommand(nodeId, evalArgs, callback))
                    callback?.Invoke(RedisHelper.MakeError(RedisConst.ErrSendRejected));

                return;
            }

            callback?.Invoke(res);
        });
    }
}

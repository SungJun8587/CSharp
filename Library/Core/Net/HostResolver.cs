// HostResolver.cs : implementation of the HostResolver class.
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Net;

/// <summary>
/// 호스트명 → IP 주소 해석. IP 문자열이면 DNS 없이 즉시 풀고, 이름이면 제한 시간을 두고 푼다.
/// DNS 조회는 취소할 수 없어 시간이 지나도 백그라운드에서 계속될 수 있으므로, 프로세스 전체에서 동시에 살아 있는
/// 조회 수에 상한(MaxActiveResolvers)을 둔다. 넘으면 그 시도는 바로 실패하여 느린 DNS 가 스레드/소켓을 소진하지 못하게 한다.
/// </summary>
public static class HostResolver
{
    /// <summary>동시에 진행할 수 있는 이름 해석 수(프로세스 전체).</summary>
    public const int MaxActiveResolvers = 4;

    private static int s_activeResolvers;

    /// <summary>진행 중인 이름 해석 수(진단용).</summary>
    public static int ActiveResolvers => Volatile.Read(ref s_activeResolvers);

    /// <summary>
    /// host 를 해석해 주소 목록을 돌려준다. host 가 비었거나, 해석에 실패/초과하거나, 동시 해석 상한에 걸리면 null.
    /// 목록은 IPv4 가 앞에 오도록 정렬한다(같은 계열 안에서는 DNS 응답 순서 유지).
    /// </summary>
    public static async Task<IPAddress[]?> ResolveAsync(string host, int timeoutMs)
    {
        // 빈 문자열을 DNS 에 넘기면 로컬 호스트 주소로 해석돼 성공해 버린다. 설정이 비어 있는 경우를 성공으로 오인하지 않게 막는다.
        if (string.IsNullOrWhiteSpace(host))
            return null;

        if (IPAddress.TryParse(host, out IPAddress? ip))
            return new[] { ip };

        if (Interlocked.Increment(ref s_activeResolvers) > MaxActiveResolvers)
        {
            Interlocked.Decrement(ref s_activeResolvers);
            return null;
        }

        Task<IPAddress[]> task;
        try
        {
            task = Dns.GetHostAddressesAsync(host);
        }
        catch (Exception)
        {
            // 255자를 넘는 이름 등 형식이 잘못된 호스트명은 동기적으로 예외를 던진다.
            Interlocked.Decrement(ref s_activeResolvers);
            return null;
        }

        _ = task.ContinueWith(static (t, _) =>
        {
            _ = t.Exception;    // 시간 초과로 버려진 작업의 예외를 관찰해 둔다
            Interlocked.Decrement(ref s_activeResolvers);
        }, null, TaskScheduler.Default);

        try
        {
            IPAddress[] list = await task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
            return OrderIPv4First(list);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>주소 목록에서 IPv4 를 우선해 하나를 고른다. 비었으면 null.</summary>
    public static IPAddress? PickPreferred(IPAddress[]? list)
        => list?.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? list?.FirstOrDefault();

    private static IPAddress[] OrderIPv4First(IPAddress[] list)
    {
        if (list.Length < 2)
            return list;

        return list.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Concat(list.Where(a => a.AddressFamily != AddressFamily.InterNetwork)).ToArray();
    }
}

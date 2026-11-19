// LocalAddress.cs : 이 PC의 사용할 만한 IPv4 주소 목록.
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Core.Net;

/// <summary>
/// 서버가 자기 주소를 알려야 할 때(존 서버가 게이트웨이에 등록, 로그 표시 등) 쓸 IPv4 주소를 고른다.
/// 동작 중인(Up) 어댑터만 보고, 루프백/링크 로컬(169.254.x.x) 주소와 가상 어댑터는 뺀다.
/// 가상 어댑터 판정은 어댑터 이름/설명에 제외 문자열이 들어 있는지로 하는 휴리스틱이라, 환경에 따라 맞지 않으면 excludeTokens를 지정한다.
/// </summary>
public static class LocalAddress
{
    /// <summary>기본 제외 문자열(대소문자 무시): 도커, VMware, 가상 어댑터 일반, Parallels, 의사 인터페이스.</summary>
    public static readonly string[] DefaultExcludeTokens = { "docker", "vmware", "virtual", "parallels", "pseudo" };

    /// <summary>사용할 만한 IPv4 주소를 어댑터 순서대로 돌려준다. 어댑터 정보를 읽지 못하면 빈 목록이다(예외를 던지지 않는다).</summary>
    /// <param name="excludeTokens">어댑터 이름/설명에 포함되면 건너뛸 문자열. null이면 <see cref="DefaultExcludeTokens"/>, 빈 배열이면 가상 어댑터도 포함한다.</param>
    public static List<string> GetIPv4(string[]? excludeTokens = null)
    {
        string[] tokens = excludeTokens ?? DefaultExcludeTokens;
        List<string> result = new List<string>();

        NetworkInterface[] adapters;
        try { adapters = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (NetworkInterface adapter in adapters)
        {
            try
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (Matches(adapter.Description, tokens) || Matches(adapter.Name, tokens))
                    continue;

                foreach (UnicastIPAddressInformation info in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    byte[] b = info.Address.GetAddressBytes();
                    if (b[0] == 127 || (b[0] == 169 && b[1] == 254))
                        continue;
                    result.Add(info.Address.ToString());
                }
            }
            catch (NetworkInformationException) { }
        }
        return result;
    }

    /// <summary>첫 번째 사용할 만한 IPv4 주소. 없으면 false.</summary>
    public static bool TryGetFirstIPv4(out string address, string[]? excludeTokens = null)
    {
        List<string> all = GetIPv4(excludeTokens);
        address = all.Count != 0 ? all[0] : string.Empty;
        return all.Count != 0;
    }

    private static bool Matches(string? text, string[] tokens)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (string t in tokens)
            if (text.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}

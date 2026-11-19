// CommandLine.cs : 간단한 명령줄 인자 파서.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Core.Util;

/// <summary>
/// <c>--name value</c>, <c>--name=value</c>, <c>--flag</c>, <c>-f</c> 형태와 위치 인자를 분리해 담는다. 이름은 대소문자를 구분하지 않는다.
/// <c>--</c> 뒤는 전부 위치 인자다. 값이 음수인 <c>--x -5</c>도 값으로 읽는다(다음 토큰이 숫자 모양이면 옵션이 아니라 값).
/// 값을 받는 옵션과 플래그를 구분하려고 파서가 미리 알 필요는 없다: 다음 토큰이 옵션 모양이 아니면 값으로 본다.
/// 존재하지 않는 옵션 검사는 <see cref="Unknown"/>으로 한다.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = new List<string>();
    private readonly HashSet<string> _queried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Positional => _positional;

    /// <param name="args">명령줄 인자</param>
    /// <param name="flagNames">값을 받지 않는 플래그 이름들. 스키마 없이는 <c>--verbose file.txt</c>의 file.txt가 verbose의 값으로 읽히므로,
    /// 플래그는 여기에 선언해 두면 뒤의 토큰을 위치 인자로 남긴다.</param>
    public CommandLine(IEnumerable<string> args, IEnumerable<string>? flagNames = null)
    {
        var flags = new HashSet<string>(flagNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        string[] a = new List<string>(args).ToArray();
        for (int i = 0; i < a.Length; ++i)
        {
            string t = a[i];
            if (t == "--")
            {
                for (++i; i < a.Length; ++i) _positional.Add(a[i]);
                break;
            }
            if (t.StartsWith("--", StringComparison.Ordinal) && t.Length > 2)
            {
                string body = t.Substring(2);
                int eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    _options[body.Substring(0, eq)] = body.Substring(eq + 1);
                }
                else if (!flags.Contains(body) && i + 1 < a.Length && !LooksLikeOption(a[i + 1]))
                {
                    _options[body] = a[++i];
                }
                else
                {
                    _options[body] = null;
                }
            }
            else if (t.Length == 2 && t[0] == '-' && char.IsLetter(t[1]))
            {
                string name = t.Substring(1);
                if (!flags.Contains(name) && i + 1 < a.Length && !LooksLikeOption(a[i + 1]))
                    _options[name] = a[++i];
                else
                    _options[name] = null;
            }
            else
            {
                _positional.Add(t);
            }
        }
    }

    private static bool LooksLikeOption(string s)
    {
        if (s.Length >= 2 && s[0] == '-' && s[1] == '-') return true;
        if (s.Length == 2 && s[0] == '-' && char.IsLetter(s[1])) return true;
        return false;      // "-5" 같은 음수는 값
    }

    /// <summary>옵션이 주어졌는가(값이 없는 플래그 포함).</summary>
    public bool Has(string name)
    {
        _queried.Add(name);
        return _options.ContainsKey(name);
    }

    /// <summary>값 문자열. 없거나 플래그뿐이면 null.</summary>
    public string? GetString(string name)
    {
        _queried.Add(name);
        return _options.TryGetValue(name, out string? v) ? v : null;
    }

    public string GetString(string name, string fallback) => GetString(name) ?? fallback;

    /// <summary>정수 값. 없으면 fallback, 숫자가 아니면 예외(오타를 조용히 기본값으로 넘기지 않는다).</summary>
    public int GetInt(string name, int fallback)
    {
        string? s = GetString(name);
        if (s == null) { RequireValueIfPresent(name); return fallback; }
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            throw new FormatException("--" + name + ": 정수가 아니다: " + s);
        return v;
    }

    public double GetDouble(string name, double fallback)
    {
        string? s = GetString(name);
        if (s == null) { RequireValueIfPresent(name); return fallback; }
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            throw new FormatException("--" + name + ": 숫자가 아니다: " + s);
        return v;
    }

    // "--port"만 주고 값이 빠진 경우를 조용히 기본값으로 넘기지 않는다.
    private void RequireValueIfPresent(string name)
    {
        if (_options.ContainsKey(name))
            throw new FormatException("--" + name + ": 값이 없다");
    }

    /// <summary>플래그. <c>--x</c>, <c>--x=true</c>, <c>--x=1</c>은 true, <c>--x=false</c>, <c>--x=0</c>은 false, 없으면 fallback.</summary>
    public bool GetBool(string name, bool fallback = false)
    {
        if (!Has(name)) return fallback;
        string? s = GetString(name);
        if (s == null) return true;
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        throw new FormatException("--" + name + ": 불리언이 아니다: " + s);
    }

    /// <summary>지금까지 Has/Get으로 묻지 않은 옵션 이름들(모든 옵션을 읽은 뒤 불러 오타를 찾는다).</summary>
    public IReadOnlyList<string> Unknown()
    {
        var r = new List<string>();
        foreach (string k in _options.Keys)
            if (!_queried.Contains(k))
                r.Add(k);
        return r;
    }
}

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Core.Util;

/// <summary>문자열 다루기: 유니코드 공백 제거, 오류 메시지용 인용, JSON 문자열 이스케이프, 로캘과 무관한 숫자 읽기.</summary>
public static class TextUtil
{
    // 공백으로 보는 문자: ASCII 공백류, NBSP, 전각 공백, BOM
    private static bool IsBlankChar(char c) => AsciiChar.IsSpace(c) || c == ' ' || c == '　' || c == '﻿';

    private static readonly Regex DecimalRe = new Regex(@"^-?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][+-]?[0-9]+)?\z", RegexOptions.CultureInvariant);
    private static readonly Regex SpecialRe = new Regex(@"^-?(?:inf|infinity|nan)\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// 십진/지수 표기를 로캘과 무관하게 읽습니다. 16진수와 앞뒤 공백은 받지 않고, '+' 부호는 하나만 허용한다.
    /// 범위를 벗어나면 ±무한대(너무 큼) 또는 0(너무 작음)을 돌려주고 true이므로 호출자가 유한성을 따로 본다.
    /// </summary>
    /// <returns>문자열 전체가 숫자 표기이면 true. "0x10", "5abc", ""는 false. inf/nan은 읽히므로 호출자가 거른다</returns>
    public static bool ParseDouble(string t, out double value)
    {
        value = 0;
        int start = 0;
        if (t.Length > 0 && t[0] == '+') start = 1;
        if (start == t.Length || t[start] == '+' || (start != 0 && t[start] == '-')) return false;   // 부호는 하나만 ("+-5", "++5" 거부)

        string body = t.Substring(start);
        if (DecimalRe.IsMatch(body))
        {
            value = double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }
        if (SpecialRe.IsMatch(body))
        {
            bool neg = body[0] == '-';
            value = body.EndsWith("nan", StringComparison.OrdinalIgnoreCase) ? double.NaN : (neg ? double.NegativeInfinity : double.PositiveInfinity);
            return true;
        }
        return false;
    }

    /// <summary>오류 메시지에 값을 인용할 때 쓰는 따옴표 표기입니다. ('abc', "it's")</summary>
    public static string Quote(string s)
    {
        char q = (s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0) ? '"' : '\'';
        var sb = new StringBuilder();
        sb.Append(q);
        foreach (char ch in s)
        {
            if (ch == q || ch == '\\') { sb.Append('\\').Append(ch); }
            else if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < 0x20 || ch == 0x7F) sb.Append("\\x").Append(((int)ch).ToString("X2", CultureInfo.InvariantCulture));
            else if (ch == ' ' || ch == '　' || ch == '﻿')
            {
                // 눈에 안 보이는 공백(NBSP 등)은 코드 값으로 보여 준다. 오류 메시지에서 원인을 찾을 수 있게 한다.
                sb.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
            }
            else sb.Append(ch);
        }
        return sb.Append(q).ToString();
    }

    /// <summary>앞뒤 공백(ASCII 공백류, NBSP, 전각 공백, BOM)을 제거합니다.</summary>
    public static string Trim(string s)
    {
        int b = 0, e = s.Length;
        while (b < e && IsBlankChar(s[b])) ++b;
        while (e > b && IsBlankChar(s[e - 1])) --e;
        return s.Substring(b, e - b);
    }

    /// <summary>JSON 문자열 리터럴(따옴표 포함)을 만듭니다. 비ASCII는 그대로 두고 제어 문자만 이스케이프합니다.</summary>
    public static string JsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}

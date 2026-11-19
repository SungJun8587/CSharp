using System.Text;

namespace Core.Util;

/// <summary>로캘과 무관한 ASCII 문자 처리. (char.IsUpper 등은 비ASCII 문자도 바꾸므로 쓰지 않는다)</summary>
public static class AsciiChar
{
    public static bool IsUpper(char c) => c >= 'A' && c <= 'Z';
    public static bool IsLower(char c) => c >= 'a' && c <= 'z';
    public static bool IsDigit(char c) => c >= '0' && c <= '9';
    public static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');
    public static char ToUpper(char c) => IsLower(c) ? (char)(c - 'a' + 'A') : c;
    public static char ToLower(char c) => IsUpper(c) ? (char)(c - 'A' + 'a') : c;

    public static string Lower(string s)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; ++i)
        {
            if (!IsUpper(s[i])) continue;
            sb ??= new StringBuilder(s);
            sb[i] = ToLower(s[i]);
        }
        return sb == null ? s : sb.ToString();
    }
}

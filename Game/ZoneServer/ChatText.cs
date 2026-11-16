// ChatText.cs : 채팅 본문 검증. 상태가 없는 순수 함수라 존 스레드 밖에서도 부를 수 있다.
using System.Globalization;
using System.Text;

namespace Game.ZoneServer;

public static class ChatText
{
    /// <summary>
    /// 본문을 다듬고 검사한다. 앞뒤 공백을 자르고, 비었거나 다음이 하나라도 있으면 거부한다.
    /// 제어 문자(줄바꿈 포함), 서식 문자(제로 폭 공백/방향 전환 등 화면을 속이는 문자), 짝이 안 맞는 서로게이트,
    /// 잘못된 UTF-8이 디코딩되며 생긴 대체 문자(U+FFFD).
    /// </summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrEmpty(text))
            return false;

        string t = text.Trim();
        if (t.Length == 0)
            return false;

        foreach (Rune r in t.EnumerateRunes())
        {
            if (r == Rune.ReplacementChar)
                return false;

            UnicodeCategory cat = Rune.GetUnicodeCategory(r);
            if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format || cat == UnicodeCategory.LineSeparator ||
                cat == UnicodeCategory.ParagraphSeparator || cat == UnicodeCategory.OtherNotAssigned)
                return false;
        }

        normalized = t;
        return true;
    }
}

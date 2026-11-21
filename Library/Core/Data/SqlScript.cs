// SqlScript.cs : 마이그레이션 스크립트 파일 이름 해석, 문장 나누기, 체크섬(모두 DB 없이 시험할 수 있다).
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Core.Data;

public static class SqlScript
{
    /// <summary>"003_player_currency.sql" -> (3, "player_currency"). 형식이 다르면 false. 버전은 파일 이름 앞의 숫자다.</summary>
    public static bool TryParseFileName(string fileName, out int version, out string name)
    {
        version = 0;
        name = "";
        if (!fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            return false;
        string stem = fileName.Substring(0, fileName.Length - 4);
        int us = stem.IndexOf('_');
        if (us < 1 || !int.TryParse(stem.AsSpan(0, us), out version) || version < 1)
            return false;
        name = stem.Substring(us + 1);
        return name.Length != 0;
    }

    /// <summary>BOM 과 줄바꿈 차이(CRLF/LF)를 무시한 SHA-256(소문자 16진). 편집기가 바꾼 줄바꿈 때문에 다른 파일로 보이지 않게 한다.</summary>
    public static string Checksum(string text)
    {
        string norm = text.TrimStart('﻿').Replace("\r\n", "\n");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 스크립트를 ';' 기준 문장으로 나눈다. 문자열('..', "..")과 `식별자` 안의 ';' 는 무시하고, 주석(-- , #, /* */)은 지운다(서버가 해석하는 /*! */ 와 /*+ */ 는 남긴다).
    /// DELIMITER 명령은 지원하지 않는다(저장 프로시저는 쓰지 않는다). 빈 문장은 건너뛴다.
    /// </summary>
    public static List<string> Split(string text)
    {
        List<string> result = new List<string>();
        StringBuilder cur = new StringBuilder();
        int i = 0, n = text.Length;
        while (i < n)
        {
            char c = text[i];
            if (c == '\'' || c == '"' || c == '`')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (text[j] == '\\' && c != '`' && j + 1 < n) { j += 2; continue; }
                    if (text[j] == c)
                    {
                        if (j + 1 < n && text[j + 1] == c) { j += 2; continue; }   // '' 로 쓴 따옴표
                        break;
                    }
                    ++j;
                }
                int end = Math.Min(j + 1, n);
                cur.Append(text, i, end - i);
                i = end;
            }
            else if ((c == '-' && i + 2 < n && text[i + 1] == '-' && (text[i + 2] == ' ' || text[i + 2] == '\t' || text[i + 2] == '\n' || text[i + 2] == '\r')) ||
                     (c == '-' && i + 2 == n && text[i + 1] == '-') || c == '#')
            {
                while (i < n && text[i] != '\n') ++i;
            }
            else if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int e = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int end = e < 0 ? n : e + 2;
                if (i + 2 < n && (text[i + 2] == '!' || text[i + 2] == '+'))
                    cur.Append(text, i, end - i);       // /*! ... */ (조건부 실행)와 /*+ ... */ (옵티마이저 힌트)는 서버가 해석하므로 그대로 둔다
                else
                    cur.Append(' ');
                i = end;
            }
            else if (c == ';')
            {
                Flush(cur, result);
                ++i;
            }
            else
            {
                cur.Append(c);
                ++i;
            }
        }
        Flush(cur, result);
        return result;
    }

    private static void Flush(StringBuilder cur, List<string> result)
    {
        string s = cur.ToString().Trim();
        cur.Clear();
        if (s.Length != 0)
            result.Add(s);
    }
}

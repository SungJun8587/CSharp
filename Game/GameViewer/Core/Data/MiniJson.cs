//***************************************************************************
// MiniJson.cs : 의존성 없는 최소 JSON 파서 (Unity/.NET 어디서나 동작).
// 결과 타입: Dictionary<string,object>, List<object>, string, double, bool, null
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameViewer.Core.Data
{
    public sealed class JsonException : Exception
    {
        public JsonException(string msg) : base(msg) { }
    }

    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("null text");
            int p = 0;
            SkipWs(text, ref p);
            object v = ReadValue(text, ref p);
            SkipWs(text, ref p);
            if (p != text.Length) throw new JsonException("trailing characters at " + p);
            return v;
        }

        public static Dictionary<string, object> AsObject(object v) { return v as Dictionary<string, object>; }
        public static List<object> AsArray(object v) { return v as List<object>; }

        public static string Str(Dictionary<string, object> o, string key, string def)
        {
            object v; return o.TryGetValue(key, out v) && v is string ? (string)v : def;
        }
        public static double Num(Dictionary<string, object> o, string key, double def)
        {
            object v; return o.TryGetValue(key, out v) && v is double ? (double)v : def;
        }
        public static bool Bool(Dictionary<string, object> o, string key, bool def)
        {
            object v; return o.TryGetValue(key, out v) && v is bool ? (bool)v : def;
        }

        private static void SkipWs(string s, ref int p)
        {
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n' || s[p] == '﻿')) ++p;
        }

        private static object ReadValue(string s, ref int p)
        {
            if (p >= s.Length) throw new JsonException("unexpected end");
            char c = s[p];
            if (c == '{') return ReadObject(s, ref p);
            if (c == '[') return ReadArray(s, ref p);
            if (c == '"') return ReadString(s, ref p);
            if (c == 't') { Expect(s, ref p, "true"); return true; }
            if (c == 'f') { Expect(s, ref p, "false"); return false; }
            if (c == 'n') { Expect(s, ref p, "null"); return null; }
            return ReadNumber(s, ref p);
        }

        private static void Expect(string s, ref int p, string word)
        {
            if (string.CompareOrdinal(s, p, word, 0, word.Length) != 0) throw new JsonException("bad literal at " + p);
            p += word.Length;
        }

        private static object ReadNumber(string s, ref int p)
        {
            int st = p;
            while (p < s.Length && "+-0123456789.eE".IndexOf(s[p]) >= 0) ++p;
            double d;
            if (p == st || !double.TryParse(s.Substring(st, p - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new JsonException("bad number at " + st);
            return d;
        }

        private static string ReadString(string s, ref int p)
        {
            ++p; // opening quote
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (p >= s.Length) throw new JsonException("unterminated string");
                char c = s[p++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (p >= s.Length) throw new JsonException("bad escape");
                char e = s[p++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (p + 4 > s.Length) throw new JsonException("bad \\u escape");
                        sb.Append((char)Convert.ToInt32(s.Substring(p, 4), 16));
                        p += 4;
                        break;
                    default: throw new JsonException("bad escape \\" + e);
                }
            }
            return sb.ToString();
        }

        private static List<object> ReadArray(string s, ref int p)
        {
            ++p;
            List<object> list = new List<object>();
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == ']') { ++p; return list; }
            while (true)
            {
                SkipWs(s, ref p);
                list.Add(ReadValue(s, ref p));
                SkipWs(s, ref p);
                if (p >= s.Length) throw new JsonException("unterminated array");
                if (s[p] == ',') { ++p; continue; }
                if (s[p] == ']') { ++p; return list; }
                throw new JsonException("expected , or ] at " + p);
            }
        }

        private static Dictionary<string, object> ReadObject(string s, ref int p)
        {
            ++p;
            Dictionary<string, object> o = new Dictionary<string, object>();
            SkipWs(s, ref p);
            if (p < s.Length && s[p] == '}') { ++p; return o; }
            while (true)
            {
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != '"') throw new JsonException("expected key at " + p);
                string key = ReadString(s, ref p);
                SkipWs(s, ref p);
                if (p >= s.Length || s[p] != ':') throw new JsonException("expected : at " + p);
                ++p;
                SkipWs(s, ref p);
                o[key] = ReadValue(s, ref p);
                SkipWs(s, ref p);
                if (p >= s.Length) throw new JsonException("unterminated object");
                if (s[p] == ',') { ++p; continue; }
                if (s[p] == '}') { ++p; return o; }
                throw new JsonException("expected , or } at " + p);
            }
        }
    }
}

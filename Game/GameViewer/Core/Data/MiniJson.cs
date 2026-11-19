//***************************************************************************
// MiniJson.cs : JSON 텍스트를 느슨한 객체 트리로 읽는다(System.Text.Json 기반).
// 결과 타입: Dictionary<string,object>, List<object>, string, double, bool, null
// 표준 JSON 만 받는다(주석/후행 쉼표는 오류). 문법 오류는 GameViewer.Core.Data.JsonException 으로 알린다.
//***************************************************************************
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GameViewer.Core.Data
{
    public sealed class JsonException : Exception
    {
        public JsonException(string msg) : base(msg) { }
    }

    public static class MiniJson
    {
        private static readonly JsonDocumentOptions Options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };

        public static object Parse(string text)
        {
            if (text == null) throw new JsonException("null text");
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);       // 문자열로 읽은 파일에 남은 BOM
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(text, Options))
                    return Convert(doc.RootElement);
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new JsonException(e.Message);
            }
            catch (FormatException e)
            {
                throw new JsonException(e.Message);       // double 로 표현할 수 없는 숫자
            }
        }

        private static object Convert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    foreach (JsonProperty p in e.EnumerateObject()) o[p.Name] = Convert(p.Value);
                    return o;
                }
                case JsonValueKind.Array:
                {
                    List<object> l = new List<object>(e.GetArrayLength());
                    foreach (JsonElement it in e.EnumerateArray()) l.Add(Convert(it));
                    return l;
                }
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Number: return e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                default: return null;
            }
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
    }
}

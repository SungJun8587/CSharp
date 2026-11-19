// JsonTableLoader.cs : 엄격한 JSON 읽기 도우미 (XlsxConverter가 만든 {"키":[...]} 형태를 Table 클래스로 읽는다)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Util;

/// <summary>
/// XlsxConverter가 만든 JSON({"Skill":[...]} 형태)을 Table 클래스(TTable)로 읽는다. 게임 데이터 타입에는 의존하지 않는다.
/// 모르는 키, 형식이 다른 값(문자열 자리에 숫자 등), 범위를 벗어난 정수, 알 수 없는 enum 이름은 모두 오류로 취급해
/// 서버가 잘못된 데이터로 뜨지 않게 한다. 값 범위 검사는 각 Table.Validate가 맡는다.
/// </summary>
public static class JsonTableLoader
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions o = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };
        // 정수 값으로 쓴 enum은 허용하지 않는다(이름만).
        o.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return o;
    }

    /// <summary>JSON 텍스트를 Table 클래스로 읽는다.</summary>
    /// <param name="json">읽을 JSON 텍스트</param>
    /// <param name="what">오류 메시지에 쓸 이름(파일 경로 등)</param>
    /// <param name="table">[out] 읽은 테이블. 실패하면 null</param>
    /// <param name="error">[out] 실패 사유</param>
    public static bool TryParse<TTable>(string json, string what, out TTable? table, out string error) where TTable : class
    {
        table = null;
        error = string.Empty;
        try
        {
            table = JsonSerializer.Deserialize<TTable>(json, s_options);
            if (table == null)
            {
                error = what + ": 최상위 값이 비어 있습니다";
                return false;
            }
            return true;
        }
        catch (JsonException e)
        {
            error = what + ": " + e.Message;
            return false;
        }
        catch (NotSupportedException e)
        {
            error = what + ": " + e.Message;
            return false;
        }
    }

    /// <summary>파일을 읽어 Table 클래스로 변환한다. 파일이 없거나 형식이 틀리면 false.</summary>
    public static bool TryLoadFile<TTable>(string path, out TTable? table, out string error) where TTable : class
    {
        table = null;
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            error = path + ": 읽을 수 없습니다 (" + e.Message + ")";
            return false;
        }

        return TryParse(text, path, out table, out error);
    }
}
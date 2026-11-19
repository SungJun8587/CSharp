// ConfigLoader.cs : 엄격한 JSON 설정 로더.
using System.IO;
using System.Text.Json;

namespace Core.Util;

/// <summary>
/// JSON 설정 파일을 T로 읽는다. 주석과 후행 쉼표를 허용하고 속성 이름은 대소문자를 구분하지 않는다. 알 수 없는 속성은 오류로 거절한다(오타 방지).
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions s_options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        IncludeFields = true,
    };

    public static T Load<T>(string path) where T : class
    {
        string json = File.ReadAllText(path);
        return Parse<T>(json, path);
    }

    public static T Parse<T>(string json, string sourceName = "<memory>") where T : class
    {
        try
        {
            T? v = JsonSerializer.Deserialize<T>(json, s_options);
            return v ?? throw new InvalidDataException(sourceName + ": 비어 있는 설정");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(sourceName + ": " + ex.Message, ex);
        }
    }

    /// <summary>파일이 없으면 기본값을 만든다(false: 없어서 기본값).</summary>
    public static bool TryLoad<T>(string path, out T value) where T : class, new()
    {
        if (!File.Exists(path))
        {
            value = new T();
            return false;
        }
        value = Load<T>(path);
        return true;
    }
}

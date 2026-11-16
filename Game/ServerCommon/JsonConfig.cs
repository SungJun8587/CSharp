// JsonConfig.cs : 서버 설정 파일(JSON) 읽기/쓰기 도우미.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Util;

namespace Game.ServerCommon;

/// <summary>접속할/접속받을 서버 노드 한 개 (설정 파일의 ServerNode 배열 원소).</summary>
public sealed class ServerNode
{
    public int Id;
    public string Name = string.Empty;
    public string Ip = string.Empty;
    public uint Port;
}

/// <summary>
/// 최상위가 객체인 JSON 설정 파일을 읽는다. 읽은 키를 기록해 두었다가 Validate()에서 모르는 키를 경고하고,
/// 형식/범위 오류는 로그로 남긴 뒤 Validate()가 false를 돌려주게 한다. 키가 없으면 인자로 넘긴 기본값을 유지한다.
/// </summary>
public sealed class JsonConfigReader
{
    private readonly JsonElement _root;
    private readonly string _path;
    private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);
    private bool _ok = true;

    private JsonConfigReader(JsonElement root, string path)
    {
        _root = root;
        _path = path;
    }

    /// <summary>파일을 읽는다. 열 수 없거나 문법이 틀리거나 최상위가 객체가 아니면 null (원인은 로그).</summary>
    public static JsonConfigReader? Load(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                LogManager.Error("[Config] top-level value must be an object. path={0}", path);
                return null;
            }
            return new JsonConfigReader(doc.RootElement.Clone(), path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
        {
            LogManager.Error("[Config] load failed. path={0}, reason={1}", path, e.Message);
            return null;
        }
    }

    /// <summary>이 서버에 해당 개념이 없어 읽지 않는 키. 있어도 경고하지 않는다.</summary>
    public void Ignore(params string[] keys)
    {
        foreach (string k in keys)
            _known.Add(k);
    }

    private bool TryGet(string key, out JsonElement value)
    {
        _known.Add(key);
        return _root.TryGetProperty(key, out value);
    }

    private void Fail(string key, string why)
    {
        LogManager.Error("[Config] invalid value. path={0}, key={1}, {2}", _path, key, why);
        _ok = false;
    }

    public void ReadString(string key, ref string value)
    {
        if (!TryGet(key, out JsonElement e))
            return;
        if (e.ValueKind != JsonValueKind.String)
        {
            Fail(key, "string expected");
            return;
        }
        value = e.GetString() ?? string.Empty;
    }

    public void ReadBool(string key, ref bool value)
    {
        if (!TryGet(key, out JsonElement e))
            return;
        if (e.ValueKind != JsonValueKind.True && e.ValueKind != JsonValueKind.False)
        {
            Fail(key, "bool expected");
            return;
        }
        value = e.GetBoolean();
    }

    public void ReadUInt(string key, ref uint value, uint min, uint max)
    {
        if (!TryGet(key, out JsonElement e))
            return;
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetUInt32(out uint v))
        {
            Fail(key, "unsigned integer expected");
            return;
        }
        if (v < min || v > max)
        {
            Fail(key, $"out of range [{min}..{max}], value={v}");
            return;
        }
        value = v;
    }

    public void ReadInt(string key, ref int value, int min, int max)
    {
        if (!TryGet(key, out JsonElement e))
            return;
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v))
        {
            Fail(key, "integer expected");
            return;
        }
        if (v < min || v > max)
        {
            Fail(key, $"out of range [{min}..{max}], value={v}");
            return;
        }
        value = v;
    }

    public void ReadFloat(string key, ref float value)
    {
        if (!TryGet(key, out JsonElement e))
            return;
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetSingle(out float v) || !float.IsFinite(v))
        {
            Fail(key, "number expected");
            return;
        }
        value = v;
    }

    /// <summary>ServerNode 배열을 읽는다. 원소는 {ID, Name, IP, Port} 객체여야 한다.</summary>
    public void ReadServerNodes(string key, List<ServerNode> nodes)
    {
        if (!TryGet(key, out JsonElement arr))
            return;
        if (arr.ValueKind != JsonValueKind.Array)
        {
            Fail(key, "array expected");
            return;
        }

        List<ServerNode> read = new List<ServerNode>();
        int index = 0;
        foreach (JsonElement e in arr.EnumerateArray())
        {
            string where = key + "[" + index++ + "]";
            if (e.ValueKind != JsonValueKind.Object)
            {
                Fail(where, "object expected");
                return;
            }

            ServerNode n = new ServerNode();
            bool hasId = false, hasIp = false, hasPort = false;
            foreach (JsonProperty p in e.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "ID":
                        hasId = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out n.Id) && n.Id >= 0 && n.Id <= short.MaxValue;
                        break;
                    case "Name":
                        if (p.Value.ValueKind == JsonValueKind.String) n.Name = p.Value.GetString() ?? string.Empty;
                        break;
                    case "IP":
                        hasIp = p.Value.ValueKind == JsonValueKind.String;
                        if (hasIp) n.Ip = p.Value.GetString() ?? string.Empty;
                        break;
                    case "Port":
                        hasPort = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetUInt32(out n.Port) && n.Port <= 65535;
                        break;
                    default:
                        LogManager.Warning("[Config] unknown key ignored. path={0}, key={1}.{2}", _path, where, p.Name);
                        break;
                }
            }

            if (!hasId || !hasIp || !hasPort)
            {
                Fail(where, "ID(0..32767), IP(string), Port(0..65535) are required");
                return;
            }
            read.Add(n);
        }

        nodes.Clear();
        nodes.AddRange(read);
    }

    /// <summary>읽는 중 오류가 없었으면 true. 모르는 키는 경고만 남긴다(이전 버전 키 등).</summary>
    public bool Validate()
    {
        foreach (JsonProperty p in _root.EnumerateObject())
        {
            if (!_known.Contains(p.Name))
                LogManager.Warning("[Config] unknown key ignored. path={0}, key={1}", _path, p.Name);
        }
        return _ok;
    }
}

/// <summary>
/// 설정 파일을 쓴다. 기존 파일이 있으면 먼저 읽어 와서 모르는 키와 키 순서를 보존한 채 알고 있는 필드의 값만 교체한다.
/// 기존 파일을 읽을 수 없으면(문법 오류 등) 덮어쓰지 않는다.
/// </summary>
public sealed class JsonConfigWriter
{
    private readonly JsonObject _root;

    private JsonConfigWriter(JsonObject root) { _root = root; }

    public static JsonConfigWriter? Open(string path)
    {
        if (!File.Exists(path))
            return new JsonConfigWriter(new JsonObject());

        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            if (node is JsonObject obj)
                return new JsonConfigWriter(obj);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
        {
            LogManager.Error("[Config] existing file is not readable; not overwritten. path={0}, reason={1}", path, e.Message);
            return null;
        }

        LogManager.Error("[Config] existing file is not a valid config; not overwritten. path={0}", path);
        return null;
    }

    public void Write(string key, string value) => _root[key] = value;
    public void Write(string key, bool value) => _root[key] = value;
    public void Write(string key, uint value) => _root[key] = value;
    public void Write(string key, int value) => _root[key] = value;
    public void Write(string key, float value) => _root[key] = JsonNode.Parse(value.ToString("R", CultureInfo.InvariantCulture));

    public void WriteServerNodes(string key, IReadOnlyList<ServerNode> nodes)
    {
        JsonArray arr = new JsonArray();
        foreach (ServerNode n in nodes)
            arr.Add(new JsonObject { ["ID"] = n.Id, ["Name"] = n.Name, ["IP"] = n.Ip, ["Port"] = n.Port });
        _root[key] = arr;
    }

    public bool Save(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            LogManager.Error("[Config] save failed. path={0}, reason={1}", path, e.Message);
            return false;
        }
    }
}

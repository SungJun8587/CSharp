// MigrationScripts.cs : 마이그레이션 스크립트 목록을 만든다(폴더 또는 어셈블리에 내장된 리소스). DB 에 닿지 않으므로 시험할 수 있다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Core.Data;

/// <summary>버전 하나의 스크립트: 번호, 이름, 본문, 체크섬.</summary>
public sealed class MigrationScript
{
    public int Version { get; }
    public string Name { get; }
    public string Text { get; }
    public string Checksum { get; }

    public MigrationScript(int version, string name, string text)
    {
        Version = version;
        Name = name;
        Text = text;
        Checksum = SqlScript.Checksum(text);
    }
}

public static class MigrationScripts
{
    /// <summary>폴더의 NNN_이름.sql 을 읽는다. 이름 형식이 다른 .sql 은 건너뛰고, 같은 버전이 둘이면 예외다. 버전 순으로 돌려준다.</summary>
    public static List<MigrationScript> FromDirectory(string dir)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException("schema directory not found: " + dir);
        List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
        foreach (string path in Directory.GetFiles(dir, "*.sql"))
            files.Add(new KeyValuePair<string, string>(Path.GetFileName(path), File.ReadAllText(path)));
        return Build(files);
    }

    /// <summary>
    /// 어셈블리에 내장된 리소스(csproj 의 &lt;EmbeddedResource Include="Schema/*.sql" /&gt;)에서 읽는다. resourcePrefix 로 시작하는 .sql 리소스만 대상이고
    /// 접두어를 뗀 나머지가 파일 이름(NNN_이름.sql)이다. 예) typeof(X).Assembly, "Game.Persistence.Schema.". 실행 파일과 함께 스크립트 폴더를 배포하지 않아도 된다.
    /// </summary>
    public static List<MigrationScript> FromAssembly(Assembly assembly, string resourcePrefix)
    {
        List<KeyValuePair<string, string>> files = new List<KeyValuePair<string, string>>();
        foreach (string res in assembly.GetManifestResourceNames())
        {
            if (!res.StartsWith(resourcePrefix, StringComparison.Ordinal) || !res.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;
            using Stream? st = assembly.GetManifestResourceStream(res);
            if (st == null)
                continue;
            using StreamReader rd = new StreamReader(st, detectEncodingFromByteOrderMarks: true);
            files.Add(new KeyValuePair<string, string>(res.Substring(resourcePrefix.Length), rd.ReadToEnd()));
        }
        return Build(files);
    }

    /// <summary>(파일 이름, 본문) 목록에서 스크립트를 만든다(순수 함수).</summary>
    public static List<MigrationScript> Build(IEnumerable<KeyValuePair<string, string>> files)
    {
        SortedDictionary<int, MigrationScript> byVersion = new SortedDictionary<int, MigrationScript>();
        foreach (KeyValuePair<string, string> f in files)
        {
            if (!SqlScript.TryParseFileName(f.Key, out int version, out string name))
                continue;
            if (byVersion.ContainsKey(version))
                throw new InvalidOperationException("duplicate migration version " + version + ": " + f.Key);
            byVersion.Add(version, new MigrationScript(version, name, f.Value));
        }
        return new List<MigrationScript>(byVersion.Values);
    }
}

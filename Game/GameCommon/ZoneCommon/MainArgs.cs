// MainArgs.cs : 명령행 인자 파서와 서버 진입점 공용 도우미.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Core.Util;

namespace Game.ZoneCommon;

/// <summary>
/// "--key=value" 또는 "--key value" 형식의 명령행 인자. 값이 없으면 "1"(플래그)로 본다.
/// ReadXxx는 키가 없으면 true(값 유지), 있는데 형식/범위가 틀리면 로그를 남기고 false를 돌려준다.
/// </summary>
public sealed class MainArgs
{
    private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

    public MainArgs(string[] args)
    {
        for (int i = 0; i < args.Length; ++i)
        {
            string a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal) || a.Length <= 2)
                continue;

            string body = a.Substring(2);
            int eq = body.IndexOf('=');
            if (eq >= 0)
                _values[body.Substring(0, eq)] = body.Substring(eq + 1);
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                _values[body] = args[++i];
            else
                _values[body] = "1";
        }
    }

    public string GetString(string key, string defaultValue) => _values.TryGetValue(key, out string? v) ? v : defaultValue;

    public bool ReadString(string key, ref string value)
    {
        if (_values.TryGetValue(key, out string? v))
            value = v;
        return true;
    }

    public bool ReadUInt(string key, ref uint value, uint min, uint max)
    {
        if (!_values.TryGetValue(key, out string? s))
            return true;
        if (!uint.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out uint v) || v < min || v > max)
        {
            LogManager.Error("[Args] invalid value. --{0}={1} (range {2}..{3})", key, s, min, max);
            return false;
        }
        value = v;
        return true;
    }

    public bool ReadInt(string key, ref int value, int min, int max)
    {
        if (!_values.TryGetValue(key, out string? s))
            return true;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || v < min || v > max)
        {
            LogManager.Error("[Args] invalid value. --{0}={1} (range {2}..{3})", key, s, min, max);
            return false;
        }
        value = v;
        return true;
    }

    public bool ReadUShort(string key, ref ushort value, ushort min, ushort max)
    {
        uint v = value;
        if (!ReadUInt(key, ref v, min, max))
            return false;
        value = (ushort)v;
        return true;
    }

    public bool ReadFloat(string key, ref float value, double min, double max)
    {
        if (!_values.TryGetValue(key, out string? s))
            return true;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v) || v < min || v > max)
        {
            LogManager.Error("[Args] invalid value. --{0}={1} (range {2}..{3})", key, s, min, max);
            return false;
        }
        value = (float)v;
        return true;
    }
}

/// <summary>설정 파일을 읽고 쓰고 서버 옵션으로 바꾸는 서버 설정 클래스의 공통 모양.</summary>
public interface IServerConfig<TOptions>
{
    bool Init(string path);
    bool Save(string path);
    void PrintInfo();
    TOptions ToOptions();
}

/// <summary>ServerMain.Run이 다루는 실행 중인 서버. Dispose는 Stop과 같다.</summary>
public interface IServerHost : IDisposable
{
    void Stop();
}

/// <summary>서버 진입점 공용 도우미: Ctrl+C 종료 플래그, --dump-config 처리, 공통 실행 흐름.</summary>
public static class ServerMain
{
    private static int s_exit;

    /// <summary>종료 요청(Ctrl+C 등)이 들어왔는지. 실제 정지(Stop)는 메인 스레드에서 한다.</summary>
    public static bool ShouldExit => Volatile.Read(ref s_exit) != 0;

    /// <summary>Ctrl+C / SIGTERM 을 종료 플래그로 연결한다.</summary>
    public static void Startup()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Volatile.Write(ref s_exit, 1);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Volatile.Write(ref s_exit, 1);
    }

    /// <summary>
    /// 서버 실행의 공통 흐름: 종료 플래그 연결, --dump-config, 설정 파일 읽기, 명령행 덮어쓰기, 시작, 종료 대기, 정지.
    /// </summary>
    /// <param name="defaultConfigFile">--config 가 없을 때 읽을 설정 파일</param>
    /// <param name="readArgs">명령행 값을 옵션에 반영한다. 형식/범위 오류가 있으면 false</param>
    /// <param name="start">서버를 만들어 시작하고 돌려준다. 실패하면 null(실패 로그는 이 함수가 남긴다)</param>
    /// <param name="pollAbort">실행 중 100ms마다 부른다. 문자열을 돌려주면 그 내용을 오류로 남기고 종료한다</param>
    /// <returns>프로세스 종료 코드</returns>
    public static int Run<TOptions>(
        string[] argv,
        string defaultConfigFile,
        Func<IServerConfig<TOptions>> newConfig,
        Func<MainArgs, TOptions, bool> readArgs,
        Func<TOptions, IServerHost?> start,
        Func<IServerHost, string?>? pollAbort = null)
    {
        Startup();
        MainArgs args = new MainArgs(argv);

        // --dump-config=경로 : 기본값 설정 파일을 만들고 종료한다(기존 파일이 있으면 알 수 없는 키를 보존한 채 값만 갱신).
        if (TryDumpConfig(args, path => newConfig().Save(path), out int dumpExit))
            return dumpExit;

        IServerConfig<TOptions> config = newConfig();
        if (!config.Init(args.GetString("config", defaultConfigFile)))
            return -1;
        config.PrintInfo();

        TOptions options = config.ToOptions();
        if (!readArgs(args, options))
            return -1;

        using IServerHost? host = start(options);
        if (host == null)
            return -1;

        while (!ShouldExit)
        {
            string? abort = pollAbort?.Invoke(host);
            if (abort != null)
            {
                LogManager.Error("{0}", abort);
                break;
            }
            Thread.Sleep(100);
        }

        host.Stop();
        return 0;
    }

    /// <summary>--dump-config=경로 가 있으면 save(경로)를 호출해 기본값 템플릿을 저장한다.</summary>
    /// <returns>--dump-config가 주어져 처리했으면 true (exitCode로 종료한다)</returns>
    public static bool TryDumpConfig(MainArgs args, Func<string, bool> save, out int exitCode)
    {
        exitCode = 0;
        string path = args.GetString("dump-config", string.Empty);
        if (path.Length == 0)
            return false;

        bool saved = path != "1" && save(path);
        if (saved)
            LogManager.Info("Default config saved. path={0}", path);
        else
            LogManager.Error("--dump-config needs a writable file path. value={0}", path);
        exitCode = saved ? 0 : -1;
        return true;
    }
}

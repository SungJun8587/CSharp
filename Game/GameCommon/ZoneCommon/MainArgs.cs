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
    private readonly HashSet<string> _used = new HashSet<string>(StringComparer.Ordinal);

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

    /// <summary>읽기 함수가 한 번이라도 조회한 키를 기록한다(오타 난 옵션을 찾아내기 위해).</summary>
    private bool TryGet(string key, out string value)
    {
        _used.Add(key);
        return _values.TryGetValue(key, out value!);
    }

    /// <summary>
    /// 주어졌지만 어떤 읽기 함수도 조회하지 않은 옵션(오타 등)을 경고로 남긴다. 옵션을 모두 읽은 뒤에 부른다.
    /// 경고만 하고 실행은 막지 않는다(서버 구동 스크립트가 쓰는 보조 인자를 깨지 않기 위해).
    /// </summary>
    /// <returns>알 수 없는 옵션 수</returns>
    public int WarnUnknown()
    {
        int n = 0;
        foreach (string key in _values.Keys)
        {
            if (_used.Contains(key))
                continue;
            ++n;
            LogManager.Warning("[Args] unknown option ignored. --{0}", key);
        }
        return n;
    }

    public string GetString(string key, string defaultValue) => TryGet(key, out string v) ? v : defaultValue;

    public bool ReadString(string key, ref string value)
    {
        if (TryGet(key, out string v))
            value = v;
        return true;
    }

    public bool ReadUInt(string key, ref uint value, uint min, uint max)
    {
        if (!TryGet(key, out string s))
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
        if (!TryGet(key, out string s))
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
        if (!TryGet(key, out string s))
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
    /// <summary>
    /// 서버 실행의 공통 흐름: --dump-config, 설정 파일 읽기, 명령행 덮어쓰기, 시작, 종료 신호 대기, 정지.
    /// 종료 신호(Ctrl+C, SIGTERM, SIGQUIT)는 ShutdownCoordinator가 받아 host.Stop()을 한 번만 실행한다.
    /// </summary>
    /// <param name="defaultConfigFile">--config 가 없을 때 읽을 설정 파일</param>
    /// <param name="readArgs">명령행 값을 옵션에 반영한다. 형식/범위 오류가 있으면 false</param>
    /// <param name="start">서버를 만들어 시작하고 돌려준다. 실패하면 null(실패 로그는 이 함수가 남긴다)</param>
    /// <param name="pollAbort">실행 중 100ms마다 부른다. 문자열을 돌려주면 그 내용을 오류로 남기고 종료한다</param>
    /// <returns>프로세스 종료 코드. 종료 신호로 끝나면 0, 서버가 pollAbort 로 중단을 요청했거나 시작에 실패하면 -1</returns>
    public static int Run<TOptions>(
        string[] argv,
        string defaultConfigFile,
        Func<IServerConfig<TOptions>> newConfig,
        Func<MainArgs, TOptions, bool> readArgs,
        Func<TOptions, IServerHost?> start,
        Func<IServerHost, string?>? pollAbort = null)
    {
        using ShutdownCoordinator shutdown = new ShutdownCoordinator(StopTimeoutMs + 5_000);
        shutdown.AttachProcessSignals();
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
        args.WarnUnknown();

        using IServerHost? host = start(options);
        if (host == null)
            return -1;

        shutdown.Register("server-stop", host.Stop, order: 0, timeoutMs: StopTimeoutMs);

        int exitCode = 0;
        while (!shutdown.IsRequested)
        {
            string? abort = pollAbort?.Invoke(host);
            if (abort != null)
            {
                LogManager.Error("{0}", abort);
                exitCode = -1;      // 서버가 스스로 중단을 요청한 경우는 실패로 끝낸다(프로세스 관리자가 정상 종료로 오인해 재시작을 멈추지 않게)
                break;
            }
            shutdown.WhenRequested.Wait(100);
        }

        foreach (HookResult r in shutdown.Run())
            if (r.Outcome != HookOutcome.Completed)
                LogManager.Error("[Shutdown] {0} {1} ({2}ms) {3}", r.Name, r.Outcome, r.ElapsedMs, r.Error?.Message ?? string.Empty);
        return exitCode;
    }

    /// <summary>host.Stop()에 주는 시간 제한. 넘으면 기다리지 않고 프로세스를 끝낸다.</summary>
    private const int StopTimeoutMs = 20_000;

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

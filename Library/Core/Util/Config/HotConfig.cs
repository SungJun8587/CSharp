// HotConfig.cs : 설정 파일 변경 감지(핫 리로드).
using System;
using System.IO;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 설정 파일을 감시하다가 바뀌면 다시 읽어 <see cref="Current"/>를 통째로 바꿔 끼운다. 읽는 쪽은 <c>Current</c>를 한 번 받아 쓰면
/// 한 번의 읽기 동안 일관된 값을 본다(필드가 섞이지 않는다). 새 파일이 잘못되면(파싱 오류) 기존 값을 유지하고 오류만 알린다.
/// 에디터가 저장할 때 변경 이벤트가 여러 번 오므로 잠깐(debounce) 기다린 뒤 한 번만 읽는다.
/// </summary>
public sealed class HotConfig<T> : IDisposable where T : class
{
    private readonly string _path;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly int _debounceMs;
    private volatile T _current;
    private int _version;
    private int _disposed;
    private readonly object _reloadLock = new object();

    /// <summary>새 설정이 적용된 뒤 불린다(타이머 스레드에서).</summary>
    public event Action<T>? Changed;
    /// <summary>다시 읽기에 실패했을 때 불린다(기존 값은 유지된다).</summary>
    public event Action<Exception>? LoadFailed;

    public HotConfig(string path, int debounceMs = 300)
    {
        _path = Path.GetFullPath(path);
        _debounceMs = debounceMs;
        _current = ConfigLoader.Load<T>(_path);     // 첫 로드 실패는 호출자에게 그대로 던진다
        _debounce = new Timer(_ => SafeReload(), null, Timeout.Infinite, Timeout.Infinite);

        string dir = Path.GetDirectoryName(_path) ?? ".";
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        _watcher.Changed += OnFsEvent;
        _watcher.Created += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.Error += (_, e) =>      // 이벤트 버퍼 넘침 등으로 변경 알림을 놓쳤을 수 있으니 한 번 다시 읽는다
        {
            LogThrottle.Warning("hotconfig.watcher-error", 10_000, "[HotConfig] watcher error: {0}", e.GetException().Message);
            OnFsEvent(this, null!);
        };
        _watcher.EnableRaisingEvents = true;
    }

    public T Current => _current;

    /// <summary>적용된 횟수(처음 로드는 0).</summary>
    public int Version => Volatile.Read(ref _version);

    // 타이머 스레드에서 예상 밖 예외가 나가면 프로세스가 종료되므로 모두 막는다(Reload는 읽기 실패를 이미 LoadFailed로 알린다).
    private void SafeReload()
    {
        try { Reload(); }
        catch (Exception ex) { LogManager.Error("[HotConfig] reload failed: {0}", ex); }
    }

    private void OnFsEvent(object? sender, FileSystemEventArgs? e)
    {
        if (Volatile.Read(ref _disposed) == 0)
            _debounce.Change(_debounceMs, Timeout.Infinite);
    }

    /// <summary>즉시 다시 읽는다(감시 없이 수동으로 부를 때). 성공하면 true.</summary>
    public bool Reload()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        T next;
        lock (_reloadLock)
        {
            try
            {
                next = ConfigLoader.Load<T>(_path);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Notify(() => LoadFailed?.Invoke(ex));
                return false;
            }
            _current = next;
            Interlocked.Increment(ref _version);
            // 알림도 락 안에서 보내 동시에 재로드돼도 구독자가 적용 순서대로 받는다(같은 스레드의 재진입은 허용된다).
            Notify(() => Changed?.Invoke(next));
        }
        return true;
    }

    // 구독자의 예외가 타이머 스레드로 새어 나가면 프로세스가 종료되므로 여기서 막는다.
    private static void Notify(Action raise)
    {
        try { raise(); }
        catch (Exception ex) { LogManager.Error("[HotConfig] subscriber failed: {0}", ex); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _watcher.Dispose();
        _debounce.Dispose();
    }
}

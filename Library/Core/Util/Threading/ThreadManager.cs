// ThreadManager.cs : 워커 스레드의 생성/보관/Join을 맡는 관리자 (C++ CThreadManager 대응).
using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Util;

/// <summary>
/// 워커 스레드의 생성, 보관, Join과 스레드 시작/종료 훅(스레드 ID 발급, 스레드 로컬 자원 정리)을 전담한다.
///
/// <list type="bullet">
/// <item>스레드 소유권과 Join 책임은 항상 이 클래스가 갖는다. 외부는 스레드 핸들을 들고 있지 않고 개수/인덱스/ID로 Join을 맡긴다.</item>
/// <item>Join처럼 블로킹되는 호출은 항상 락 밖에서 한다. 락 안에서는 목록 구조(추가/제거)만 바꾼다.</item>
/// <item><see cref="JoinThreads"/>(전체 종료)가 시작되면 새 스레드 생성을 거부한다. <see cref="JoinLastThreads"/>(부분 정리)는 이후에도 생성이 계속될 수 있는 정상 흐름이라 종료 플래그와 무관하다.</item>
/// <item>자기 자신의 스레드는 Join하지 않고 건너뛴다(자기 자신을 기다리면 영원히 끝나지 않는다).</item>
/// </list>
///
/// <para><b>주의</b>: <see cref="JoinThreadByIndex"/>의 인덱스는 제거 때문에 뒤쪽이 당겨지므로 조회 직후 한 번만 쓰는 값이다. 여러 인덱스를 모아 순서대로 부르면 안전하지 않다.</para>
/// <para><b>예외</b>: 콜백에서 난 예외는 스레드 종료 훅을 부른 뒤 다시 던져진다. 처리되지 않은 예외는 .NET이 프로세스를 종료하므로(C++의 std::terminate와 같다)
/// 콜백 안에서 직접 try/catch로 처리해야 한다. 이 클래스는 예외를 복구하지 않는다.</para>
/// <para>스레드는 전경 스레드다. 프로세스가 끝나기 전에 <see cref="JoinThreads"/>(또는 Dispose)를 반드시 부른다.</para>
/// </summary>
public sealed class ThreadManager : IDisposable
{
    private static int s_nextThreadId;
    [ThreadStatic] private static uint t_threadId;

    private readonly object _lock = new object();
    private readonly List<Thread> _threads = new List<Thread>();
    private volatile bool _shuttingDown;

    /// <summary>이 관리자가 만든 스레드 안에서 발급된 ID(1부터, 프로세스 전체에서 유일). 관리자가 만들지 않은 스레드에서는 0. (C++ <c>LThreadId</c>)</summary>
    public static uint CurrentThreadId => t_threadId;

    /// <summary>스레드가 콜백에 들어가기 직전에 그 스레드에서 호출된다. 스레드 로컬 자원 초기화용. (C++ InitTLS의 확장 지점)</summary>
    public static Action? ThreadStartHook { get; set; }

    /// <summary>콜백이 끝난 뒤(예외로 끝나도) 그 스레드에서 호출된다. 스레드 로컬 캐시 반납 등. (C++ DestroyTLS가 CMemory::FlushCurrentThreadCache를 부르던 자리)</summary>
    public static Action? ThreadExitHook { get; set; }

    /// <summary>
    /// 새 워커 스레드를 만들어 시작한다.
    /// </summary>
    /// <param name="callback">스레드에서 실행할 함수</param>
    /// <param name="name">스레드 이름(진단 도구에 보인다)</param>
    /// <returns>시작했으면 true, 전체 종료 중이라 거부했으면 false</returns>
    public bool CreateThread(Action callback, string? name = null)
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));

        lock (_lock)
        {
            if (_shuttingDown)
                return false;

            var thread = new Thread(() => ThreadMain(callback)) { Name = name, IsBackground = false };
            _threads.Add(thread);
            try
            {
                thread.Start();
            }
            catch
            {
                _threads.Remove(thread);
                throw;
            }
            return true;
        }
    }

    /// <summary>종료 절차에 들어가 보유한 모든 스레드가 끝날 때까지 기다린 뒤 목록을 비운다.</summary>
    public void JoinThreads()
    {
        Thread[] local;
        lock (_lock)
        {
            _shuttingDown = true;
            local = _threads.ToArray();
            _threads.Clear();
        }

        JoinAll(local);
    }

    /// <summary>목록 뒤에서부터 count개를 목록에서 빼 Join한다.</summary>
    public void JoinLastThreads(int count)
    {
        Thread[] local;
        lock (_lock)
        {
            if (count <= 0 || _threads.Count == 0)
                return;

            int actual = Math.Min(count, _threads.Count);
            int start = _threads.Count - actual;
            local = _threads.GetRange(start, actual).ToArray();
            _threads.RemoveRange(start, actual);
        }

        JoinAll(local);
    }

    /// <summary>현재 목록의 index번째 스레드 하나를 목록에서 빼 Join한다. 범위 밖이면 아무것도 하지 않는다.</summary>
    public void JoinThreadByIndex(int index)
    {
        Thread thread;
        lock (_lock)
        {
            if (index < 0 || index >= _threads.Count)
                return;

            thread = _threads[index];
            _threads.RemoveAt(index);
        }

        JoinAll(new[] { thread });
    }

    /// <summary>관리 스레드 ID(<see cref="Thread.ManagedThreadId"/>)가 같은 스레드를 찾아 목록에서 빼 Join한다. 없으면 아무것도 하지 않는다.</summary>
    public void JoinThreadById(int managedThreadId)
    {
        Thread? thread = null;
        lock (_lock)
        {
            int index = _threads.FindIndex(t => t.ManagedThreadId == managedThreadId);
            if (index >= 0)
            {
                thread = _threads[index];
                _threads.RemoveAt(index);
            }
        }

        if (thread != null)
            JoinAll(new[] { thread });
    }

    /// <summary>아직 Join되지 않은 관리 스레드 수. 스스로 끝난 스레드도 Join 전까지는 센다.</summary>
    public int GetThreadCount()
    {
        lock (_lock)
            return _threads.Count;
    }

    /// <summary>전체 종료 절차에 들어갔는지. 워커 루프가 주기적으로 확인해 빠져나오는 용도.</summary>
    public bool IsShuttingDown => _shuttingDown;

    /// <summary>
    /// Join 없이 종료 플래그만 먼저 세운다. 워커를 깨우는 신호(예: 완료 포트에 깨움 메시지)를 보내기 전에 부르면 깨어난 워커가 곧바로 종료를 알아챈다.
    /// 실제 Join은 <see cref="JoinThreads"/>를 따로 불러야 한다. 여러 번 불러도 안전하다.
    /// </summary>
    public void RequestShutdown() => _shuttingDown = true;

    /// <summary>
    /// 관리 스레드가 하나도 없을 때만 종료 플래그를 풀어 <see cref="CreateThread"/>를 다시 허용한다(재시작용).
    /// 스레드가 남아 있으면 아무것도 하지 않고 false. 서비스 Stop() 끝(Join 완료 뒤)에 한 번 부른다.
    /// </summary>
    public bool ResetShutdown()
    {
        lock (_lock)
        {
            if (_threads.Count != 0)
                return false;

            _shuttingDown = false;
            return true;
        }
    }

    public void Dispose() => JoinThreads();

    private static void JoinAll(Thread[] threads)
    {
        Thread self = Thread.CurrentThread;
        foreach (Thread t in threads)
        {
            if (t != self)
                t.Join();
        }
    }

    private static void ThreadMain(Action callback)
    {
        InitThread();
        try
        {
            callback();
        }
        catch
        {
            // 예외로 끝나도 스레드 로컬 자원은 정리한다. 이어서 다시 던져지므로 처리되지 않은 예외로 프로세스가 종료된다.
            DestroyThread();
            throw;
        }

        DestroyThread();
    }

    private static void InitThread()
    {
        t_threadId = (uint)Interlocked.Increment(ref s_nextThreadId);
        ThreadStartHook?.Invoke();
    }

    private static void DestroyThread() => ThreadExitHook?.Invoke();
}

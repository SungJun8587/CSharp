// PacketDispatcher.cs : implementation of the PacketDispatcher class.
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reflection;

namespace Core.Net;

/// <summary>패킷 디스패처 처리 결과. 프로토콜/서버 종류와 무관하게 공통이다.</summary>
public enum PacketDispatchResult
{
    /// <summary>정상 처리됨.</summary>
    Handled,

    /// <summary>등록되지 않은 패킷 타입.</summary>
    UnknownType,

    /// <summary>헤더의 size 가 헤더 크기나 타입별 최소 크기보다 작거나, 받은 버퍼를 넘음. (프로토콜 위반)</summary>
    SizeViolation,
}

/// <summary>
/// 패킷 핸들러. 세션 등 처리 주체와 패킷을 받는다.
/// </summary>
/// <param name="context">이 패킷을 처리할 세션/객체</param>
/// <param name="packet">헤더를 포함한 전체 패킷 (헤더의 size 길이로 잘려 있다). 반환 이후에는 무효이므로 보관하지 말 것.</param>
public delegate void PacketHandler<in TContext>(TContext context, ReadOnlySpan<byte> packet);

/// <summary>
/// 핸들러 메서드를 PacketDispatcher.RegisterFromType / RegisterFromAssembly 가 자동 등록하게 하는 표시.
/// 대상은 `static void M(TContext context, ReadOnlySpan&lt;byte&gt; packet)` 형태의 메서드다.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PacketHandlerAttribute : Attribute
{
    public PacketHandlerAttribute(ushort type) => Type = type;

    /// <summary>처리할 패킷 타입(헤더의 id).</summary>
    public ushort Type { get; }

    /// <summary>패킷의 최소 허용 크기(헤더 포함). 생략하면 헤더 크기(4)이다.</summary>
    public int MinSize { get; set; } = PacketHeader.HeaderSize;
}

/// <summary>
/// 패킷 타입(ushort) -> 핸들러 매핑과 크기 검증을 맡는 공용 디스패처.
/// 특정 세션/프로토콜/패킷 enum 을 모르므로 서버 종류와 무관하게 쓸 수 있다.
///
/// 등록은 시작할 때 한 번 하고(Register 직접 호출, 또는 [PacketHandler] 자동 등록), 첫 Dispatch 또는 Freeze 호출 뒤에는
/// 테이블이 바뀌지 않는다. 그래서 Dispatch 는 락 없이 여러 스레드에서 동시에 불러도 된다.
///
/// 사용 예:
///   var dispatcher = new PacketDispatcher&lt;ChatSession&gt;();
///   dispatcher.Register((ushort)ChatPacket.Chat, 6, ChatHandlers.OnChat);      // 직접 등록
///   dispatcher.RegisterFromAssembly(typeof(ChatHandlers).Assembly);            // 또는 [PacketHandler] 자동 등록
///   ...
///   protected override void OnRecvPacket(ReadOnlySpan&lt;byte&gt; packet)
///   {
///       if (dispatcher.Dispatch(this, packet) != PacketDispatchResult.Handled)
///           Disconnect(CloseReason.ProtocolError);
///   }
/// 핸들러에서 예외가 나면 그대로 전파된다. PacketSession 안에서는 연결을 끊는 처리로 이어진다.
/// </summary>
public sealed class PacketDispatcher<TContext>
{
    private readonly struct Entry
    {
        public readonly int MinSize;
        public readonly PacketHandler<TContext> Handler;
        public Entry(int minSize, PacketHandler<TContext> handler) { MinSize = minSize; Handler = handler; }
    }

    private Dictionary<ushort, Entry>? _building = new Dictionary<ushort, Entry>();
    private FrozenDictionary<ushort, Entry>? _frozen;
    private readonly object _gate = new object();

    /// <summary>등록이 끝나 더 등록할 수 없는 상태인지.</summary>
    public bool IsFrozen => _frozen != null;

    /// <summary>등록된 핸들러 수.</summary>
    public int Count
    {
        get
        {
            FrozenDictionary<ushort, Entry>? frozen = _frozen;
            if (frozen != null) return frozen.Count;
            lock (_gate) return _frozen?.Count ?? _building!.Count;
        }
    }

    /// <summary>
    /// 패킷 핸들러를 등록합니다.
    /// </summary>
    /// <param name="type">패킷 타입(헤더의 id)</param>
    /// <param name="minSize">패킷의 최소 허용 크기(헤더 포함). 헤더 크기(4)보다 작으면 안 된다</param>
    /// <param name="handler">패킷을 처리할 콜백</param>
    /// <exception cref="ArgumentNullException">handler 가 null 일 때</exception>
    /// <exception cref="ArgumentOutOfRangeException">minSize 가 헤더 크기 미만이거나 65535 초과일 때</exception>
    /// <exception cref="InvalidOperationException">같은 타입이 이미 등록됐거나, 등록이 끝난 뒤(Freeze/첫 Dispatch 이후)일 때</exception>
    public void Register(ushort type, int minSize, PacketHandler<TContext> handler)
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));

        // minSize 가 헤더보다 작으면 size 가 0~3 인 패킷도 핸들러까지 도달해 호출부의 프레이밍이 전진하지 못한다.
        if (minSize < PacketHeader.HeaderSize || minSize > PacketHeader.MaxPacketSize)
            throw new ArgumentOutOfRangeException(nameof(minSize), minSize, $"minSize 는 {PacketHeader.HeaderSize}..{PacketHeader.MaxPacketSize} 이어야 합니다.");

        lock (_gate)
        {
            if (_frozen != null)
                throw new InvalidOperationException("등록이 끝난 디스패처에는 핸들러를 더 등록할 수 없습니다.");

            // 같은 타입이 중복 등록되면(복붙 실수 등) 조용히 덮어쓰지 않고 바로 알 수 있게 예외를 던진다.
            if (!_building!.TryAdd(type, new Entry(minSize, handler)))
                throw new InvalidOperationException($"패킷 타입 {type} 의 핸들러가 이미 등록되어 있습니다.");
        }
    }

    /// <summary>
    /// type 안의 `[PacketHandler]` 가 붙은 static 메서드를 모두 등록합니다. (public/private 모두)
    /// 첫 매개변수가 TContext 가 아닌 메서드는 다른 디스패처의 핸들러로 보고 건너뛴다.
    /// 첫 매개변수가 TContext 인데 나머지 시그니처가 `(ReadOnlySpan&lt;byte&gt;)`, 반환형이 void 가 아니면 오류다.
    /// </summary>
    /// <returns>등록한 핸들러 수</returns>
    /// <exception cref="InvalidOperationException">시그니처가 맞지 않거나 instance 메서드이거나 타입이 중복될 때</exception>
    public int RegisterFromType(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        int count = 0;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (MethodInfo method in type.GetMethods(flags))
        {
            var attr = method.GetCustomAttribute<PacketHandlerAttribute>(false);
            if (attr == null) continue;

            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length == 0 || ps[0].ParameterType != typeof(TContext))
                continue;       // 다른 TContext 용 핸들러

            string where = $"{type.FullName}.{method.Name}";
            if (!method.IsStatic)
                throw new InvalidOperationException($"{where}: [PacketHandler] 메서드는 static 이어야 합니다.");
            if (method.ReturnType != typeof(void) || ps.Length != 2 || ps[1].ParameterType != typeof(ReadOnlySpan<byte>))
                throw new InvalidOperationException($"{where}: 시그니처는 static void M({typeof(TContext).Name} context, ReadOnlySpan<byte> packet) 이어야 합니다.");

            var handler = (PacketHandler<TContext>)Delegate.CreateDelegate(typeof(PacketHandler<TContext>), method);
            try
            {
                Register(attr.Type, attr.MinSize, handler);
            }
            catch (ArgumentOutOfRangeException e)
            {
                throw new InvalidOperationException($"{where}: {e.Message}", e);
            }
            ++count;
        }
        return count;
    }

    /// <summary>
    /// 어셈블리 안의 모든 타입에서 `[PacketHandler]` 메서드를 찾아 등록합니다. (RegisterFromType 을 타입마다 부른다)
    /// </summary>
    /// <returns>등록한 핸들러 수</returns>
    public int RegisterFromAssembly(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));

        int count = 0;
        foreach (Type t in assembly.GetTypes())
            count += RegisterFromType(t);
        return count;
    }

    /// <summary>
    /// 등록을 끝냅니다. 이후에는 Register 가 예외를 던지고, Dispatch 는 락 없이 읽는다. 첫 Dispatch 가 자동으로 부르므로 생략해도 된다.
    /// </summary>
    public void Freeze()
    {
        if (_frozen != null) return;
        lock (_gate)
        {
            if (_frozen != null) return;
            _frozen = _building!.ToFrozenDictionary();
            _building = null;
        }
    }

    /// <summary>
    /// 패킷을 등록된 핸들러로 넘깁니다.
    /// </summary>
    /// <param name="context">핸들러에 전달할 세션/객체</param>
    /// <param name="packet">헤더를 포함해 받아 둔 버퍼. 헤더의 size 가 이 길이를 넘으면 SizeViolation</param>
    public PacketDispatchResult Dispatch(TContext context, ReadOnlySpan<byte> packet)
    {
        // 호출부가 프레이밍 검증을 빠뜨렸을 수 있으므로 공용 컴포넌트인 여기서 한 번 더 막는다.
        if (packet.Length < PacketHeader.HeaderSize)
            return PacketDispatchResult.SizeViolation;

        int size = PacketHeader.ReadSize(packet);
        if (size < PacketHeader.HeaderSize || size > packet.Length)
            return PacketDispatchResult.SizeViolation;

        FrozenDictionary<ushort, Entry>? table = _frozen;
        if (table == null)
        {
            Freeze();
            table = _frozen!;
        }

        if (!table.TryGetValue(PacketHeader.ReadId(packet), out Entry entry))
            return PacketDispatchResult.UnknownType;

        if (size < entry.MinSize)
            return PacketDispatchResult.SizeViolation;

        entry.Handler(context, packet.Slice(0, size));
        return PacketDispatchResult.Handled;
    }
}

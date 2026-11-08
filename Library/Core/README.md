# Core

C# 공용 라이브러리. 어셈블리 둘로 나뉘며 의존 방향은 `Core.Net` -> `Core.Util` 한쪽뿐이다.

| 프로젝트 | 네임스페이스 | 내용 |
|---|---|---|
| `Util/Core.Util.csproj` | `Core.Util` | 의존성 없는 공용 유틸: 로그(`LogManager`), 파일 쓰기(`FileUtil`, `FileReplace`), 문자열(`TextUtil`), `AsciiChar`. 앞으로의 Util 클래스는 여기에 추가한다 |
| `Net/Core.Net.csproj`   | `Core.Net`  | 네트워크: 세션, 리스너, 송수신 버퍼, TCP/UDP/TLS 서비스, JobQueue/JobTimer, 패킷 디스패처(`PacketDispatcher<TContext>`: 직접 등록 또는 `[PacketHandler]` 자동 등록), 소켓 유틸, `INetLogger`와 `LogManagerNetLogger`(LogManager에 연결) |

네트워크가 필요 없는 도구는 `Core.Util`만 참조한다.

## 빌드와 데모
    dotnet build Core.sln
    dotnet run --project demo/UtilDemo/Core.UtilDemo.csproj      # Util 테스트
    dotnet run --project demo/NetDemo/Core.NetDemo.csproj        # Net 스모크/스트레스 테스트
    dotnet run --project demo/NetLogDemo/Core.NetLogDemo.csproj  # Net + Util 로그 연결 테스트
    dotnet run --project demo/DispatcherDemo/Core.DispatcherDemo.csproj  # 패킷 디스패처 테스트

각 데모는 마지막 줄에 `ALL PASSED`가 나와야 한다.

## 사용
    using Core.Net;
    using Core.Util;
    var options = new NetServiceOptions { Logger = new LogManagerNetLogger() };

## 패킷 디스패처
    var dispatcher = new PacketDispatcher<ChatSession>();
    dispatcher.Register((ushort)ChatPacket.Chat, 6, ChatHandlers.OnChat);   // 직접 등록
    dispatcher.RegisterFromAssembly(typeof(ChatHandlers).Assembly);         // [PacketHandler(type, MinSize = n)] static 메서드 자동 등록

    protected override void OnRecvPacket(ReadOnlySpan<byte> packet)
    {
        if (dispatcher.Dispatch(this, packet) != PacketDispatchResult.Handled)
            Disconnect(CloseReason.ProtocolError);
    }

등록은 시작할 때 한 번 하고, 첫 `Dispatch`(또는 `Freeze`) 뒤에는 바뀌지 않아 `Dispatch`는 락 없이 여러 스레드에서 불러도 된다.

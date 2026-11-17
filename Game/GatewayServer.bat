@ECHO OFF
chcp 65001 >nul
CD /D "%~dp0"
SET EXE=Bin\Release\Game.GatewayServer.exe
IF NOT EXIST "%EXE%" SET EXE=Bin\Debug\Game.GatewayServer.exe
IF NOT EXIST "%EXE%" (
    ECHO [GatewayServer] Game.GatewayServer.exe not found in Bin\Release or Bin\Debug. Build Game.sln first.
    PAUSE
    EXIT /B 1
)
"%EXE%" --config "Config\gatewayserver_config.json" %*
SET RC=%ERRORLEVEL%
ECHO.
ECHO [GatewayServer] exit code=%RC%
PAUSE
EXIT /B %RC%

:: ---- 아래는 설명(주석)이며 EXIT /B 이후라 실행되지 않는다. 한글 주석이 cmd 파싱에 영향을 주지 않도록 실행 줄 뒤에 둔다. ----
:: GatewayServer.bat [옵션...]   (C# 서버: Game.sln을 Release로 빌드하면 Bin\Release에 Game.GatewayServer.exe가 만들어진다)
:: [설명]
::  - 클라이언트(봇 포함)가 접속하는 클라이언트 포트와, 존 서버가 접속하는 존 서버 포트를 열고
::    클라이언트 패킷(2000번대)을 해석 없이 현재 존으로 중계. 존 서버 하트비트가 끊기면 해당 존의 클라이언트에 존 소실을 알림
::  - 실행 순서: GatewayServer -> ZoneServer(존마다 1개) -> BotClient
::  - 옵션을 모두 생략하면 Config\gatewayserver_config.json 값으로 실행(파일이 없으면 시작 실패)
::  - 배치 파일에 준 옵션은 실행 줄 끝의 %* 로 그대로 서버에 전달된다. 예) GatewayServer.bat --client-port 7001
::  - 상대 경로(Config\..., Bin\...)는 이 배치 파일이 있는 폴더(Game) 기준이므로 탐색기에서 더블클릭해 실행할 것
:: [파라미터 설명] (명령행 값이 설정 파일 값보다 우선)
::      --config <file>             : 설정 JSON 파일(기본 Config\gatewayserver_config.json)
::      --dump-config <file>        : 기본값 설정 파일 템플릿을 저장하고 종료
::      --client-ip <addr>          : 클라이언트 접속 바인딩 주소(기본 0.0.0.0)
::      --client-port <n>           : 클라이언트 접속 포트(기본 7000)
::      --max-clients <n>           : 최대 클라이언트 세션 수(기본 5000)
::      --zone-ip <addr>            : 존 서버 접속 바인딩 주소(기본 127.0.0.1)
::      --zone-port <n>             : 존 서버 접속 포트(기본 7100)
::      --max-zones <n>             : 최대 존 서버 세션 수(기본 64)
::      --heartbeat-ms <n>          : 존 서버에 지시하는 하트비트 주기(기본 2000)
::      --heartbeat-timeout-ms <n>  : 이 시간 동안 하트비트가 없으면 존 서버 연결을 끊음, 주기보다 커야 함(기본 10000)
::      --client-send-queue-bytes <n> : 클라이언트 세션당 송신 대기 한도(바이트), 넘으면 끊음(기본 1048576, 0=제한 없음)
::  JSON 키(Config\gatewayserver_config.json 참고): Name, IP, Port, MaxSessionCount, WorkerThreadCnt,
::      ZoneIP, ZonePort, ZoneMaxSessionCount, ZoneWorkerThreadCnt, HeartbeatIntervalMs, HeartbeatTimeoutMs,
::      ClientMaxPacketsPerSec(세션당 초당 패킷 한도, 기본 120, 0=제한 없음), ClientMaxSendQueueBytes
::  종료: Ctrl+C


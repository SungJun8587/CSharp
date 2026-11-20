@ECHO OFF
chcp 65001 >nul
CD /D "%~dp0"

REM ---- 이 배치 파일이 띄울 존 ID. 여기만 바꾸면 된다(0 = 시험장). 실행 때 --zone-id 를 따로 주면 그 값이 우선한다. ----
SET ZONE_ID=0

SET EXE=Bin\Release\Game.ZoneServer.exe
IF NOT EXIST "%EXE%" SET EXE=Bin\Debug\Game.ZoneServer.exe
IF NOT EXIST "%EXE%" (
    ECHO [ZoneServer] Game.ZoneServer.exe not found in Bin\Release or Bin\Debug. Build Game.sln first.
    PAUSE
    EXIT /B 1
)
ECHO [ZoneServer] zone-id=%ZONE_ID%
"%EXE%" --config "Config\zoneserver_config.json" --zone-id %ZONE_ID% %*
SET RC=%ERRORLEVEL%
ECHO.
ECHO [ZoneServer] exit code=%RC%
PAUSE
EXIT /B %RC%

:: ---- 아래는 설명(주석)이며 EXIT /B 이후라 실행되지 않는다. 한글 주석이 cmd 파싱에 영향을 주지 않도록 실행 줄 뒤에 둔다. ----
:: ZoneServer.bat [옵션...]   (C# 서버: Game.sln을 Release로 빌드하면 Bin\Release에 Game.ZoneServer.exe가 만들어진다)
:: [설명]
::  - 존 하나를 호스팅하는 프로세스. Gateway의 존 서버 포트에 접속해 등록하고, 틱 루프로 이동/AOI/전투/몬스터 AI를 처리
::  - 필드/던전마다 zone-id를 다르게 해서 프로세스를 여러 개 띄운다. 이 배치 파일은 위쪽 SET ZONE_ID 값(기본 0, 시험장)으로 띄우며, 다른 존은 ZONE_ID 를 바꾸거나 ZoneServer.bat --zone-id 2 처럼 옵션을 주면 된다(뒤에 준 --zone-id 가 우선)
::  - Gateway가 등록을 거부하면(중복 zone-id 등) 스스로 종료
::  - 영웅/몬스터/스폰/보스/드롭/차단 맵 데이터는 GameDataDir(기본 GameData)의 JSON에서 읽음. Hero.json, Monster.json, Spawn.json은 필수
::  - 배치 파일에 준 옵션은 실행 줄 끝의 %* 로 그대로 서버에 전달된다
::  - 상대 경로(Config\..., GameData, Bin\...)는 이 배치 파일이 있는 폴더(Game) 기준이므로 탐색기에서 더블클릭해 실행할 것
:: [파라미터 설명] (명령행 값이 설정 파일 값보다 우선)
::      --config <file>         : 설정 JSON 파일(기본 Config\zoneserver_config.json)
::      --dump-config <file>    : 기본값 설정 파일 템플릿을 저장하고 종료
::      --zone-id <n>           : 이 프로세스가 호스팅하는 존 ID, Gateway 전체에서 유일해야 함(기본 1)
::      --kind <n>              : 존 종류 1=필드, 2=던전(기본 1)
::      --capacity <n>          : 최대 수용 인원(기본 500)
::      --name <str>            : 로그/모니터링용 서버 이름(기본 ZoneServer)
::      --gateway-ip <addr>     : Gateway의 존 서버용 포트 주소(기본 127.0.0.1)
::      --gateway-port <n>      : Gateway의 존 서버용 포트(기본 7100)
::      --tick-ms <n>           : 존 틱 간격(기본 50)
::      --min-x/--min-y/--max-x/--max-y <f> : 존 영역(기본 0,0 ~ 1000,1000)
::      --cell-size <f>         : AOI 셀 크기(기본 50)
::      --spawn-x/--spawn-y <f> : 플레이어 입장 위치(기본 100,100)
::  JSON 키(Config\zoneserver_config.json 참고): Name, WorkerThreadCnt, ServerNode, ZoneId, ZoneKind, Capacity,
::      TickIntervalMs, MinX, MinY, MaxX, MaxY, CellSize, SpawnX, SpawnY, RespawnDelayMs, RespawnInvulnMs, AllowPvp,
::      GameDataDir, MonsterThinkIntervalMs, MaxMonsterCount, MetricsIntervalMs, RequestGuardEnabled
::  종료: Ctrl+C

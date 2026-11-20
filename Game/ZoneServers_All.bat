@ECHO OFF
chcp 65001 >nul
CD /D "%~dp0"
SETLOCAL

:: 열 존 ID 목록. 밖에서 SET ZONES=1 2 3 처럼 먼저 지정하면 그 값을 쓴다. 0 은 모든 몬스터가 나오는 시험장이다(지형은 ZoneRule.json 의 Terrain).
IF NOT DEFINED ZONES SET ZONES=0 1 2 3 4 5 6 7 8 9 10

SET ZEXE=Bin\Release\Game.ZoneServer.exe
IF NOT EXIST "%ZEXE%" SET ZEXE=Bin\Debug\Game.ZoneServer.exe
IF NOT EXIST "%ZEXE%" (
    ECHO [ZoneServers_All] Game.ZoneServer.exe not found in Bin\Release or Bin\Debug. Build Game.sln first.
    PAUSE
    EXIT /B 1
)

SET GATEWAY=0
IF /I "%~1"=="/G" (
    SET GEXE=Bin\Release\Game.GatewayServer.exe
    IF NOT EXIST "%GEXE%" SET GEXE=Bin\Debug\Game.GatewayServer.exe
    IF NOT EXIST "%GEXE%" (
        ECHO [ZoneServers_All] Game.GatewayServer.exe not found in Bin\Release or Bin\Debug. Build Game.sln first.
        PAUSE
        EXIT /B 1
    )
    ECHO [ZoneServers_All] starting GatewayServer
    START "" /B "%GEXE%" --config "Config\gatewayserver_config.json"
    SET GATEWAY=1
    timeout /t 2 /nobreak >nul
)

:: 같은 콘솔 창 하나에서 모든 존을 돌린다. 로그 줄마다 서버 이름이 붙어 있어 어느 존인지 알 수 있다.
FOR %%Z IN (%ZONES%) DO (
    ECHO [ZoneServers_All] starting zone %%Z
    START "" /B "%ZEXE%" --config "Config\zoneserver_config.json" --zone-id %%Z --name Zone%%Z
)

ECHO.
ECHO [ZoneServers_All] started zones: %ZONES%
ECHO [ZoneServers_All] Press any key or Ctrl+C in this window to stop everything started here.
PAUSE >nul

ECHO [ZoneServers_All] stopping zone servers
TASKKILL /F /IM Game.ZoneServer.exe >nul 2>&1
IF "%GATEWAY%"=="1" TASKKILL /F /IM Game.GatewayServer.exe >nul 2>&1
EXIT /B 0

:: ---- 아래는 설명(실행되지 않는다) ----
:: ZoneServers_All.bat [/G]
:: [설명]
::  - ZONES 목록의 존마다 존 서버 프로세스를 띄우되, 새 창을 열지 않고 이 콘솔 창 하나에서 모두 돌린다(START /B). 서버 이름은 Zone<id>.
::  - 이 창에서 아무 키나 누르거나 Ctrl+C 를 누르면 종료한다. 키를 누르면 Game.ZoneServer.exe 를 모두 끈다(다른 창에서 띄운 존 서버도 같이 꺼진다).
::  - /G 를 주면 Gateway 도 같은 창에서 먼저 열고 잠시 기다린다. 키를 누르면 Gateway 도 같이 끈다.
::  - 존 서버는 Gateway로 나가는 연결만 맺는다. 그래서 같은 PC에서 여러 개를 띄워도 포트가 겹치지 않는다.
::  - Gateway를 나중에 열어도 존 서버가 자동으로 다시 접속한다(지수 백오프 500ms~5s). /G 는 편의를 위해 Gateway를 먼저 연다.
::  - 존 ID는 GameData\ZoneRule.json, ZoneLink.json, Spawn.json 에 있는 0~10 이다. 존을 추가하면 위 ZONES 기본값에 ID를 더한다.
::  - 일부만 열려면 먼저 목록을 지정한다. 예) SET ZONES=1 2 6 & ZoneServers_All.bat
::  - 존 하나만 따로 보고 싶으면 ZoneServer.bat --zone-id <id> 를 쓴다. 그쪽은 자기 창에서 돌고 종료 코드를 보여 준다.

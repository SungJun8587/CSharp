@ECHO OFF
chcp 65001 >nul
CD /D "%~dp0"
SETLOCAL

:: 열 존 ID 목록. 밖에서 SET ZONES=1 2 3 처럼 먼저 지정하면 그 값을 쓴다.
IF NOT DEFINED ZONES SET ZONES=1 2 3 4 5 6 7 8 9 10

IF NOT EXIST "%~dp0ZoneServer.bat" (
    ECHO [ZoneServers_All] ZoneServer.bat not found next to this file.
    PAUSE
    EXIT /B 1
)

:: /G 를 주면 게이트웨이를 먼저 열고 잠시 기다린다.
IF /I "%~1"=="/G" (
    IF NOT EXIST "%~dp0GatewayServer.bat" (
        ECHO [ZoneServers_All] GatewayServer.bat not found next to this file.
        PAUSE
        EXIT /B 1
    )
    ECHO [ZoneServers_All] starting GatewayServer
    START "GatewayServer" "%~dp0GatewayServer.bat"
    timeout /t 2 /nobreak >nul
)

FOR %%Z IN (%ZONES%) DO (
    ECHO [ZoneServers_All] starting zone %%Z
    START "Zone %%Z" "%~dp0ZoneServer.bat" --zone-id %%Z --name Zone%%Z
)

ECHO.
ECHO [ZoneServers_All] started zones: %ZONES%
EXIT /B 0

:: ---- 아래는 설명(실행되지 않는다) ----
:: ZoneServers_All.bat [/G]
:: [설명]
::  - ZONES 목록의 존마다 ZoneServer.bat 을 새 창으로 연다. 창 제목은 "Zone <id>", 서버 이름은 Zone<id>.
::  - 존 서버는 Gateway로 나가는 연결만 맺는다. 그래서 같은 PC에서 여러 개를 띄워도 포트가 겹치지 않는다.
::  - Gateway를 나중에 열어도 존 서버가 자동으로 다시 접속한다(지수 백오프 500ms~5s). /G 는 편의를 위해 Gateway를 먼저 연다.
::  - 존 ID는 GameData\ZoneRule.json, ZoneLink.json, Spawn.json 에 있는 1~10 이다. 존을 추가하면 위 ZONES 기본값에 ID를 더한다.
::  - 일부만 열려면 먼저 목록을 지정한다. 예) SET ZONES=1 2 6 & ZoneServers_All.bat
::  - 각 창은 해당 존 서버를 Ctrl+C 로 끄면 종료 코드를 보여 주고 PAUSE 한다.

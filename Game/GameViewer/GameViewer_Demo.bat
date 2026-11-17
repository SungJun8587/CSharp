@ECHO OFF
chcp 65001 >nul
CD /D "%~dp0"
REM DATA: GameData folder read by the server (XlsxConverter output). Edit it if needed, e.g. SET DATA=D:\Game\GameData
SET DATA=%~dp0..\GameData
SET EXE=%~dp0Bin\Release\GameViewer.App.exe
IF NOT EXIST "%EXE%" SET EXE=%~dp0..\Bin\Release\GameViewer.App.exe
IF NOT EXIST "%EXE%" SET EXE=%~dp0Bin\Debug\GameViewer.App.exe
IF NOT EXIST "%EXE%" SET EXE=%~dp0..\Bin\Debug\GameViewer.App.exe
IF NOT EXIST "%EXE%" (
    ECHO GameViewer.App.exe not found. Build GameViewer.sln first.
    PAUSE
    EXIT /B 1
)
"%EXE%" --demo --data "%DATA%" --auto %*
SET RC=%ERRORLEVEL%
IF NOT %RC%==0 (
    ECHO.
    ECHO [GameViewer_Demo] exit code=%RC%
    PAUSE
)
EXIT /B %RC%

:: ---- 아래는 설명(주석)이며 EXIT /B 이후라 실행되지 않는다. ----
:: 서버 없이 전투 화면을 확인하는 데모 실행 (영웅 + 동료 3명 vs 몬스터 12마리를 로컬에서 시뮬레이션)
:: 조작/자동전투/스킬바/미니맵을 서버 연결 전에 점검할 때 사용
:: SET DATA 줄은 서버가 읽는 GameData 폴더(XlsxConverter 출력)로 고치세요. 예) SET DATA=D:\Game\GameData

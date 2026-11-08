@ECHO OFF
:: 서버 없이 전투 화면을 확인하는 데모 실행 (영웅 + 동료 3명 vs 몬스터 12마리를 로컬에서 시뮬레이션)
:: 조작/자동전투/스킬바/미니맵을 서버 연결 전에 점검할 때 사용
SET DATA=%~dp0..\GameData
:: (위 줄) 서버가 읽는 GameData 폴더(XlsxConverter 출력)로 고치세요. 예) SET DATA=D:\Game\GameData
SET EXE=%~dp0App\bin\Release\net48\GameViewer.App.exe
IF NOT EXIST "%EXE%" SET EXE=%~dp0App\bin\Debug\net48\GameViewer.App.exe
IF NOT EXIST "%EXE%" (
    ECHO GameViewer.App.exe 가 없습니다. GameViewer.sln 을 먼저 빌드하세요.
    PAUSE
    EXIT /B 1
)
"%EXE%" --demo --data "%DATA%" --auto %*

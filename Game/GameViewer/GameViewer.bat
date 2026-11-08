@ECHO OFF
:: GameViewer.App.exe [옵션...]
:: [설명]
::  - Gateway 클라이언트 포트에 접속해 로그인 -> 존 입장 후, 주변(AOI) 전투를 숲 필드 화면으로 보여주는 전투 뷰어
::  - 직접 조작(좌클릭/우클릭/1~9/Q)하거나 A 키로 자동 전투를 켜서 BotClient 봇들과 함께 전투를 관찰할 수 있음
::  - 옵션을 모두 생략하면 GameViewer.json 의 값으로 실행 (명령행에 같이 준 옵션이 파일 값보다 우선)
:: [파라미터 설명]
::      --host <addr>         : Gateway 클라이언트 포트 주소(기본 127.0.0.1)
::      --port <n>            : Gateway 클라이언트 포트(기본 7000)
::      --char-id <n>         : 캐릭터 ID(기본 900001, 봇 100000+ 와 겹치지 않게)
::      --zone <id>           : 입장할 존 ID(기본 1)
::      --hero <id>           : 영웅 ID, 0=서버 기본 영웅(기본 1)
::      --data <dir>          : GameData JSON 폴더(서버의 GameDataDir 와 같은 폴더, 기본 GameData)
::      --auto                : 접속하자마자 자동 전투
::      --scale <n>           : 화면 배율 px/unit(기본 20)
::      --width <n> --height <n> : 창 크기
::      --demo                : 서버 없이 로컬 시뮬레이션으로 실행(화면/조작 확인용)
::      --config <file>       : 설정 JSON 파일
::      --dump-config <file>  : 기본값 설정 파일 템플릿을 저장하고 종료
::  조작: 좌클릭 땅=이동/적=선택·추적공격, 우클릭 누른 채 이동, 1~9 스킬, Q 기본공격, Tab 대상 전환,
::        A 자동전투, F 시점고정, 휠 확대/축소, Space 내 위치, F5 재접속, F1 도움말, L 로그
SET DATA=%~dp0..\GameData
:: (위 줄) 서버가 읽는 GameData 폴더(XlsxConverter 출력)로 고치세요. 예) SET DATA=D:\Game\GameData
SET EXE=%~dp0App\bin\Release\net48\GameViewer.App.exe
IF NOT EXIST "%EXE%" SET EXE=%~dp0App\bin\Debug\net48\GameViewer.App.exe
IF NOT EXIST "%EXE%" (
    ECHO GameViewer.App.exe 가 없습니다. GameViewer.sln 을 먼저 빌드하세요.
    PAUSE
    EXIT /B 1
)
"%EXE%" --config "%~dp0GameViewer.json" --data "%DATA%" %*

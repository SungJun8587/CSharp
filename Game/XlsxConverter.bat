@ECHO OFF
chcp 65001 >nul
"K:\GitHub\CSharp\Game\Bin\Release\XlsxConverter.exe" "K:\GitHub\CPP\Project\Game\GameData" --out "K:\GitHub\CPP\Project\Game\GameData" --cpp-gen-out "K:\GitHub\CPP\Project\Game\GameCommon\Generated" --cs-gen-out "K:\GitHub\CSharp\Game\GameCommon\Generated" --cs-gen-namespace Game.Generated --cs-client-proto-out "K:\GitHub\CPP\Project\Game\GameViewer\Core\Protocol\Generated" --cs-client-proto-namespace GameViewer.Core.Protocol --proto-sizes-out "K:\GitHub\CPP\Project\Game\GameViewer\Tests\cpp_packet_sizes.txt"
SET RC=%ERRORLEVEL%
ECHO.
IF %RC%==0 (ECHO [XlsxConverter] 완료) ELSE (ECHO [XlsxConverter] 실패. 종료 코드=%RC%)
PAUSE
EXIT /B %RC%

:: ---- 아래는 설명(실행되지 않음. EXIT 이후라 인코딩과 무관) ----
:: XlsxConverter.exe [GameData 폴더 | 엑셀 파일] --out [출력 폴더] [--cpp-gen-out [폴더]] [--cs-gen-out [폴더]] [--cs-gen-namespace [이름]] [--cs-client-proto-out [폴더]] [--cs-client-proto-namespace [이름]] [--proto-sizes-out [파일]] [--sheets 시트이름 ...] [--incremental]
:: 종료 코드: 0 성공, 1 변환 실패(데이터 오류), 2 사용법/파일 오류
:: 인자 설명은 README.md 참고
:: [파라미터 설명]
::      - 1. GameData 폴더(그 안의 모든 .xlsx를 읽어 하나로 합침) : K:\GitHub\CPP\Project\Game\GameData
::      - 2. --out 출력 폴더 : K:\GitHub\CPP\Project\Game\GameData
::      - 3. --cpp-gen-out 헤더 출력 폴더 : K:\GitHub\CPP\Project\Game\GameCommon\Generated
::      - 4. --cs-gen-out C# 출력 폴더(시트 클래스, 서버 패킷 IPacket 구조체, ProtocolTypes) : K:\GitHub\CSharp\Game\GameCommon\Generated
::      - 5. --cs-gen-namespace C# 네임스페이스(생략 시 GameDataRows) : Game.Generated (GameCommon\ZoneCommon\ZonePacket.cs의 IPacket, PacketIO와 같은 네임스페이스)
::      - 6. --cs-client-proto-out 패킷 C# 폴더 : K:\GitHub\CPP\Project\Game\GameViewer\Core\Protocol\Generated
::      - 7. --cs-client-proto-namespace : GameViewer.Core.Protocol
::      - 8. --proto-sizes-out : K:\GitHub\CPP\Project\Game\GameViewer\Tests\cpp_packet_sizes.txt
::      - 9. --sheets 변환할 시트 이름(생략 시 _로 시작하지 않는 모든 시트) : Monster Spawn
::      - 10. --incremental (Pre-Build 이벤트용)

# XlsxConverter (C#)

기획 데이터 엑셀(.xlsx) -> 시트별 JSON 변환기와 C++/C# 행 구조체, 패킷 프로토콜 코드 생성기.

## 구성
- `./`   XlsxConverter. 변환 로직(Cell/ConverterApp 등)과 ClosedXML 어댑터(ClosedXmlWorkbookReader/Writer), 진입점(Program.cs)이 한 프로젝트에 있다.
- `tests/`  XlsxConverter.Tests. ZipArchive 기반 테스트용 리더로 전체 흐름을 검증하는 콘솔 테스트.

## 의존성
공용 유틸은 `Core.Util`(`../Core/Util/Core.Util.csproj`)을 참조한다. `Core`와 `XlsxConverter` 폴더는 같은 위치(형제)에 둔다.

## 빌드와 실행
    dotnet build XlsxConverter.csproj -c Release
    dotnet run --project XlsxConverter.csproj -- <GameData 폴더 | file.xlsx> --out ../GameData [--cpp-out dir] [--cs-out dir [--cs-namespace N]]
        [--cs-client-proto-out dir [--cs-client-proto-namespace N]] [--cs-server-proto-out dir [--cs-server-proto-namespace N]] [--proto-sizes-out file] [--sheets A B] [--incremental]

패킷 프로토콜 C# 출력은 두 가지다. `--cs-client-proto-out`은 클라이언트(뷰어)용 `Build`/`Parse` 클래스, `--cs-server-proto-out`은 서버용 `IPacket` 구조체(`Write`/`TryRead`, Cs 열과 무관하게 모든 패킷)와 `ProtocolTypes.gen.cs`를 만든다. 서버용은 같은 네임스페이스에 손으로 쓰는 `IPacket`, `PacketIO`가 있어야 한다. 출력 폴더는 `--cs-out`, `--cs-client-proto-out`과 서로 달라야 한다.

종료 코드: 0 성공, 1 변환 실패(데이터 오류), 2 사용법/파일 오류.

## 테스트
    dotnet run --project tests

## Visual Studio

`XlsxConverter.sln`을 연다. 시작 프로젝트는 `XlsxConverter`이고, ClosedXML은 처음 빌드할 때 NuGet에서 복원된다. 테스트는 `XlsxConverter.Tests` 프로젝트를 시작 프로젝트로 지정해 실행한다 (콘솔 프로그램, 마지막 줄에 `ALL PASSED`가 나와야 한다).

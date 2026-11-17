# XlsxConverter (C#) 적용 가이드

Nov 17, 2026 · @성준

XlsxConverter(C#)는 GameData 폴더의 기획 데이터 엑셀(.xlsx) 여러 개를 합쳐 시트별 JSON, 그 JSON을 읽는 C++/C# 행 코드, enum·상수 정의, 패킷 프로토콜 코드를 한 번에 만드는 .NET 8 콘솔 도구입니다. C++ 버전(xlnt 기반)을 C#으로 옮긴 것으로 엑셀 규칙, 명령행 옵션, 생성 결과는 같고 엑셀 읽기만 ClosedXML이 맡습니다. 엑셀 헤더가 스키마이고 코드는 그 스키마에서 나오므로 데이터와 코드가 항상 일치합니다.

## 1. 전체 구조

```text
GameData\*.xlsx ──► XlsxConverter ──┬─► <시트>.json                                                      (--out)
(기능별 파일 여러 개)               ├─► <시트>.gen.h, Enums/EnumStrings/Consts.gen.h, GeneratedCommon.h  (--cpp-gen-out)
데이터 시트 + 패킷 정의 시트        ├─► ZonePacketTypes.gen.h, ZonePacketStructs.gen.h                  (--cpp-gen-out)
                                    ├─► <시트>.gen.cs, Enums.gen.cs, Consts.gen.cs                      (--cs-gen-out)
                                    ├─► ProtocolTypes.gen.cs, Packets.gen.cs  (서버 IPacket 구조체)     (--cs-gen-out)
                                    ├─► ProtocolTypes.gen.cs, Packets.gen.cs  (클라이언트 Build/Parse)  (--cs-client-proto-out)
                                    └─► cpp_packet_sizes.txt                                            (--proto-sizes-out)
```

시트 하나가 JSON 파일 하나, C++ 구조체 하나(S<시트>Row), C# 클래스 둘(<시트>Row, <시트>Table)이 됩니다.

Enum, Const 시트는 JSON이 아니라 형식 정의입니다. enum과 상수 코드만 만듭니다.

패킷 정의 시트 5개(PacketEnum, PacketConst, PacketGroup, Packet, PacketField)도 JSON이 아니라 형식 정의입니다. 패킷 구조체·번호·결과 코드 enum·프로토콜 상수의 C++ 코드, 서버용 C# 구조체(IPacket), 클라이언트용 C# 코드(Build/Parse/ToBytes), C++ sizeof 대조용 크기 목록을 만듭니다. 시트가 없으면 아무것도 만들지 않습니다.

코드 생성은 출력 폴더를 지정한 언어만 합니다. --cpp-gen-out만 주면 C++, --cs-gen-out만 주면 C#, 둘 다 주면 둘 다 만듭니다. --cs-gen-out 폴더에는 시트 클래스와 함께 서버용 패킷 C#(Packets.gen.cs, ProtocolTypes.gen.cs)이 같은 네임스페이스(--cs-gen-namespace)로 만들어집니다. 클라이언트(뷰어)용 패킷 C#은 --cs-client-proto-out(네임스페이스는 --cs-client-proto-namespace), 크기 목록은 --proto-sizes-out으로 따로 지정하며, 패킷 C++ 헤더는 --cpp-gen-out 폴더에 함께 생성됩니다.

오류가 하나라도 있으면 오류를 출력하고(최대 200건이고 나머지는 "... 그 밖에 N건 더 있습니다"로 건수만 알립니다) 아무 파일도 쓰지 않습니다.

## 2. 프로젝트 구성

변환 로직과 ClosedXML 어댑터, 진입점이 `XlsxConverter` 프로젝트 하나에 있고, 테스트는 `tests/`의 별도 프로젝트입니다. 공용 유틸은 `Core.Util`(`../Core/Util/Core.Util.csproj`)을 프로젝트 참조하므로 `Core`와 `XlsxConverter` 폴더는 같은 부모 아래 형제로 둡니다.

| 파일 | 역할 |
| --- | --- |
| Program.cs | 진입점. Options 해석 후 ConverterApp 실행, 종료 코드 반환 |
| Options.cs | 명령행 해석과 옵션 조합 검사 (`--cs-client-proto-out`과 `--cs-gen-out` 동일 폴더 금지, `--sheets`와 코드 생성 옵션 동시 사용 금지) |
| ConverterApp.cs | 시트 읽기 순서, 오류 수집, 파일 쓰기, 낡은 생성 파일 정리, 증분 실행 |
| WorkbookSet.cs | GameData 폴더의 xlsx 여러 개를 하나로 합침. 시트 이름 중복 검사 |
| ClosedXmlWorkbookReader.cs | ClosedXML로 엑셀 읽기 (IWorkbookReader 구현). 셀 값을 Cell(빈 칸/문자열/숫자/불리언)로 변환 |
| ClosedXmlWorkbookWriter.cs / WorkbookWriterRules.cs | 셀 모델을 새 .xlsx로 쓰는 라이터와 그 검증 규칙(시트 이름 중복·길이 등). 변환 흐름이 아니라 샘플 xlsx 생성에 쓰임 |
| Cell.cs | 읽은 셀 값 표현 |
| SheetConverter.cs | 시트 → JSON 변환. 헤더 해석(Name:type\[min..max\]?), 셀 값 검증 |
| RowSchema.cs | 헤더에서 읽은 스키마, Enum/Const 정의, 타입 표 |
| DefinitionSheets.cs | Enum, Const 시트와 패킷 정의 시트 5개 해석·검증 |
| RowGenerator.cs | 스키마 → C++/C# 행 코드 생성 |
| PacketSchema.cs / PacketGenerator.cs | 패킷 스키마와 패킷 C++ 구조체·enum·번호, 서버용 C# IPacket 구조체, 클라이언트용 C# Build/Parse/ToBytes, 크기 목록 |
| CodeGenCommon.cs | RowGenerator와 PacketGenerator가 함께 쓰는 C++/C# 예약어와 Windows 매크로 이름 검사, 식별자·네임스페이스 검사, 머리말 |
| ValueFormat.cs | 숫자·문자열 표기 규칙(JSON 출력용) |
| FileOutput.cs | 원자적 쓰기, 변경된 파일만 쓰기, 낡은 생성 파일 정리, 증분 상태(.XlsxConverter.stamp) |

엑셀 라이브러리는 ClosedXML 0.104.2(NuGet)뿐이고, 읽기·쓰기는 인터페이스 뒤에 있어 변환 로직은 ClosedXML을 직접 부르지 않습니다.

### 타입 표

RowSchema.cs의 표 하나가 기본 타입과 enum 기반형의 모든 매핑을 가집니다. 타입을 추가하거나 바꿀 때는 이 표만 고칩니다.

| 이름 | C++ 멤버 | C++ 상수 | C# | rapidjson | 정수 범위 |
| --- | --- | --- | --- | --- | --- |
| int | int32 | std::int32\_t | int | GetInt() | -2147483648 .. 2147483647 |
| uint | uint32 | std::uint32\_t | uint | GetUint() | 0 .. 4294967295 |
| float | double | double | double | GetDouble() | 해당 없음 |
| string | \_tstring | const TCHAR\* | string | GetString() | 해당 없음 |
| bool | bool | bool | bool | GetBool() | 해당 없음 |

enum 기반형(Base 열)은 uint8, uint16, uint32, int32이고 C++에서는 std::uintN\_t/std::int32\_t, C#에서는 byte/ushort/uint/int가 됩니다.

## 3. 엑셀 작성 규칙

엑셀 규칙은 C++ 버전과 같습니다. 기존 GameData 엑셀을 고치지 않고 그대로 씁니다.

### 시트와 행

시트 이름이 곧 `<시트>.json` 파일 이름, JSON 최상위 키, C++ 구조체 이름(`S<시트>Row`), C# 클래스 이름(`<시트>Row`, `<시트>Table`)이 됩니다. 예) Monster 시트 → Monster.json = `{"Monster":[...]}`

1행은 헤더, 2행부터 데이터입니다. 셀 앞뒤의 공백은 지우며, NBSP·전각 공백(U+3000)·U+FEFF도 공백으로 봅니다.

첫 칸이 `#`로 시작하는 행과 완전히 빈 행은 건너뜁니다. 주석 행에 다른 칸 값이 있으면 건너뛰되 변환이 끝난 뒤 stderr에 warning을 내므로, 데이터 행이 주석 처리된 것은 아닌지 확인하세요.

이름이 `_`로 시작하는 시트(예: \_README)는 변환하지 않습니다. (--sheets로 직접 지정하면 변환합니다.)

예약 시트 이름 Enums, Consts, EnumStrings, GeneratedCommon, 패킷 생성 파일 이름인 ZonePacketTypes, ZonePacketStructs는 생성 파일 이름과 겹치므로 시트 이름으로 쓸 수 없습니다.

### 헤더 문법

`필드명:타입[min..max]?`

| 요소 | 설명 |
| --- | --- |
| 필드명 | JSON 키이자 C++/C# 멤버 이름의 원본. 영문, 숫자, \_만, 숫자로 시작 불가. C++/C# 키워드는 쓸 수 없고 kFile, kTable, Specs, ToJSON, FromJSON, enumOk도 예약되어 있음 |
| 타입 | int, uint, float, string, bool, 또는 Enum 시트에 정의한 enum 이름 (예: Shape:ESkillShape) |
| \[min..max\] | int/uint의 허용 범위. 한쪽 생략 가능. 예) uint\[1..\], int\[-1000..1000\], uint\[..100\]. enum에는 쓸 수 없음 |
| ? | 선택 열. 빈 칸이면 오류 없이 JSON 필드를 생략하고, C++은 HasMember로 확인해 기본값을 둠 |

범위를 쓰지 않아도 값은 C++ 멤버 타입(int32/uint32)에 담기는 범위여야 합니다.

### 셀 값 규칙

| 타입 | 규칙 |
| --- | --- |
| int, uint | 정수여야 함. 소수는 오류. uint는 음수 불가. 헤더 범위 또는 타입 범위를 벗어나면 오류 |
| float | 숫자 |
| string | 빈 칸은 오류 (?가 있으면 필드 생략) |
| bool | TRUE/FALSE, 1/0, yes/no, y/n |
| enum | 멤버 이름이 정확히 일치해야 함. 오타는 허용 목록과 함께 오류 |

### Enum 시트

열: Enum, Name, Value(선택), Base(선택), Comment(선택)

같은 Enum 이름의 행들이 한 enum이 되고 순서는 시트의 행 순서입니다. Value를 비우면 앞 값 + 1이고 첫 값은 0입니다.

Base는 uint8(기본), uint16, uint32, int32 중 하나이며 enum의 첫 행에만 써도 됩니다. 값은 기반형 범위 안이어야 합니다.

멤버 이름 중복, 값 중복, C++ 키워드와 ERROR, DELETE 같은 Windows 매크로 이름은 오류입니다.

EnumComment 열(enum 설명)은 PacketEnum 시트에만 있고 enum의 첫 행에만 쓸 수 있습니다. 일반 Enum 시트에는 이 열이 없습니다.

### Const 시트

열: Name, Type(int, uint, float, string, bool), Value, Comment(선택)

Name은 식별자여야 하고 중복될 수 없습니다. 값은 Type에 맞아야 합니다.

### 패킷 정의 시트

패킷 프로토콜은 시트 5개로 정의합니다. 이 시트들은 JSON으로 변환되지 않고 코드 생성에만 쓰이며, 시트 이름은 예약어입니다. 5개를 한 파일에 모을 필요는 없고, GameData 폴더 전체에서 각각 한 번씩만 있으면 됩니다.

| 시트 | 내용 | 열 |
| --- | --- | --- |
| PacketEnum | 결과 코드 enum. Enum 시트와 같은 규칙 | Enum, Name, Value, Base, Comment, EnumComment |
| PacketConst | 프로토콜 상수 (Type: uint16, uint32, size). Comment가 비면 앞 상수의 설명에 이어 쓰임 | Name, Type, Value, Comment |
| PacketGroup | 패킷 번호 구간과 그 구간의 번호 열거형 | Group, Enum(C++ 열거형 이름), CsEnum, Min, Max, Section(구조체 파일의 구분 주석), Comment |
| Packet | 패킷 목록 | Name, Group, Id, Cs, Summary, Comment(구조체 @brief), Detail(@details) |
| PacketField | 패킷 본문의 필드. 시트 순서가 와이어 순서 | Packet, Name, Type, Len, Enum, Comment |

헤더(size, type 4바이트)는 자동으로 붙습니다. C++ 구조체는 `#pragma pack(1)`이고 크기는 sizeof static\_assert로 고정되므로, 시트와 구조체가 어긋나면 C++ 컴파일이 멈춥니다.

필드 Type: uint8, bool8, uint16, uint32, int32, uint64, float, char. bool8은 C++에서 uint8, C#에서 bool입니다. char는 Len(숫자 또는 PacketConst 이름)이 필요하고 Cs=None 패킷에서만 쓸 수 있습니다.

Enum 열은 필드 값의 의미를 나타내는 PacketEnum의 enum입니다. 필드 타입과 enum의 기반형이 같아야 합니다. C++ 멤버는 정수형 그대로이고 C#은 enum 타입이 됩니다.

Cs 열: Send(클라이언트가 보냄, Build 생성), Recv(클라이언트가 받음, Parse/ToBytes 생성), Both, None(기본값, 서버 간 패킷).

Id는 그룹의 Min..Max 안에 있어야 하고 전체에서 유일해야 합니다. 패킷을 추가할 때는 Packet 시트에 행을, PacketField 시트에 그 패킷의 필드 행을 추가하고 다시 실행하면 됩니다. 패킷·필드·enum·상수 이름이 C++ 키워드나 Windows 매크로 이름이면 오류이고, Type이 size인 상수는 2147483647 이하여야 합니다.

## 4. 명령행

```text
XlsxConverter <GameData 폴더 | file.xlsx> --out <dir> [--cpp-gen-out <dir>] [--cs-gen-out <dir>] [--cs-gen-namespace <name>]
                    [--cs-client-proto-out <dir>] [--cs-client-proto-namespace <name>] [--proto-sizes-out <file>]
                    [--sheets Name ...] [--incremental]
```

개발 중에는 `dotnet run --project XlsxConverter.csproj -- <인자...>`, 빌드 후에는 `XlsxConverter.exe`(또는 `dotnet XlsxConverter.dll`)로 실행합니다. 첫 인자가 폴더이면 그 폴더 바로 아래의 .xlsx를 모두 읽어 하나로 합쳐 처리하고, 파일이면 그 파일만 처리합니다. 규칙은 아래 "GameData 폴더 읽기"에 있습니다.

| 옵션 | 설명 |
| --- | --- |
| --out | JSON 출력 폴더 (필수) |
| --cpp-gen-out | C++ 헤더 출력 폴더 |
| --cs-gen-out | C# 출력 폴더. 시트 행/테이블 클래스, Enums/Consts, 서버용 패킷 C#(Packets.gen.cs, ProtocolTypes.gen.cs) |
| --cs-gen-namespace | C# 네임스페이스 (기본 GameDataRows). 점으로 구분한 식별자여야 함. 서버용 패킷 C#도 이 네임스페이스로 만듬 |
| --sheets | 일부 시트만 JSON 변환. 코드 생성 옵션·--incremental과 함께 쓸 수 없음 (일부 시트만으로 만든 헤더는 다른 구조체를 지우기 때문). 시트 이름 뒤에 마지막 인자로 .xlsx 파일이나 존재하는 경로가 오면 시트 이름이 아니라 입력 경로로 봅니다. 시트 이름이 하나도 없으면 오류 |
| --incremental | 모든 xlsx의 파일 이름/수정 시각/크기가 마지막 실행 때와 같고 출력 파일이 모두 있으면 엑셀을 열지 않고 종료 |
| --cs-client-proto-out | 클라이언트(뷰어)용 패킷 프로토콜 C# 출력 폴더. --cs-gen-out과 다른 폴더여야 함 (3절 패킷 정의 시트) |
| --cs-client-proto-namespace | 클라이언트 패킷 C# 네임스페이스 (기본은 --cs-gen-namespace 값). 같은 규칙으로 검사 |
| --proto-sizes-out | "<구조체> <바이트>" 목록 파일. C# 테스트가 C++ sizeof와 대조하는 입력. 폴더 없이 파일 이름만 줘도 현재 폴더에 만듦 |

종료 코드: 0 성공, 1 변환 실패(데이터 오류), 2 사용법/파일 오류

### 옵션을 주지 않으면 만들어지지 않는 것

--out만 주면 시트별 JSON만 만들어집니다. 코드와 목록 파일은 해당 출력 옵션을 준 경우에만 만들어지며, 옵션이 없다고 오류가 나지 않고 그냥 만들지 않습니다.

| 주지 않은 옵션 | 만들어지지 않는 것 | 함께 생략되는 처리 |
| --- | --- | --- |
| --out | 아무것도 만들지 않음 | 필수라서 없으면 사용법을 출력하고 종료 코드 2 |
| --cpp-gen-out | 시트별 <시트>.gen.h, Enums.gen.h, EnumStrings.gen.h, Consts.gen.h, GeneratedCommon.h, 패킷 C++ 헤더(ZonePacketTypes.gen.h, ZonePacketStructs.gen.h) | 다른 코드 옵션이 없으면 Const 시트와 패킷 정의 시트도 읽지 않음 |
| --cs-gen-out | 시트별 C# 행/테이블 클래스, Enums.gen.cs, Consts.gen.cs, 서버용 패킷 C#(Packets.gen.cs, ProtocolTypes.gen.cs) | 서버용 패킷 C#도 만들어지지 않음. --cs-gen-namespace는 클라이언트 패킷 C#의 기본 네임스페이스로는 쓰임 |
| --cs-client-proto-out | 클라이언트용 패킷 C# (Packets.gen.cs, ProtocolTypes.gen.cs) | --cs-gen-out만 줘도 클라이언트용 패킷 C#은 만들어지지 않음. 이 옵션이 따로 필요 |
| --proto-sizes-out | 패킷 크기 목록 파일(cpp\_packet\_sizes.txt) | 뷰어 테스트의 C# Size와 C++ sizeof 대조가 입력 파일 없이 건너뛰어짐 |
| --cs-gen-namespace | (생성물은 그대로) | C# 네임스페이스가 기본값 GameDataRows로 만들어짐 (서버용 패킷 C# 포함) |
| --cs-client-proto-namespace | (생성물은 그대로) | 클라이언트 패킷 C# 네임스페이스가 --cs-gen-namespace 값을 따름 |
| --sheets | (생성물은 그대로) | 대신 모든 시트(이름이 \_로 시작하는 시트 제외)를 변환. 이 옵션을 주면 코드 생성 옵션은 쓸 수 없음 |
| --incremental | (생성물은 그대로) | 변경 여부를 확인하지 않고 매번 엑셀을 읽어 변환 |

Enum 시트는 어떤 옵션이든 항상 읽어 enum 열의 멤버 이름을 검사합니다. 출력 옵션이 없어도 이 검사는 합니다.

패킷 정의 시트는 코드·목록 출력 옵션(--cpp-gen-out, --cs-gen-out, --cs-client-proto-out, --proto-sizes-out) 중 하나라도 있을 때만 읽습니다. 반대로 --cs-client-proto-out 또는 --proto-sizes-out을 줬는데 엑셀에 패킷 정의 시트가 없으면 오류(종료 코드 1)이고, --cpp-gen-out이나 --cs-gen-out만 준 경우에는 패킷 시트가 없어도 오류 없이 패킷 코드만 생략됩니다.

### 파일 쓰기 규칙

생성 파일은 BOM 있는 UTF-8, JSON은 BOM 없는 UTF-8입니다. 내용이 같은 파일은 다시 쓰지 않고(줄바꿈 차이는 무시) 파일은 임시 파일에 쓴 뒤 교체하므로 읽는 쪽이 반쥐 쓰인 파일을 보지 않습니다.

이번에 만들지 않은 `*.gen.h`/`*.gen.cs` 중 상단에 "자동 생성 파일" 표식이 있는 것만 지웁니다. 손으로 만든 파일은 건드리지 않습니다. 폴더마다 \*.gen.cs 한 묶음을 관리하므로 --cs-gen-out과 --cs-client-proto-out을 같은 폴더로 주면 서로의 파일을 지우게 되어 옵션 해석 단계에서 오류로 막습니다.

### 증분 실행

상태는 --out 폴더의 `.XlsxConverter.stamp`에 저장합니다. 버전 관리에서 제외하세요. --incremental이 아니어도 모든 출력이 끝나면 상태 파일을 씁니다(--sheets 실행은 제외).

상태에는 실행 인자(패킷 C# 네임스페이스는 비워 두면 --cs-gen-namespace를 따르므로 실제로 쓰이는 값을 기록하고, 그래서 --cs-gen-namespace만 바꿔도 다시 변환합니다), 실행 파일의 수정 시각, 각 xlsx의 파일 이름/수정 시각/크기, 만들어진 출력 파일 목록을 기록합니다. 인자가 바뀔거나 도구를 다시 빌드했거나 xlsx가 추가·삭제·수정됐거나 출력 파일이 하나라도 없으면 다시 변환합니다. 엑셀을 읽을 수 없으면(Excel에 열려 있는 경우 등) 같은 인자로 만든 출력이 있을 때 경고만 내고 종료 코드 0으로 끝나 기존 출력으로 빌드를 계속합니다. ClosedXML은 엑셀이 다른 프로그램에 열려 있어도 읽기 공유(FileShare.ReadWrite)로 열기 때문에 단순히 열려 있는 것만으로는 이 경고가 나지 않습니다.

### GameData 폴더 읽기

폴더를 주면 그 폴더 바로 아래의 \*.xlsx를 모두 읽습니다. 하위 폴더는 보지 않습니다. Excel 임시 파일(\~$로 시작)과 .로 시작하는 파일은 건너뛰고, 나머지는 파일 이름(대소문자 무시) 순서로 읽습니다.

파일이 여러 개여도 데이터 시트, Enum, Const, 패킷 정의 시트는 폴더 전체에서 하나로 모입니다. 파일을 나눠도 JSON과 생성 코드는 달라지지 않습니다.

시트 이름은 폴더 전체에서 유일해야 합니다. \_로 시작하는 시트(\_README 등)는 여러 파일에 있어도 됩니다. 같은 이름이 두 파일에 있으면 `<시트>: 시트 이름이 두 파일에 있습니다 (A.xlsx, B.xlsx)` 오류가 나고 파일은 쓰지 않습니다. 대소문자만 다른 이름(Monster / monster)도 출력 파일 이름이 같아지므로 오류입니다.

폴더에 xlsx가 하나도 없거나 한 파일을 열 수 없으면 파일 이름을 담은 오류로 중단합니다.

### 기능별 파일 나누기

시트가 늘어도 관리하기 쉽도록 기능 단위로 파일을 나눕니다. 현재 구성은 다섯 개입니다. 시트 하나는 한 파일에만 둡니다. 시트를 다른 파일로 옮기면 원본 파일에서 반드시 지우세요.

| 파일 | 시트 |
| --- | --- |
| Definitions.xlsx | Enum, Const, \_README |
| Hero.xlsx | Hero, Skill, Status, HeroSkill |
| Monster.xlsx | Monster, Boss, BossPhase, BossAction |
| World.xlsx | Spawn, Block |
| Packet.xlsx | PacketEnum, PacketConst, PacketGroup, Packet, PacketField |

### XlsxConverter.bat

수동 실행용 배치 파일은 GameData 폴더를 통째로 넘기고, 결과를 확인할 수 있게 종료 전에 멈춥니다. 파일은 BOM 없는 UTF-8이고 줄바꿈은 CRLF입니다. 첫 줄 다음의 `chcp 65001 >nul`로 콘솔을 UTF-8로 바꿔 한글 메시지가 깨지지 않습니다. C# 도구는 콘솔 출력을 항상 BOM 없는 UTF-8로 보내므로 이 줄이 있어야 접속 콘솔에서 한글이 정상으로 보입니다.

```bat
@ECHO OFF
chcp 65001 >nul
"K:\GitHub\CSharp\Game\Bin\AnyCPU\Release\XlsxConverter.exe" "K:\GitHub\CPP\Project\Game\GameData" --out "K:\GitHub\CPP\Project\Game\GameData" --cpp-gen-out "K:\GitHub\CPP\Project\Game\GameCommon\Generated" --cs-gen-out "K:\GitHub\CSharp\Game\GameCommon\Generated" --cs-gen-namespace Game.Generated --cs-client-proto-out "K:\GitHub\CPP\Project\Game\GameViewer\Core\Protocol\Generated" --cs-client-proto-namespace GameViewer.Core.Protocol --proto-sizes-out "K:\GitHub\CPP\Project\Game\GameViewer\Tests\cpp_packet_sizes.txt"
SET RC=%ERRORLEVEL%
ECHO.
IF %RC%==0 (ECHO [XlsxConverter] 완료) ELSE (ECHO [XlsxConverter] 실패. 종료 코드=%RC%)
PAUSE
EXIT /B %RC%
```

실행 코드를 먼저 두고 한글 설명 주석(::)은 EXIT /B 아래에 둡니다. 실행되지 않는 영역이라 인코딩과 상관없이 명령으로 해석되지 않습니다. 종료 코드를 RC에 저장해 성공/실패 메시지를 출력하고, PAUSE 후 `EXIT /B %RC%`로 같은 종료 코드를 돌려줍니다.

## 5. 생성되는 코드

생성물은 플랫폼과 상관없이 동일합니다. 도구를 C#으로 바꾸어도 C++ 헤더와 클라이언트 C# 코드의 형태는 그대로라 서버와 뷰어의 기존 코드를 고칠 필요가 없습니다.

### C++ 헤더 (--cpp-gen-out)

| 파일 | 내용 |
| --- | --- |
| <시트>.gen.h | GameDataRows::S<시트>Row 구조체 |
| Enums.gen.h | GameDataEnums의 enum class들. 다른 헤더에 의존하지 않음 |
| EnumStrings.gen.h | ToString(enum), TryParse(const TCHAR\*, enum&) |
| Consts.gen.h | GameDataConsts::k\<Name> constexpr 상수 |
| ZonePacketTypes.gen.h | 패킷 프로토콜 상수(PacketConst), enum class(PacketEnum), 패킷 번호 열거형(PacketGroup + Packet) |
| ZonePacketStructs.gen.h | <이름>Packet : PacketHeader 구조체와 sizeof static\_assert (Packet + PacketField) |
| GeneratedCommon.h | 위 파일들을 한곳에서 include하는 대표 헤더 (정의 헤더 먼저) |

행 구조체의 멤버 이름은 필드명의 첫 글자를 소문자로 한 이름이고 `Specs()`, `ToJSON()`, `FromJSON()`을 가집니다. Specs()는 헤더의 타입과 \[min..max\]를 그대로 담아 로더가 범위를 검증하는 데 쓰고, 선택 열은 FromJSON에서 HasMember로 확인합니다. enum 필드가 있는 시트는 enumOk 멤버를 가지며, JSON의 멤버 이름이 TryParse로 변환되지 않으면 false입니다.

로더는 GeneratedCommon.h 하나만 include합니다. SkillData.h처럼 `using GameDataEnums::ESkillShape;`로 생성된 enum을 이름만 가져오면 기존 코드가 그대로 동작합니다. ZonePacketStructs.gen.h의 구조체는 `#pragma pack(1)`이고 PacketHeader(4바이트) 다음에 PacketField 시트 순서대로 필드가 이어집니다. enum 열이 있는 필드도 C++ 멤버는 uint8 같은 정수형 그대로이고 bool8도 uint8입니다. 가변 길이 중계 패킷(ZoneRelayToZone 등)은 고정 부분만 생성되고, 뒤따르는 본문을 조립하는 함수는 ZonePacket.h에 손으로 남습니다.

### C# 코드 (--cs-gen-out, --cs-client-proto-out)

| 파일 | 내용 |
| --- | --- |
| <시트>.gen.cs | <시트>Row(FileName, TableKey, 속성, Validate)와 <시트>Table(JSON 최상위 객체). `--cs-gen-out` |
| Enums.gen.cs | enum들. `--cs-gen-out` |
| Consts.gen.cs | GameConsts의 public const 상수. `--cs-gen-out` |
| ProtocolTypes.gen.cs | ProtocolConst(partial), enum, 패킷 번호 열거형. 서버용은 `--cs-gen-out`, 클라이언트용은 `--cs-client-proto-out` |
| Packets.gen.cs | 서버용(`--cs-gen-out`): 모든 패킷의 IPacket 구조체(Size, Type, Write, TryRead). 클라이언트용(`--cs-client-proto-out`): Build.<패킷>(...) 송신, <패킷>.Parse(byte\[\]) 수신, <패킷>.ToBytes() 서버 쪽 레이아웃 직렬화 |

속성 이름이 JSON 키와 같아 별도 특성이 필요 없고, string은 `string.Empty`, enum은 첫 멤버로 초기화됩니다. `Validate(where, errors)`는 헤더의 \[min..max\]를 벗어난 값을 errors에 덧붙이고, `<시트>Table.Validate(errors)`는 모든 행을 검사합니다.

JSON에는 enum이 멤버 이름 문자열로 저장되므로 역직렬화에 문자열 enum 변환기가 필요합니다. (System.Text.Json의 `JsonStringEnumConverter` 또는 Newtonsoft의 `StringEnumConverter`) enum 이름이 <시트>Row/<시트>Table 이름과 겹치거나 필드명이 FileName, TableKey, Validate와 겹치면 생성 단계에서 오류입니다.

### 패킷 코드

클라이언트용 C#에서 Cs 열이 Send/Both인 패킷은 `Build.<패킷>(필드...)`가 되고 크기 상수 `Build.<패킷>Size`가 함께 생깁니다. Recv/Both인 패킷은 필드를 담는 클래스와 `Parse(byte[])`, `ToBytes()`가 되고 `Size` 상수를 가집니다. ToBytes()는 테스트의 가짜 서버와 오프라인 데모가 씁니다.

이름 규칙: enum의 앞 E와 상수의 앞 k는 떼고(EGatewayResult → GatewayResult, kMaxClientPacketBytes → MaxClientPacketBytes), 필드 이름은 첫 글자만 대문자로 합니다(charId → CharId). bool8은 bool, Enum 열이 있는 필드는 그 enum 타입이 됩니다. Build의 매개변수 이름 `w`는 생성 코드의 지역 변수와 겹치므로 필드명으로 쓸 수 없습니다.

클라이언트용 생성 패킷 코드는 `PacketWriter(ushort type, int size)`와 `PacketReader(byte[] data, int offset)`를 호출합니다. 두 클래스는 생성물이 아니라 소비 프로젝트(뷰어의 Core\\Protocol)에 손으로 두어야 하며, 같은 네임스페이스에서 참조되어야 합니다. ProtocolConst는 partial이라 시트에 없는 값(HeaderSize, 시트에 없는 EntityType)은 손으로 쓴 PacketTypes.cs가 같은 클래스에 더합니다.

클라이언트용 패킷 C#의 네임스페이스는 --cs-client-proto-namespace(기본은 --cs-gen-namespace 값)이고 서버용은 --cs-gen-namespace입니다. char 배열은 클라이언트용 C# 생성을 지원하지 않으므로 해당 패킷은 Cs=None이어야 합니다. (서버용 C#에는 고정 길이 UTF-8 문자열 필드로 나옵니다.) --proto-sizes-out으로 만든 cpp\_packet\_sizes.txt(한 줄에 `<이름>Packet <바이트>`)를 뷰어 테스트가 읽어 C# 클래스의 Size와 C++ sizeof를 대조합니다.

서버용 패킷 C#(`--cs-gen-out`)은 Cs 열과 무관하게 모든 패킷을 IPacket을 구현하는 구조체로 만듭니다. 모든 필드는 리틀 엔디언이고 헤더 \[size:u16 (헤더 포함)\]\[type:u16\]로 시작합니다. enum 필드는 원래 정수 형식이고 char 배열은 고정 길이 UTF-8 문자열 필드입니다. 손으로 쓰는 IPacket, PacketIO는 같은 네임스페이스(--cs-gen-namespace)에 있어야 합니다. 서버 프로젝트는 다음처럼 나눕니다.

- Game.Generated(GameCommon\\Generated): XlsxConverter가 만든 \*.gen.cs만 둡니다.
- Game.ZoneCommon(GameCommon\\ZoneCommon): IPacket, PacketIO, 중계 패킷(ZonePacket.cs)와 서버 공용 코드를 둡니다. 이 프로젝트가 Generated 폴더의 Packets.gen.cs와 ProtocolTypes.gen.cs를 파일 링크로 함께 컴파일하고, Game.Generated는 이 두 파일을 컴파일하지 않습니다. 생성 파일이 IPacket을 쓰고 Game.ZoneCommon이 Game.Generated를 참조하므로 순환 참조를 피하는 구성입니다.
- 패킷 구조체를 쓰는 프로젝트(GatewayServer, ZoneServer)는 Game.ZoneCommon을 참조합니다.

## 6. 인코딩

생성 파일은 BOM 있는 UTF-8(줄바꿈 LF), JSON은 BOM 없는 UTF-8입니다. XlsxConverter 소스(.cs, .csproj)는 BOM 있는 UTF-8로 저장합니다.

C# 문자열은 항상 UTF-16이고 파일을 쓸 때 인코딩을 명시하므로, C++ 버전의 `U8("...")` 매크로나 `/utf-8` 컴파일러 옵션이 필요 없습니다. 한글 리터럴을 그냥 써도 생성 파일의 한글 주석이 깨지지 않습니다. 콘솔 출력(stdout/stderr)도 BOM 없는 UTF-8 스트림으로 보내므로 빌드 로그와 파이프에서 한글 메시지가 깨지지 않습니다.

ClosedXML이 돌려주는 셀 값은 아래 규칙으로 변환됩니다.

| 엑셀 셀 | 변환 |
| --- | --- |
| 빈 칸 | 빈 값 |
| 텍스트 | 문자열 그대로 |
| 숫자 | 숫자. 2^53 이하 정수는 자릿수 그대로, 그 외는 최단 왕복 표기 |
| 불리언 | 불리언 |
| 날짜/시간 | ISO 8601 문자열. 자정이면 yyyy-MM-dd, 아니면 yyyy-MM-ddTHH:mm:ss |
| 기간(TimeSpan) | 일 단위 숫자 |
| 오류값(#N/A 등) | 오류 표기 그대로의 문자열. 숫자/불리언 열에서 "숫자가 아님" 오류로 드러남 |
| 수식 | 저장된 계산 결과. 계산 결과가 없으면 ClosedXML이 계산 |

숨김 시트도 읽습니다. 차트 시트처럼 셀이 없는 시트는 워크시트 목록에 없어 이름도 나오지 않습니다.

## 7. 빌드 자동화

### Pre-Build Event

서버 빌드 시 엑셀 수정이 자동 반영되도록 C# 서버의 Game.Generated 프로젝트(GameCommon\\Generated\\Game.Generated.csproj)에 MSBuild 타깃으로 설정합니다. 이 타깃은 이 저장소의 GameData 엑셀로 이 프로젝트 폴더의 \*.gen.cs(시트 클래스, Enums/Consts, 서버용 패킷)만 만듭니다. C++ 저장소와 뷰어 쪽 출력(헤더, 클라이언트 프로토콜, 크기 목록)은 XlsxConverter.bat이 만듭니다. 입력 엑셀과 생성 코드가 한 저장소 안에서 일치하도록 --out도 같은 GameData 폴더를 씁니다.

Game.Generated.csproj에 넣는 내용입니다.

```xml
<ItemGroup Condition="'$(SkipXlsxConverterBuild)' != 'true'">
  <!-- 변환기를 먼저 빌드하도록 순서만 건다(어셈블리는 참조하지 않는다) -->
  <ProjectReference Include="../../XlsxConverter/XlsxConverter.csproj" ReferenceOutputAssembly="false" />
</ItemGroup>

<PropertyGroup>
  <CsGameDir Condition="'$(CsGameDir)' == ''">$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..\..'))</CsGameDir>
  <XlsxConverterExe Condition="'$(XlsxConverterExe)' == ''">$(CsGameDir)\Bin\$(Configuration)\XlsxConverter.exe</XlsxConverterExe>
</PropertyGroup>

<Target Name="GenerateFromXlsx" BeforeTargets="BeforeBuild" Condition="Exists('$(XlsxConverterExe)')">
  <Exec Command="&quot;$(XlsxConverterExe)&quot; &quot;$(CsGameDir)\GameData&quot; --out &quot;$(CsGameDir)\GameData&quot; --cs-gen-out &quot;$(MSBuildProjectDirectory)&quot; --cs-gen-namespace Game.Generated --incremental" />
  <!-- 생성 파일이 없던 첫 빌드를 위해, 변환기가 끝난 뒤 컴파일 목록을 다시 채운다 -->
  <ItemGroup>
    <Compile Remove="*.gen.cs" />
    <Compile Include="*.gen.cs" Exclude="Packets.gen.cs;ProtocolTypes.gen.cs" />
  </ItemGroup>
</Target>
<Target Name="WarnNoXlsxConverter" BeforeTargets="BeforeBuild" Condition="!Exists('$(XlsxConverterExe)')">
  <Warning Code="XLSXCONV" Text="XlsxConverter.exe를 찾지 못해 생성 코드를 갱신하지 않았습니다: $(XlsxConverterExe)" />
</Target>
```

스크립트 형태의 경로는 속성이라 -p:CsGameDir=... 처럼 바꿀 수 있고, CsGameDir 기본값은 이 프로젝트 기준의 Game 폴더입니다. Directory.Build.props가 Game의 모든 프로젝트 출력을 Game\\Bin<구성>\\ 한 곳에 모으고, exe 경로에 $(Configuration)이 들어 있어 Debug 빌드는 Bin\\Debug\\XlsxConverter.exe, Release 빌드는 Bin\\Release\\XlsxConverter.exe를 씁니다. exe가 없으면 XLSXCONV 경고만 내고 기존 생성 파일로 빌드합니다. 변환 도구는 .NET 8 런타임이 있는 PC에서만 실행되므로 빌드 서버와 팀원 PC에 .NET 8 SDK(또는 Desktop Runtime)를 설치합니다.

| 상황 | 동작 |
| --- | --- |
| 모든 xlsx 변경 없음 | up to date로 엑셀을 열지 않고 종료, 빌드 계속 |
| xlsx 변경·추가·삭제됨 | 다시 변환. 내용이 같은 생성 파일은 다시 쓰지 않아 재빌드가 늘지 않음 |
| 생성 파일이 없는 첫 빌드 | 변환기가 파일을 만든 뒤 컴파일 목록에 포함해 같은 빌드에서 반영 |
| 데이터 오류 | 종료 코드 1로 빌드 실패. 오류 목록이 출력 창에 나오고 파일은 쓰지 않음 |
| 엑셀을 읽을 수 없음 | 같은 인자로 만든 출력이 있으면 경고 후 종료 코드 0 |
| XlsxConverter.exe 없음 | XLSXCONV 경고만 내고 기존 생성 파일로 빌드 |

### 프로젝트 종속성 (빌드 순서)

Pre-Build가 쓰는 XlsxConverter.exe가 Game.Generated보다 먼저 빌드되게 합니다. Game의 모든 프로젝트가 같은 Bin 폴더에 출력하므로, 전체 다시 빌드에서 변환기 exe가 지워지거나 쓰이는 중에 이벤트가 실행되지 않도록 순서를 고정합니다.

- Game.Generated.csproj의 ProjectReference(위 코드)가 빌드 순서만 건다. 어셈블리는 참조하지 않으므로(ReferenceOutputAssembly=false) 솔루션 탐색기의 빌드 종속성 설정은 필요 없다.
- XlsxConverter는 Core.Util을 프로젝트 참조로 가지므로 함께 빌드된다.
- 서버만 빌드하는 PC에서는 -p:SkipXlsxConverterBuild=true로 변환기 빌드를 건너뛸 수 있다.

주의할 점은 세 가지입니다.

- exe 경로는 빌드 구성을 따라갑니다. Debug로 빌드하면 XlsxConverter도 Debug로 만들어져 Bin\\Debug\\에 나오고, 이벤트도 그 exe를 씁니다.
- 전체 다시 빌드를 해도 변환기가 먼저 끝난 뒤 이벤트가 실행되고, 컴파일 목록이 그 뒤에 채워지므로 생성 파일이 없는 상태에서도 정상으로 빌드됩니다.
- 처음 빌드할 때는 ClosedXML을 NuGet에서 복원하므로 네트워크가 필요합니다.

변환기를 먼저 빌드하지 않는 구성(SkipXlsxConverterBuild)에서는 XlsxConverter를 미리 한 번 빌드해 두고, 소스를 고칠 때마다 직접 다시 빌드합니다.

### 생성 헤더를 탐색기에 표시 (선택)

생성 헤더는 프로젝트 항목으로 등록하지 않아도 #include 경로만 맞으면 컴파일됩니다. 시트를 추가하면 XlsxConverter가 <시트>.gen.h를 만들고 GeneratedCommon.h에 include 줄을 넣으므로 로더 쪽에서 고칠 것은 없습니다. 솔루션 탐색기에 보이게 하려면 C++ 버전 가이드와 같이 .vcxproj에 헤더마다 항목을 하나씩 두고 .vcxproj.filters에 Generated 필터를 정의합니다. 변환 도구가 .vcxproj를 직접 고치는 방식은 쓰지 않습니다. 실행 중인 VS와 충돌하고 팀원의 프로젝트 파일 수정과 병합 문제가 생길 수 있기 때문입니다.

C# 소비 프로젝트(.csproj)는 SDK 스타일이면 폴더 안의 \*.cs를 자동으로 컴파일하므로 --cs-gen-out, --cs-client-proto-out 폴더를 프로젝트 폴더 아래에 두면 항목을 따로 등록할 필요가 없습니다. 폴더가 프로젝트 밖이면 `<Compile Include="..\GameCommon\Generated\CSharp\**\*.cs" />`로 추가합니다.

## 8. 도입 절차

1. `Core`와 `XlsxConverter` 폴더를 같은 부모 아래에 둡니다. XlsxConverter는 `../Core/Util/Core.Util.csproj`를 참조합니다.
2. .NET 8 SDK를 설치하고 `dotnet build XlsxConverter.csproj -c Release`(또는 `XlsxConverter.sln`을 Visual Studio에서 열어 빌드)로 도구를 만듭니다. ClosedXML은 처음 빌드할 때 NuGet에서 복원됩니다.
3. 회귀 테스트(`dotnet run --project tests`)를 실행해 마지막 줄에 `ALL PASSED`가 나오는지 확인합니다. (9절)
4. 기능별 xlsx를 GameData 폴더에 두고 XlsxConverter.bat을 실행해 JSON과 생성 파일을 만들고, 생성 파일의 한글 주석이 정상인지 확인합니다. 기존 C++ 도구가 만든 생성물과 같은 엑셀에서 만든 결과를 비교해, 내용이 같으면 이후 다시 쓰지 않음을 눈으로 확인하기 쉽습니다.
5. 로더는 GeneratedCommon.h 하나만 include합니다. 솔루션을 빌드합니다.
6. (선택) C# 서버는 7절의 Game.Generated.csproj 타겟을 넣어 빌드 때 \*.gen.cs를 갱신합니다. 처음 빌드하는 PC에서는 \*.gen.cs가 없어도 전체 다시 빌드가 되는지 확인합니다. 인자 이름은 옵션 표(3절)를 따릅니다.

패킷 프로토콜 생성을 쓰는 경우는 C++ 도구로 이미 적용했다면 추가 작업이 없습니다. 처음 도입하는 경우에만 아래를 합니다.

- GameCommon\\ZoneCommon\\ZonePacketTypes.h와 ZonePacket.h를 전달/도우미만 남긴 버전으로 교체합니다.
- 뷰어의 Core\\Protocol\\PacketTypes.cs를 축소본으로 교체하고 Packets.cs를 삭제합니다. PacketWriter, PacketReader는 이 폴더에 남겨 둡니다.
- Core\\Protocol\\Generated\\ 폴더와 새 Tests\\cpp\_packet\_sizes.txt를 넣습니다.
- 서버 코드의 #include는 바꾸지 않습니다.

## 9. 테스트와 문제 해결

### 회귀 테스트

`tests/`의 XlsxConverter.Tests는 콘솔 프로그램입니다. ZipArchive 기반 테스트용 xlsx 리더/라이터(SimpleXlsx)로 엑셀을 즉석에서 만들어 ConverterApp.Run을 프로세스 안에서 실행하고 결과를 검사합니다. 실행은 `dotnet run --project tests` 또는 Visual Studio에서 XlsxConverter.Tests를 시작 프로젝트로 지정해 실행하고, 마지막 줄에 `ALL PASSED`가 나와야 합니다. 테스트 프로젝트도 메인 프로젝트를 참조하므로 ClosedXML을 NuGet에서 복원할 수 있어야 빌드됩니다.

| 구역 | 확인 내용 |
| --- | --- |
| 값 규칙 | 숫자·문자열 표기(ValueFormat) 단위 검사 |
| 전체 흐름 | 데이터 시트·Enum·Const·패킷 시트 5개로 JSON, C++, C#, 크기 목록 생성, 내용이 같은 파일은 다시 쓰지 않음, 낡은 생성 파일 정리, --incremental |
| 오류 | 헤더 문법, Enum/Const, 셀 값(범위·enum 오타·정수 아님·bool 아님·빈 칸), 예약 시트 이름, C++ 키워드·Windows 매크로 이름, 패킷 시트 오류. 종료 코드 1, 메시지 포함 여부, 출력 파일 없음 |
| 사용법 | 인자 오류, 옵션 조합 제한. 종료 코드 2 |
| 폴더 규칙 | 여러 xlsx 합치기, 시트 이름 중복(대소문자 차이 포함), 파일 이름 순서 |
| 라이터 규칙 | 새 .xlsx를 쓸 때의 시트 이름 검증 |

생성 코드의 형식을 바꾼 경우에는 관련 테스트의 기대값을 함께 고칩니다. 오류 메시지를 바꾸면 그 문장을 확인하는 테스트도 함께 고칩니다.

### 문제 해결

| 증상 | 원인과 조치 |
| --- | --- |
| 생성 파일의 한글 주석이 깨짐 | 에디터가 BOM 없는 UTF-8이나 CP949로 다시 저장했거나, 빌드 로그만 깨진 경우임. 도구는 항상 BOM 있는 UTF-8로 쓰므로 생성 파일을 직접 고치지 않고 다시 실행. 콘솔만 깨지면 bat에 `chcp 65001 >nul`이 있는지 확인 |
| C2440 GameDataEnums::X → X | 서버 쪽 enum 정의가 생성 enum과 다른 타입. SkillData.h처럼 `using GameDataEnums::X;`로 가져오게 함 |
| 종료 코드 1과 오류 목록 | 엑셀 데이터 오류. `시트!행 열` 위치를 고침 |
| 종료 코드 2 | 인자 또는 파일 오류. 경로와 인자 확인 |
| 엑셀을 고쳤는데 up to date | 인자가 같고 엑셀 수정 시각/크기가 같은 경우. --incremental을 빼고 실행해 강제 변환 |
| --cpp-gen-out과 --sheets 오류 | 함께 쓸 수 없음. 코드 생성은 모든 시트로 해야 함 |
| --incremental과 --sheets 오류 | 함께 쓸 수 없음. 일부 시트 실행은 상태 파일을 쓰지 않음 |
| --cs-client-proto-out 또는 --proto-sizes-out 사용 시 "패킷 정의 시트가 없습니다" | GameData 폴더의 xlsx에 PacketEnum, PacketConst, PacketGroup, Packet, PacketField 시트가 없음. 시트를 추가하거나 해당 옵션을 빼세요 |
| Packet!행 오류: Id 범위 밖, Id가 같음 | PacketGroup의 Min/Max 구간과 Packet의 Id를 확인. 패킷 번호는 전체에서 유일해야 함 |
| C2338 …Packet: wire size differs from the Packet sheet | 생성 헤더를 손으로 고친 경우이거나 오래된 생성물. 엑셀을 고치고 XlsxConverter를 다시 실행 |
| --cs-client-proto-out 오류: --cs-gen-out과 다른 폴더여야 함 | C# 생성물 두 묶음이 같은 폴더에 섞이면 서로의 \*.gen.cs를 지움. 폴더를 분리 |
| 빌드에서 PacketWriter/PacketReader를 찾지 못함 | 두 클래스는 생성물이 아니라 소비 프로젝트에 손으로 두는 코드임. 같은 네임스페이스(--cs-client-proto-namespace)에 있는지 확인 |
| 시트 이름이 두 파일에 있습니다 (A.xlsx, B.xlsx) | 같은 시트가 두 xlsx에 있음. 한 파일에서 지움. \_로 시작하는 시트는 해당 없음 |
| no .xlsx file in: ... | 첫 인자 폴더에 .xlsx가 없음. 경로 확인. \~$로 시작하는 임시 파일과 하위 폴더의 파일은 읽지 않음 |
| 서버 시작 시 game data does not fit the zone ... spawn center of monster N is inside an obstacle | 변환 도구 오류가 아니라 서버의 시작 검증. Spawn 시트의 해당 몬스터 스폰 중심 좌표가 Block 맵의 막힌 칸 안에 있음. 좌표를 옮기고 다시 변환 |
| XlsxConverter.bat 실행 시 한글이 깨지거나 주석이 명령으로 실행됨 | 배치 파일이 BOM 없는 UTF-8이 아니거나 chcp 65001이 없음, 또는 한글 주석이 EXIT /B 위에 있음. 실행 코드를 위, 주석을 EXIT /B 아래로 둠 |
| NU1101 ClosedXML을 찾을 수 없음 | NuGet 복원 실패. 빌드 서버가 nuget.org(또는 사내 미러)에 접근할 수 있는지 확인하고, 오프라인이면 미리 복원한 패키지 폴더를 신뢰된 소스로 등록 |

### 알려진 제한 사항

- 가변 길이 중계 패킷(ZoneRelayToZone 등)은 고정 부분만 생성됩니다. 뒤따르는 본문을 조립하는 함수는 ZonePacket.h에 손으로 둡니다.
- char 배열 필드는 클라이언트용 C# 생성을 지원하지 않으므로 해당 패킷은 Cs=None(서버 간 패킷)이어야 합니다. 서버용 C#에는 고정 길이 UTF-8 문자열 필드로 생성됩니다.
- --sheets는 코드 생성 옵션과 함께 쓸 수 없습니다. 코드 생성은 항상 모든 시트로 합니다.
- --cs-client-proto-out은 --cs-gen-out과 다른 폴더여야 합니다. 같은 폴더에 두면 서로의 \*.gen.cs를 지웁니다.
- Spawn 좌표와 Block 맵의 충돌 같은 시트 사이의 내용 검증은 변환 도구가 아니라 서버가 시작할 때 합니다.
- ClosedXML은 다른 프로그램이 열어 둔 xlsx도 읽지만, 저장되지 않은 편집 내용은 보이지 않습니다. 변환 전에 저장하세요.

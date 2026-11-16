# Game — MMORPG 필드 전투 서버 (C# 포팅)

C++ `CPP_Project/Game`의 필드 전투 서버를 C#(.NET 8)으로 포팅한 것입니다. 네트워크/유틸은 기존 `Library/Core`를 씁니다.

## 폴더 배치 (중요)

`Library/Core`가 `Game`과 **같은 부모 아래**에 있어야 합니다.

```
csharp/
  Library/Core/    Core.Util, Core.Net (C# 네트워크/유틸 라이브러리)
  Game/            ← 이 폴더 (Game.sln: XlsxConverter, Core, 게임 서버 프로젝트)
```

Game 폴더 구조:

```
Game/
  Game.sln
  Config/                  gatewayserver_config.json, zoneserver_config.json
  GameData/                기획 엑셀과 XlsxConverter가 만든 JSON (Hero, Monster, Spawn, Skill, Block 등)
  GameCommon/
    Generated/             Game.Data      생성 행/테이블 클래스(*.gen.cs), GameDataJson
    ZoneCommon/            Game.Protocol  Packets.gen.cs, ProtocolTypes.gen.cs, ZonePacket.cs
    ZoneCore/              Game.ZoneCore  존 틱 루프, AOI, 전투, 차단 맵, 몬스터/보스
  ServerCommon/            Game.ServerCommon  JsonConfig, MainArgs, PacketSend
  GatewayServer/           Game.GatewayServer
  ZoneServer/              Game.ZoneServer
  Game.Tests/              테스트 러너
  Game.ViewerIT/           뷰어(GameViewer/Core) 연동 통합 테스트
```

폴더 이름과 달리 프로젝트(어셈블리/네임스페이스) 이름은 `Game.*` 그대로다.

## 구성

| 프로젝트 | 내용 |
|---|---|
| Game.Data | XlsxConverter가 만든 행/테이블 클래스, `GameDataJson`(엄격 JSON 로더) |
| Game.Protocol | 패킷 구조체(와이어 크기는 C++ static_assert와 동일), `ZonePacket` 중계 패킷 |
| Game.ZoneCore | 존 틱 루프, AOI, 이동/공격/스킬/상태효과/투사체, 차단 맵(A*), 몬스터 AI, 보스 |
| Game.ServerCommon | JSON 설정 읽기/쓰기, 명령행 인자 |
| Game.ZoneServer | 전투 존 + Gateway 접속(등록/하트비트/재접속/입장·퇴장·중계) |
| Game.GatewayServer | 클라이언트/존 서버 이중 리슨, 라우터, 정원 관리, 하트비트 감시 |
| Game.Tests | 콘솔 러너(단위 + 소켓 E2E) |

## 빌드/실행

```
dotnet build Game.sln
dotnet run --project Game.Tests            # 전체 테스트 (인자로 이름 일부를 주면 해당 스위트만)

dotnet run --project GatewayServer -- --config=Config/gatewayserver_config.json
dotnet run --project ZoneServer   -- --config=Config/zoneserver_config.json --zone-id=1
```

- 설정 파일 키와 명령행 옵션은 C++ 버전과 같습니다. `--dump-config=경로`로 기본값 파일을 만듭니다.
- `GameDataDir`(기본 `GameData`)에 XlsxConverter가 만든 JSON(Hero/Monster/Spawn 필수, Skill/Status/HeroSkill/Boss*/Block/Drop 선택)을 두세요. 빈 문자열이면 데이터 없이(기본 영웅 1명, 몬스터 없음) 뜹니다.
- Gateway는 세션당 초당 `ClientMaxPacketsPerSec`(기본 120, 0이면 제한 없음)를 넘는 클라이언트를 끊는다. 존 서버의 종류별 빈도 제한(`RequestGuard`) 합계보다 커야 한다.
- Gateway의 `AuthenticateClient`는 C++과 마찬가지로 임시(항상 통과)입니다. 클라이언트 포트를 외부에 열기 전에 구현하세요.

## 전투 규칙

- **MP/재생**: 영웅의 `MaxMp`(0이면 MP 없음)와 스킬의 `MpCost`. MP는 시전이 성공하는 시점에 깎이며 시전이 취소돼도 돌려주지 않는다. 부족하면 `NotEnoughMp`로 거부한다. `HpRegen`/`MpRegen`은 초당 회복량이고 `RegenIntervalMs`(1000) 주기로 적용한다. 체력 재생은 피해를 주고받은 뒤 `RegenCombatDelayMs`(5000) 동안 멈추고 MP 재생은 멈추지 않는다. 내 체력/MP는 `ZcStatNotify`로 본인에게만 간다.
- **위협(어그로)**: 피해량만큼 쌓이며 스킬의 `ThreatBonusPercent`로 보정한다. 치유는 치유량의 `HealThreatPercent`(50)%를 치유 대상을 어그로 표에 가진 `HealThreatRange`(20) 안 몬스터에게 치유자의 위협으로 더한다. `TauntMs`가 있는 스킬은 시전자를 표 맨 위로 올리고 그 시간 동안 대상으로 고정한다. 보스와 복귀 중인 몬스터는 도발되지 않는다.
- **밀치기/끌어당기기/속박**: `KnockbackDistance` 양수는 밀어내고 음수는 끌어당긴다(기준점은 Area면 중심, 아니면 시전자). 장애물에서 멈추고 끌어당기기는 `PullStopDistance`(1.0)에서 멈추며 보스는 면역이다. `Root` 상태는 이동만 막는다. 쫓던 몬스터는 포기하거나 집으로 순간이동하지 않고 풀릴 때까지 기다린다.
- **드롭/줍기**: `Drop` 표(몬스터별 확률 1000분율, 수량 범위)로 몬스터가 죽을 때 지면 아이템 엔티티(`ItemIds.Base` 대역, 스폰 통지의 entityType=3, templateId=아이템 ID, hp=maxHp=수량)를 만든다. 처치자(플레이어)가 `DropOwnerMs` 동안 먼저 줍고 `DropLifetimeMs`가 지나면 사라진다. 존당 `MaxGroundItems`가 상한이고 소환 몬스터는 드롭하지 않는다. 가방(`Inventory`)은 메모리에만 있다(`MaxInventorySlots` 종류, 종류당 `MaxItemStack`).
- **채팅**: `ZcChatReq`의 채널은 Say(내 시야 안 + 본인), Zone(존 전체, 플레이어마다 `ChatZoneCooldownMs` 쿨다운), Whisper(같은 존의 `targetId` + 본인 에코). 본문은 고정 128바이트 UTF-8 필드(한글 약 42자)이며 앞뒤 공백을 자르고, 비었거나 제어/서식 문자(줄바꿈, 제로 폭 공백, 방향 전환), 대체 문자(잘못된 UTF-8)가 있으면 `InvalidText`로 거부한다. 서버는 검증한 문자열을 다시 인코딩해 보내므로 클라이언트 바이트를 그대로 퍼뜨리지 않는다. 귓속말 대상이 없거나 자기 자신이거나 몬스터 ID여도 같은 `NoTarget`이라 존재 여부가 새지 않는다. 형식 위반(없는 채널, 잘못된 본문)은 위반 점수도 올리고, 채팅은 종류별 빈도 제한(2/s, 버스트 5)을 받는다. 보낸 사람 이름은 싣지 않는다(`senderId`만). 존을 넘는 채팅(월드/길드/파티)과 차단·금칙어는 아직 없다.
- **요청 빈도 제한**: 종류별(`RequestKind`) 토큰 버킷(이동 20/s 버스트 40, 공격·스킬 8/s 버스트 16, 줍기 10/s 버스트 20, 핑 2/s 버스트 6)으로 넘치는 요청을 버리고 위반 점수를 쌓는다. 점수가 `ViolationLimit`(60)에 이르면 `ZoneKickNotify(RateLimited)`로 존에서 내보낸다(초당 5점 감소). 비정상 값(NaN/Inf, 크기 부족)은 가중치가 크다. `RequestGuardEnabled=false`로 끈다.
- **지표**: `MetricsIntervalMs`(30000, 0이면 끔)마다 `[Metrics]` 한 줄을 남긴다. 인원/몬스터/지면 아이템 수, 틱 평균·최대와 지연 구간 분포(1ms·2ms·5ms·10ms·25ms·50ms 경계), 수신/송신/중계/팬아웃/버린 요청/비정상/추방/줍기 수.

## C++ 대비 차이

- 소켓 I/O는 Core.Net(.NET 비동기)이라 `WorkerThreadCnt` 설정은 읽기만 하고 쓰지 않습니다.
- 스폰/부활 위치 난수는 `System.Random`(존 ID 시드)이라 C++ `mt19937_64`와 비트 단위로 같지 않습니다.
- A* 동률 처리 순서가 달라 같은 비용의 다른 경로가 나올 수 있습니다(경로 길이는 같습니다).
- 캐시라인 패딩 등 C++ 레이아웃 최적화는 옮기지 않았습니다.
- 존 서버가 Gateway 패킷을 받을 때 선언된 패킷 크기가 상한을 넘으면, 상한 검사는 패킷이 모두 도착한 뒤에 합니다(Core.Net의 프레이밍이 선검사 훅을 제공하지 않음).

## 전투 뷰어(GameViewer) 연동

뷰어는 Gateway 클라이언트 포트에 접속해 로그인, 존 입장, 이동/공격/스킬을 같은 와이어 프로토콜로 주고받는다.
핑 측정(`ZcPingReq` 2021, `ZcPongNotify` 2022)은 존이 응답하며, 응답은 요청한 클라이언트에게만 간다.

`Game.ViewerIT`는 뷰어의 `Core` 소스를 그대로 컴파일해 `ViewerSession`과 `AutoPilot`이 이 서버에 실제 소켓으로 붙어
로그인, 입장, 핑, 자동 전투(추적, 기본 공격, 스킬, 처치)까지 가는지 확인한다. 솔루션에는 넣지 않았다.

    dotnet run --project Game.ViewerIT -p:ViewerCore=<뷰어>/Core

서버는 시작할 때 GameData를 검증하므로 다음을 지킨다.
- 스킬/보스가 쓰는 상태 효과 ID는 모두 Status 표에 있어야 한다.
- 영웅 스킬의 `MpCost`는 그 영웅의 `MaxMp` 이하여야 하고, 몬스터·보스가 쓰는 스킬의 `MpCost`는 0이어야 한다.
- `Drop`의 몬스터 ID는 Monster 표에 있어야 하고 `MinCount <= MaxCount <= MaxItemStack`이어야 한다.
- Boss 표가 있으면 BossAction 표(비어 있어도)도 있어야 한다.
- 스폰 중심은 장애물(Block) 안에 있으면 안 된다.
`GameData/`는 이 조건을 맞춘 샘플이다.

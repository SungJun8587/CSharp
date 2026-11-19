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
    Generated/             Game.Generated 생성 행/테이블 클래스(*.gen.cs). 생성 파일만 둔다. Packets.gen.cs, ProtocolTypes.gen.cs는 Game.ZoneCommon이 컴파일
    ZoneCommon/            Game.ZoneCommon  JsonConfig, MainArgs, PacketSend
                           ZonePacket.cs (IPacket, PacketIO, 중계 패킷)
    ZoneCore/              Game.ZoneCore  존 틱 루프, AOI, 전투, 차단 맵, 몬스터/보스
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
| Game.Generated | XlsxConverter가 만든 행/테이블 클래스, 상수/열거형 |
| Game.ZoneCore | 존 틱 루프, AOI, 이동/공격/스킬/상태효과/투사체, 차단 맵(A*), 몬스터 AI, 보스 |
| Game.ZoneCommon | JSON 설정, 명령행 인자, 패킷 전송, `IPacket`/`PacketIO`/`ZonePacket`, 생성된 패킷 구조체(와이어 크기는 C++ static_assert와 동일) |
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

Windows에서는 `ZoneServers_All.bat`이 존 1~10을 각각 새 창으로 엽니다(`ZoneServers_All.bat /G`는 Gateway를 먼저 엽니다). 일부만 열려면 `SET ZONES=1 2 6`을 먼저 지정합니다. 존 서버는 Gateway로 나가는 연결만 맺으므로 한 PC에서 여러 개를 띄워도 포트가 겹치지 않습니다.

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
- **PvP/결투**: 존별 규칙은 `GameData/ZoneRule.json`(World.xlsx의 `ZoneRule` 시트: `ZoneId`, `AllowPvp`, `AllowDuel`)에 둔다. `AllowPvp`가 켜진 존은 누구나 서로 공격할 수 있고(자유 PvP), 꺼진 존은 결투로만 싸운다. `AllowDuel`이 꺼진 존은 결투 신청을 `RejectNotAllowed`로 거부한다. 행이 없는 존은 zoneserver_config.json의 `AllowPvp`(기본 꺼짐)/`AllowDuel`(기본 켜짐)을 쓰고, 형식이 틀리거나 같은 존의 행이 둘이면 그 존 서버는 구동하지 않는다. 규칙은 존 서버를 시작할 때 한 번 읽으므로 바꾸면 그 존 서버를 다시 시작한다. 입장 직후 본인에게 `ZcDuelNotify(ZoneRules)`로 규칙을 알린다(`RemainMs` 비트 1 = 자유 PvP, 2 = 결투 가능). `AllowPvp`와 `AllowDuel`을 둘 다 끄면 플레이어끼리 싸울 수 없는 안전 존이다(몬스터 전투는 그대로). 현재 데이터는 존 1이 안전 존, 존 2~9가 결투만, 존 10이 자유 PvP다. `ZcDuelReq`(2030)로 신청(Request, 상대 `DuelRequestRange` 40 이내)·수락/거절(신청자 ID)·취소(Cancel)를 보내고, 서버는 본인과 상대에게만 `ZcDuelNotify`(2031)를 보낸다. 수락하면 3초 카운트다운 뒤 시작하며, 시작부터 끝까지 서로만 `CanAttack`이 참이다(기본 공격과 스킬 범위 공격 모두 같은 판정을 거치므로 피해·사거리·무적 처리는 몬스터 전투와 같다). 한쪽이 쓰러지거나(승자 통지) 기권, 상대가 존을 떠남, `DuelLeaveRange`(120) 밖으로 멀어짐, 제한 5분이 지나면 끝난다. 신청은 15초 안에 응답이 없으면 만료되고, 한 명은 동시에 결투/신청 하나만 가질 수 있다. 쓰러진 쪽은 기존 부활 정책을 따른다. 패킷은 Packet.xlsx가 아니라 `DuelPackets.cs`에 손으로 썼다. 결투 요청은 종류별 빈도 제한 2/s(버스트 4)를 받는다.
- **요청 빈도 제한**: 종류별(`RequestKind`) 토큰 버킷(이동 20/s 버스트 40, 공격·스킬 8/s 버스트 16, 줍기 10/s 버스트 20, 핑 2/s 버스트 6, 채팅·결투 2/s)으로 넘치는 요청을 버리고 위반 점수를 쌓는다. 점수가 `ViolationLimit`(60)에 이르면 `ZoneKickNotify(RateLimited)`로 존에서 내보낸다(초당 5점 감소). 비정상 값(NaN/Inf, 크기 부족)은 가중치가 크다. `RequestGuardEnabled=false`로 끈다.
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

## 공용 유틸 사용 위치

- 요청 빈도 제한(`ZoneServer/RequestGuard`): 종류별 `Core.Util.TokenBucket`.
- Gateway 클라이언트 패킷 제한: `Core.Util.SlidingWindowCounter`(최근 1초 창).
- 알 수 없는 패킷/속도 초과 로그: `Core.Util.LogThrottle`(같은 로그는 간격당 한 줄).
- 종료 처리(`ServerMain.Run`): `Core.Util.ShutdownCoordinator`가 Ctrl+C·SIGTERM·SIGQUIT를 받아 `host.Stop()`을 한 번만 실행한다(제한 20초, 넘으면 기다리지 않고 종료). 실패/시간 초과는 로그로 남긴다.
- 명령행(`MainArgs`): 읽기 함수가 조회한 키를 기록하고, 옵션을 모두 읽은 뒤 `WarnUnknown()`이 조회되지 않은 옵션(오타)을 경고한다. 실행은 막지 않는다.
- 게이트웨이 재접속(`ZoneServerMain.LinkLoop`): `Core.Util.BackoffState`(500ms→5s 지수, 지터 250ms). 게이트웨이가 재시작될 때 여러 존 서버가 같은 순간에 몰리지 않게 한다. 지터를 포함해 상한 5초를 넘기지 않는다(상한에서는 지터가 없다). 백오프는 연결 완료가 아니라 등록 성공 때 되돌리므로, 연결만 받고 바로 끊는 게이트웨이에는 간격이 5초까지 늘어난다.
- 반복될 수 있는 오류 로그(`Zone` 틱 단계 예외, 존 만원 입장 거절, 존 입력 큐 가득 참으로 인한 상태 복원/퇴장 누락, 몬스터 소환 실패): `LogThrottle`(키별 1초에 한 줄, 억제 횟수 표시).

### 공용 유틸을 쓰지 않는 곳

- `BlockMap` 경로 탐색: 희소 가중치 A*(노드 상한 8192, 칸 수 최대 6400만). `GridPathfinder`는 칸당 17바이트를 미리 잡는 밀집 배열이라 큰 맵에는 맞지 않는다. 작은 맵(수십만 칸 이하)에서만 대안이다.
- 스킬 쿨다운·상태 효과: 존 논리 시간(`ulong` ms)과 `ref readyAtMs`, DoT/HoT 틱 따라잡기 로직이 이미 맞물려 있어 `CooldownSet`/`BuffTimerSet`으로 바꾸면 이득 없이 동작만 바뀐다.
- `TimerHeap<T>`: `ulong` 타이머 ID, 지연 삭제(+압축), 논리 시간 기준 실행이 `Zone` 계약에 들어 있다. `MinHeap`은 BCL `PriorityQueue`보다 느려(약 2배) 바꿀 이유가 없다.
- 난수(`Random`), `Environment.TickCount64`, Gateway 상태 맵, 설정 로더: 바꿔도 동작·성능 이득이 없어 그대로 둔다.

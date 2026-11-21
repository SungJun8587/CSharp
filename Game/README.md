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
| Game.Persistence | 캐릭터 상태 저장소: `IPlayerStore`, 메모리 구현, 쓰기 큐(`WriteBehindPlayerStore`), MySQL 스키마(`Schema/001_players.sql`; level/exp 열이 없는 기존 DB는 `002_player_level.sql` 한 번, player_currency 표가 없는 기존 DB는 `003_player_currency.sql`, player_skill 표가 없는 기존 DB는 `004_player_skill.sql`). 외부 패키지 없음 |
| Game.Persistence.MySql | `MySqlPlayerStore`(Dapper + `Core.Data`). 저장은 배치 전체를 트랜잭션 하나로(왕복 3번: 시작, 문장 묶음, 커밋), 마지막 커밋과 비교해 바뀐 표만 쓰고 아무것도 안 바뀐 플레이어는 DB에 가지 않으며(`FingerprintCache`: 오래 안 쓴 항목은 시간이 지나면, 상한을 넘으면 오래된 것부터 삭제; DB를 프로세스 밖에서 고치면 `InvalidateSavedCache()`, 같은 캐릭터를 동시에 저장하지 않는다는 전제), players는 다중 행 UPSERT, 재화/스킬/가방은 키 목록 DELETE + 다중 행 INSERT를 한 명령으로 묶고(키를 정렬하고 READ COMMITTED로 실행해 교착을 줄임), 로드는 네 표를 한 번에 읽고, 교착/락 대기 초과/연결 끊김은 트랜잭션을 통째로 재시도한다. DB가 죽어 서킷이 열리면 입장(로드)은 즉시 ZoneError로 거절하고, 저장은 실패 횟수에 세지 않고 대기열에 두었다가(캐릭터별로 합쳐져 늘지 않음) 복구되면 내려 쓴다. Gateway가 연결 문자열이 있을 때만 쓴다 |
| Core.Data (`Library/Core/Data`) | `SqlBuilder`(다중 행 INSERT/UPSERT/DELETE 문장, 식별자 검사, `스키마.표` 이름 허용), `SqlBatch`(여러 문장을 파라미터 이름이 겹치지 않게 한 명령으로 묶고 한도에서 나눔), `MySqlDb`(연결 대여 + 일시적 오류 재시도 `RetryPolicy`, 로그/지표에 붙는 DB 이름 `name`, 트랜잭션, `InsertRows/UpsertRows/ReplaceRows/TryInsert`(대량 작업은 호출마다 `commandTimeoutSec` 지정 가능, 키 중복이면 false: 거래 ID 같은 멱등 기록용), 저장 트랜잭션은 격리 수준 지정 가능), `DbErrors`, 서킷 브레이커(접속 실패/끊김이 연속 5번이면 10초 동안 접속 없이 즉시 `CircuitOpenException`, 교착/SQL 오류/풀 고갈(연결 대기 시간 초과)/명령 시간 초과(느린 쿼리, 재시도도 안 함)/취소는 세지 않음; `Circuit`, `db_circuit_rejected_total`), `DbMetrics`(작업별 시간 히스토그램 `db_op_ms`, `db_ops_total`/`db_errors_total`/`db_retries_total`/`db_slow_total`/`db_pool_exhausted_total`, 작업 이름별 `db_op_ms_<작업>`(최대 64종), 머리말 `db`는 DB마다 바꿀 수 있음, 200ms 이상은 작업 이름과 함께 경고 로그), `MigrationRunner`(문장 시간 초과 `statementTimeoutSec` 기본 300초)/`MigrationScripts`/`SqlScript`(스키마 마이그레이션: 폴더 또는 어셈블리 내장 스크립트, 버전 표 이름 지정 가능, 락 이름은 DB+표별) |

통합 시험(실제 MySQL, 시험 전용 DB의 연결 문자열을 환경변수 `CORE_DATA_TEST_DB`에 지정; 이름에 `test`가 없으면 거부): `dotnet run --project ../Library/Core/demo/DataDemo`(SqlBatch/UPSERT/교체/교착 재시도/마이그레이션/지표/풀 고갈; 접속 불가 시 서킷 시험은 DB 없이도 돈다), `dotnet run --project Persistence.MySql.Tests`(저장소 왕복, 바뀐 표만 쓰기, 배치, 동시 배치).
| Game.Tests | 콘솔 러너(단위 + 소켓 E2E) |

## 빌드/실행

```
dotnet build Game.sln
dotnet run --project Game.Tests            # 전체 테스트 (인자로 이름 일부를 주면 해당 스위트만)

dotnet run --project GatewayServer -- --config=Config/gatewayserver_config.json
dotnet run --project ZoneServer   -- --config=Config/zoneserver_config.json --zone-id=1
```

Windows에서는 `ZoneServers_All.bat`이 존 0~10(0은 모든 몬스터가 나오는 시험장, 지형은 `GameData/ZoneRule.json`의 `Terrain`이 가리키는 1~10번 존의 장애물)을 새 창을 따로 열지 않고 콘솔 창 하나에서 모두 돌립니다(`ZoneServers_All.bat /G`는 Gateway를 먼저 열고, 창에서 아무 키나 누르거나 Ctrl+C 를 누르면 모두 종료합니다). 존 1~10은 존마다 몬스터 3종류가 나오고, 존 11(시험장)에는 모든 일반 몬스터가 3마리씩, 모든 보스가 1마리씩 나옵니다. 존 0은 Gateway가 '존 없음'으로 쓰는 값이라 쓸 수 없어 11번을 썼습니다. 일부만 열려면 `SET ZONES=1 2 6`을 먼저 지정합니다. 존 서버는 Gateway로 나가는 연결만 맺으므로 한 PC에서 여러 개를 띄워도 포트가 겹치지 않습니다.

- 설정 파일 키와 명령행 옵션은 C++ 버전과 같습니다. `--dump-config=경로`로 기본값 파일을 만듭니다.
- `GameDataDir`(기본 `GameData`)에 XlsxConverter가 만든 JSON(Hero/Monster/Spawn 필수, Skill/Status/HeroSkill/Boss*/Block/Drop/Level/MonsterExp/Currency/MonsterCurrency 선택)을 두세요. 빈 문자열이면 데이터 없이(기본 영웅 1명, 몬스터 없음) 뜹니다.
- Gateway는 세션당 초당 `ClientMaxPacketsPerSec`(기본 120, 0이면 제한 없음)를 넘는 클라이언트를 끊는다. 존 서버의 종류별 빈도 제한(`RequestGuard`) 합계보다 커야 한다.
- Gateway의 `AuthenticateClient`는 C++과 마찬가지로 임시(항상 통과)입니다. 클라이언트 포트를 외부에 열기 전에 구현하세요.

## 캐릭터 저장 (Gateway)
- 저장은 Gateway가 맡는다. 존 서버는 DB에 접근하지 않고, 플레이어가 존을 떠날 때 보내는 상태(`ZoneStateSave`)를 Gateway가 받아 메모리에 보관하면서 `WriteBehindPlayerStore` 큐에 넣는다. 큐 워커가 `SaveFlushIntervalMs`(기본 1000) 간격으로 최대 `SaveBatchSize`(기본 100)명씩 모아 한 트랜잭션으로 `IPlayerStore`에 쓴다. 같은 캐릭터의 요청은 최신 하나로 합치고, 패킷 처리 스레드는 DB를 기다리지 않는다.
- 입장 때 메모리에 보관한 상태가 없으면 저장소(대기 중인 최신 상태가 있으면 그것)에서 읽어 복원한 뒤 입장시킨다. 저장된 적이 없으면 새 캐릭터다. **읽기에 실패하면 입장을 거부한다**(`ZoneError`): 빈 상태로 들어가면 나중에 그 상태가 저장돼 DB의 좋은 데이터를 덮어쓴다.
- 쓰기 실패는 재시도한다(1초부터 30초까지 두 배씩 쉬며). 묶음이 실패하면 한 명씩 나눠 저장해 문제 있는 한 건이 다른 캐릭터를 막지 않게 하고, 한 건이 5번 실패하면 버리고 로그를 남긴다. Gateway를 끌 때는 남은 것을 내려 쓸 때까지 기다린다(최대 10초).
- 설정(`gatewayserver_config.json`): `GameDB`(MySQL 연결 문자열, 비우면 DB 없이 동작), `SchemaDir`(스키마 폴더 또는 `embedded`(실행 파일에 내장된 스크립트), 비우면 스키마를 건드리지 않음), `SaveFlushIntervalMs`, `SaveBatchSize`. 예) `Server=127.0.0.1;Port=3306;Database=game;User ID=game;Password=...;MaximumPoolSize=16;ConnectionTimeout=5;DefaultCommandTimeout=10`(기본 연결 15초/명령 30초는 재시도와 곱해져 길어지므로 줄여 쓴다). 입장 시 캐릭터 읽기는 10초 안에 끝나지 않으면 입장을 거절한다. `SchemaDir`에 `Persistence/Schema` 폴더(또는 `embedded`)를 주면 시작할 때 아직 적용하지 않은 `NNN_이름.sql`만 버전 순으로 적용한다(적용 기록은 `schema_version` 표, 이미 적용한 파일을 고치면 체크섬 불일치로 시작하지 않는다, 여러 서버가 동시에 떠도 락으로 한 곳만 적용). 스크립트는 다시 실행해도 안전해야 한다(DDL은 롤백되지 않는다). 비워 두면 스키마 파일을 직접 실행한다. 커넥션 풀은 MySqlConnector가 연결 문자열 옵션으로 관리한다.
- MySQL 지원 없이 빌드하려면 `-p:SkipMySql=true`(NuGet의 Dapper/MySqlConnector가 필요 없다). 그 빌드에서 연결 문자열을 설정하면 Gateway가 시작하지 않는다. 테스트는 `GatewayOptions.PlayerStore`에 `InMemoryPlayerStore`를 넘겨 DB 없이 돈다.
- 병목이 되면 Gateway 코드는 그대로 두고 `IPlayerStore` 구현만 "별도 DB 서비스로 보내는 클라이언트"로 바꾼다.

## 전투 규칙

- **MP/재생**: 영웅의 `MaxMp`(0이면 MP 없음)와 스킬의 `MpCost`. MP는 시전이 성공하는 시점에 깎이며 시전이 취소돼도 돌려주지 않는다. 부족하면 `NotEnoughMp`로 거부한다. `HpRegen`/`MpRegen`은 초당 회복량이고 `RegenIntervalMs`(1000) 주기로 적용한다. 체력 재생은 피해를 주고받은 뒤 `RegenCombatDelayMs`(5000) 동안 멈추고 MP 재생은 멈추지 않는다. 내 체력/MP는 `ZcStatNotify`로 본인에게만 간다.
- **위협(어그로)**: 피해량만큼 쌓이며 스킬의 `ThreatBonusPercent`로 보정한다. 치유는 치유량의 `HealThreatPercent`(50)%를 치유 대상을 어그로 표에 가진 `HealThreatRange`(20) 안 몬스터에게 치유자의 위협으로 더한다. `TauntMs`가 있는 스킬은 시전자를 표 맨 위로 올리고 그 시간 동안 대상으로 고정한다. 보스와 복귀 중인 몬스터는 도발되지 않는다.
- **밀치기/끌어당기기/속박**: `KnockbackDistance` 양수는 밀어내고 음수는 끌어당긴다(기준점은 Area면 중심, 아니면 시전자). 장애물에서 멈추고 끌어당기기는 `PullStopDistance`(1.0)에서 멈추며 보스는 면역이다. `Root` 상태는 이동만 막는다. 쫓던 몬스터는 포기하거나 집으로 순간이동하지 않고 풀릴 때까지 기다린다.
- **경험치/레벨**: `Level` 표(Hero.xlsx: 레벨마다 `ExpToNext`와 능력치 배율 `StatPercent`, 레벨 1은 100·마지막 레벨은 `ExpToNext` 0)와 `MonsterExp` 표(Monster.xlsx: 몬스터별 처치 경험치)로 정한다. 처치자(플레이어)가 경험치를 얻고(소환 몬스터는 주지 않는다), 필요량을 채우면 레벨이 오른다(한 번에 여러 레벨 가능). 레벨 업마다 영웅 능력치 중 MaxHp/MaxMp/Attack/Defense를 `StatPercent`만큼 곱해 다시 계산하고 체력/MP를 가득 채우며, 주변에는 `ZcHealNotify`로 새 최대 체력을 알린다. 최대 레벨에서는 경험치를 쌓지 않는다. 본인에게는 손으로 쓴 패킷 `ZcExpNotify`(2032: level, flags, exp, expToNext, gained)를 입장 때와 경험치를 얻을 때마다 보낸다. 레벨 표가 없으면 레벨/경험치 기능이 꺼진다(레벨 1 고정, 패킷 없음). 레벨과 경험치는 체력/MP/가방과 함께 `PlayerState`로 저장돼 존 이동·재접속에서 이어지며, 저장된 레벨이 표의 최대 레벨을 넘으면 최대 레벨로, 경험치가 필요량 이상이면 필요량 직전으로 보정한다.
- **재화/골드**: `Currency` 표(Hero.xlsx: 재화 ID, 이름, 보유 상한 `MaxAmount`, 선택 열 `Paid`)와 `MonsterCurrency` 표(Monster.xlsx: 몬스터별 재화, `MinAmount`~`MaxAmount`, `ChancePermille`)로 정한다. 처치자(플레이어)가 줄마다 확률을 굴려 범위 안의 수량을 받고(한 몬스터가 여러 재화를 줄 수 있다, 소환 몬스터는 주지 않는다), 보유 상한을 넘는 만큼은 버린다. 보유량은 플레이어의 지갑(재화 ID -> 양, 최대 `PlayerState.MaxCurrencies`=4종)에 쌓이고 `PlayerState`로 저장돼 존 이동·재접속에서 이어진다. 복원할 때 표에서 사라진 재화는 버리고 상한이 줄었으면 상한으로 보정한다. 본인에게는 손으로 쓴 패킷 `ZcCurrencyNotify`(2033: currencyId, amount, gained)를 입장 때 정의된 재화마다 하나씩(gained 0), 얻을 때마다 보낸다. 표가 없으면 재화 기능이 꺼진다. 샘플 데이터는 골드(ID 1)이고 일반 몬스터는 800‰, 보스는 1000‰로 경험치의 50~100%를 준다.
- **드롭/줍기**: `Drop` 표(몬스터별 확률 1000분율, 수량 범위)로 몬스터가 죽을 때 지면 아이템 엔티티(`ItemIds.Base` 대역, 스폰 통지의 entityType=3, templateId=아이템 ID, hp=maxHp=수량)를 만든다. 처치자(플레이어)가 `DropOwnerMs` 동안 먼저 줍고 `DropLifetimeMs`가 지나면 사라진다. 존당 `MaxGroundItems`가 상한이고 소환 몬스터는 드롭하지 않는다. 가방(`Inventory`)은 메모리에만 있다(`MaxInventorySlots` 종류, 종류당 `MaxItemStack`).
- **채팅**: `ZcChatReq`의 채널은 Say(내 시야 안 + 본인), Zone(존 전체, 플레이어마다 `ChatZoneCooldownMs` 쿨다운), Whisper(같은 존의 `targetId` + 본인 에코). 본문은 고정 128바이트 UTF-8 필드(한글 약 42자)이며 앞뒤 공백을 자르고, 비었거나 제어/서식 문자(줄바꿈, 제로 폭 공백, 방향 전환), 대체 문자(잘못된 UTF-8)가 있으면 `InvalidText`로 거부한다. 서버는 검증한 문자열을 다시 인코딩해 보내므로 클라이언트 바이트를 그대로 퍼뜨리지 않는다. 귓속말 대상이 없거나 자기 자신이거나 몬스터 ID여도 같은 `NoTarget`이라 존재 여부가 새지 않는다. 형식 위반(없는 채널, 잘못된 본문)은 위반 점수도 올리고, 채팅은 종류별 빈도 제한(2/s, 버스트 5)을 받는다. 보낸 사람 이름은 싣지 않는다(`senderId`만). 존을 넘는 채팅(월드/길드/파티)과 차단·금칙어는 아직 없다.
- **시험장 지형 치트**: 뷰어에서 시험장(존 0)에 있을 때 `F6`(Shift+F6은 이전)을 누르면 자동 전투가 켜지고 지형 1~10이 차례로 바뀝니다. 서버에 붙었으면 `ZcTerrainReq`(2034)가 존 0 서버로 가서 장애물 맵(`Block.json`의 그 존 번호 행)을 바꾸고(충돌/경로 모두), 존 전원에게 `ZcTerrainNotify`(2035)를 보내 화면이 같은 지형으로 바뀝니다. 새 장애물 안에 있던 플레이어/몬스터는 가장 가까운 빈자리로 옮겨지고, 바뀐 뒤에 입장한 플레이어도 입장 때 현재 지형을 받습니다. 존 0 서버만 켜지며(`ZoneServerMain`이 `TerrainLoader`를 연결), 다른 존은 요청을 무시합니다. 바꾼 지형은 존 서버를 다시 시작하면 `ZoneRule.Terrain` 값으로 돌아갑니다. 두 패킷은 결투/경험치 패킷처럼 `Packet.xlsx`가 아닌 손으로 쓴 파일(`TerrainPackets.cs`, 뷰어의 `TerrainPacket.cs`)입니다.
- **PvP/결투**: 존별 규칙은 `GameData/ZoneRule.json`(World.xlsx의 `ZoneRule` 시트: `ZoneId`, `AllowPvp`, `AllowDuel`, `Terrain`, `AllMonsterCount`, `AllBossCount`; `Terrain`이 0이 아니면 그 존 번호의 장애물(Block)을 쓰고, `AllMonsterCount`/`AllBossCount`가 0이 아니면 Monster 표의 일반 몬스터/Boss 표의 보스를 모두 그 마릿수씩 칸을 나눠 세운다. 시험장(존 0)은 3과 1이라 Spawn 표에 행이 없다. 칸 배치는 `ZoneServer/AllSpawnLayout.cs`와 뷰어 `GameData`가 같은 규칙으로 계산한다)에 둔다. `AllowPvp`가 켜진 존은 누구나 서로 공격할 수 있고(자유 PvP), 꺼진 존은 결투로만 싸운다. `AllowDuel`이 꺼진 존은 결투 신청을 `RejectNotAllowed`로 거부한다. 행이 없는 존은 zoneserver_config.json의 `AllowPvp`(기본 꺼짐)/`AllowDuel`(기본 켜짐)을 쓰고, 형식이 틀리거나 같은 존의 행이 둘이면 그 존 서버는 구동하지 않는다. 규칙은 존 서버를 시작할 때 한 번 읽으므로 바꾸면 그 존 서버를 다시 시작한다. 입장 직후 본인에게 `ZcDuelNotify(ZoneRules)`로 규칙을 알린다(`RemainMs` 비트 1 = 자유 PvP, 2 = 결투 가능). `AllowPvp`와 `AllowDuel`을 둘 다 끄면 플레이어끼리 싸울 수 없는 안전 존이다(몬스터 전투는 그대로). 현재 데이터는 존 1이 안전 존, 존 2~9가 결투만, 존 10이 자유 PvP다. `ZcDuelReq`(2030)로 신청(Request, 상대 `DuelRequestRange` 40 이내)·수락/거절(신청자 ID)·취소(Cancel)를 보내고, 서버는 본인과 상대에게만 `ZcDuelNotify`(2031)를 보낸다. 수락하면 3초 카운트다운 뒤 시작하며, 시작부터 끝까지 서로만 `CanAttack`이 참이다(기본 공격과 스킬 범위 공격 모두 같은 판정을 거치므로 피해·사거리·무적 처리는 몬스터 전투와 같다). 한쪽이 쓰러지거나(승자 통지) 기권, 상대가 존을 떠남, `DuelLeaveRange`(120) 밖으로 멀어짐, 제한 5분이 지나면 끝난다. 신청은 15초 안에 응답이 없으면 만료되고, 한 명은 동시에 결투/신청 하나만 가질 수 있다. 쓰러진 쪽은 기존 부활 정책을 따른다. 패킷은 Packet.xlsx가 아니라 `DuelPackets.cs`에 손으로 썼다. 결투 요청은 종류별 빈도 제한 2/s(버스트 4)를 받는다.
- **몬스터 기본 공격**: 몬스터는 기본 공격을 바로 넣지 않고 예비 동작(시전)을 거친다. 일반 몬스터는 공격 간격의 30%(250~450ms) 준비한 뒤 한 명을, 보스는 700ms 준비한 뒤 앞쪽 부채꼴(110도) 안의 최대 6명을 때리고 1.5 밀어낸다. 준비 중에 대상이 사거리 밖으로 피하면 빗나간다. 클라이언트에는 `ZcSkillCastNotify`/`ZcSkillNotify`로 전해지며 스킬 ID는 90000 + 몬스터 ID(데이터 표에 없는 가상 스킬)다. 선공 시야(`AggroRange`)는 몬스터 표에서 정한다. `MonsterSystemConfig.BasicAttackWindup` 을 끄면 예전처럼 즉시 공격한다.
- **스킬 개방/구매/장착**: 영웅이 배울 수 있는 스킬(영웅당 최대 8)과 조건은 `HeroSkill` 표(Hero.xlsx)가 정한다. 칸은 8개다. **칸 1~6은 고정 칸**: `Price` 0, `Slot` 1~6인 스킬이 `UnlockLevel`에 이르면 자동으로 지급되어 그 칸에 들어가며 옮기거나 뺄 수 없다. **칸 7·8은 상점 칸**: `Price`>0(`PriceCurrencyId` 필수), `Slot` 0인 스킬을 `UnlockLevel` 이상에서 사서 레벨로 열린 칸(`SkillRules.ShopSlotUnlockLevels`, 현재 25/35)에 끼운다. 구매하면 열려 있는 빈 상점 칸에 바로 장착되고, 해제하거나 두 칸을 맞바꿀 수 있다. 같은 고정 칸을 둘이 쓰거나 상점 스킬에 고정 칸이 있으면 로드에 실패한다. 보유 스킬과 장착 칸은 `ZonePlayer.Skills`(스킬 ID → 칸, 0은 장착 안 함)에 있고 `PlayerState.Skills`/`PlayerStateWire`의 스킬 블록/`player_skill` 표로 저장·복원된다(복원 때 무료 스킬은 표대로 다시 지급하고, 상점 스킬의 칸은 열려 있고 겹치지 않을 때만 유지). 장착 중인 스킬만 시전할 수 있다(장착하지 않은 스킬의 `ZcSkillReq`는 `UnknownSkill`로 거부). 손으로 쓴 패킷 `ZcSkillBookNotify`(2036, 보유 현황·칸·상점 칸 7/8이 열리는 레벨), `ZcSkillBuyReq`(2037), `ZcSkillEquipReq`(2038, 칸 0은 해제), `ZcSkillOpResNotify`(2039, 결과 사유: 고정 칸/칸 잠김 포함). 구매는 사망/보유/무료 스킬/레벨/재화 순으로 검사하고, 성공하면 재화를 깎고 현황·재화 통지를 보낸 뒤 바로 Gateway로 상태를 저장한다. 존 서버는 `Currency` 표에 있는(몬스터 보상으로 얻는) 재화만 쓴다. 결제로 얻는 유료 재화는 존이 바꾸지 않고 계정 단위 지갑과 거래 장부를 가진 Gateway/DB 트랜잭션으로만 다룰 예정이다.
- **요청 빈도 제한**: 종류별(`RequestKind`) 토큰 버킷(이동 20/s 버스트 40, 공격·스킬 8/s 버스트 16, 줍기 10/s 버스트 20, 핑 2/s 버스트 6, 스킬 구매·장착 4/s 버스트 8, 채팅·결투 2/s)으로 넘치는 요청을 버리고 위반 점수를 쌓는다. 점수가 `ViolationLimit`(60)에 이르면 `ZoneKickNotify(RateLimited)`로 존에서 내보낸다(초당 5점 감소). 비정상 값(NaN/Inf, 크기 부족)은 가중치가 크다. `RequestGuardEnabled=false`로 끈다.
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
- `Level`은 1부터 빠짐없이 이어져야 하고, 배율은 100 이상 100000 이하이며 줄어들지 않는다. `MonsterExp`의 몬스터 ID는 Monster 표에 있어야 한다.
- 재화 2 "블루 다이아"는 `Paid`(유료) 재화다: 결제로만 얻으므로 몬스터 보상이 될 수 없고(검증이 막는다) 존 서버가 차감하지도 않는다(스킬 구매 가격으로 못 쓴다). 보유량은 지갑에 0으로 시작하고, 쌓고 쓰는 길은 이후 Gateway/DB 거래(계정 지갑)로만 연다.
- `MonsterCurrency`의 몬스터 ID는 Monster 표에, 재화 ID는 Currency 표에 있어야 하고 `1 <= MinAmount <= MaxAmount <= 그 재화의 MaxAmount`, 확률은 1~1000이며 같은 몬스터가 같은 재화를 두 줄 갖지 않는다.
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

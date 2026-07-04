# JwtAuthService 프로젝트 분석

> 대상: [SungJun8587/CSharp](https://github.com/SungJun8587/CSharp) 저장소의 `JwtAuthService` 폴더
> 분석 기준: `main` 브랜치

---

## 1. 개요

ASP.NET Core(.NET 8) 기반의 **JWT 인증 서비스** 샘플/레퍼런스 프로젝트입니다. 동일한 인증 로직(`JwtAuthCommon`)을 공유하면서, 서로 다른 두 가지 API 통신 방식(**JSON**, **Protobuf**)으로 노출하는 구조를 실험적으로 구현한 것이 특징입니다. Access/Refresh 토큰 발급·검증·로테이션, Redis 기반 블랙리스트, MySQL 영속화, 통합 테스트까지 인증 서비스의 핵심 요소를 폭넓게 다룹니다.

## 2. 솔루션 구조

`JwtAuthService.sln` 기준 5개 프로젝트로 구성됩니다.

| 프로젝트 | 유형 | 역할 |
|---|---|---|
| `JwtAuthCommon` | 클래스 라이브러리 | 인증 도메인 로직 공통 모듈 (엔티티, 리포지토리, 서비스, DbContext) |
| `JwtAuthService.Json` | ASP.NET Core Web API | JSON 기반 REST API 서버 |
| `JwtAuthService.Json.Tests` | xUnit 테스트 | Json 서버 통합 테스트 |
| `JwtAuthService.Protobuf` | ASP.NET Core Web API | Protobuf(바이너리) 기반 REST API 서버 |
| `JwtAuthService.Protobuf.Tests` | xUnit 테스트 | Protobuf 서버 통합 테스트 |

```
JwtAuthService/
├── JwtAuthCommon/                 # 공통 도메인 로직 (두 API 서버가 공유)
│   ├── Data/AppDbContext.cs
│   ├── Entities/                  # UserEntity, RefreshTokenEntity, BlacklistedAccessTokenEntity
│   ├── Repositories/              # IUserRepository, IRefreshTokenRepository + 구현체
│   ├── Services/                  # IAuthService, IJwtService, ITokenBlacklistService + 구현체
│   └── DB/Create_RefreshTokens_Table.sql
├── JwtAuthService.Json/           # JSON REST API
│   ├── Controllers/               # Auth, Admin, User, Protected
│   ├── Middleware/JwtValidationMiddleware.cs
│   └── Program.cs
├── JwtAuthService.Protobuf/       # Protobuf REST API
│   ├── Controllers/
│   ├── ProtobufFormatter/         # Input/Output Formatter
│   ├── Protos/ (user.proto, services.proto)
│   └── Program.cs
├── JwtAuthService.Json.Tests/
├── JwtAuthService.Protobuf.Tests/
└── JwtAuthPostman/                # Postman 컬렉션/환경
```

## 3. 핵심 아키텍처: JSON vs Protobuf 이원화

가장 눈에 띄는 설계 포인트입니다. `JwtAuthCommon`에 인증 비즈니스 로직을 전부 두고, 두 API 프로젝트는 **얇은 컨트롤러 계층**만 다르게 구현합니다.

- **JwtAuthService.Json**: 표준 `[ApiController]` + System.Text.Json 직렬화. Swagger 지원.
- **JwtAuthService.Protobuf**: gRPC가 아니라 **REST 위에 Protobuf 바이너리 페이로드**를 얹은 방식입니다.
  - `.proto`로 메시지(DTO)만 정의하고 `GrpcServices="None"`으로 서비스 스텁 생성은 꺼둠 (`services.proto`에 서비스 정의는 있지만 실제 gRPC 서버로는 안 씀 — 참고/설계용으로 보임).
  - `ProtobufInputFormatter` / `ProtobufOutputFormatter`를 직접 구현해 `Content-Type: application/x-protobuf` 요청·응답을 바이트 단위로 직렬화/역직렬화.
  - 컨트롤러에 `[Consumes("application/x-protobuf")]`, `[Produces("application/x-protobuf")]` 명시.

같은 인증 흐름을 JSON과 바이너리 프로토콜 두 방식으로 각각 구현하며 성능·설계 차이를 비교해보는 학습/실험용 구조로 보입니다.

## 4. 인증 흐름 (JwtAuthCommon)

### 4.1 데이터 모델

- **UserEntity**: `Id`(관리자 1~100, 일반유저 101~), `Username`, `Password_Hash`(BCrypt), `Email`, `Role`, `IsActive`, `CreatedAt`, `LastLoginAt`, `IsActiveChangedAt`
- **RefreshTokenEntity**: `Token`(Base64, 64바이트 랜덤), `DeviceId`(기기 바인딩), `ExpiresAt`, `RevokedAt`, `ReplacedByToken`(로테이션 추적), `IsActive`(계산 속성)
- **BlacklistedAccessTokenEntity**: `Jti`, `ExpiresAt` — 실시간 블랙리스트 조회는 **Redis**로 처리하되, MySQL(`BlacklistedAccessTokens` 테이블)에도 동일 정보를 영속 기록해 감사 로그 및 Redis 장애 시 복구 소스로 함께 사용하는 이중 저장 구조

### 4.2 JwtService — 토큰 발급/검증/로테이션 핵심

- `GenerateAccessToken`: HS256 서명, Claims에 `Sub`(userId), `Name`, `Role`, `Email`, `Jti`(GUID) 포함. 만료시간은 설정값(`Jwt:AccessTokenExpirationMinutes`) 사용.
- `GenerateTokensAsync`: 동일 `deviceId`의 기존 활성 리프레시 토큰을 모두 무효화한 뒤 새 Access/Refresh 토큰 발급 (기기당 세션 1개 유지 전략).
- `RotateRefreshTokenAsync`: Refresh 토큰 재사용 시 유효성(폐기 여부/만료/기기 일치) 검증 후 기존 토큰 폐기 + 신규 토큰 발급하는 **Refresh Token Rotation** 패턴. 기기 불일치 시 즉시 토큰 무효화 처리(탈취 대응).
- `InvalidateAccessTokenAsync` / `IsAccessTokenBlacklistedAsync`: 로그아웃 시 Access 토큰의 `jti`를 Redis 블랙리스트에 등록, 남은 만료시간만큼 TTL 설정.

### 4.3 AuthService — 유스케이스 오케스트레이션

`LoginAsync`(자격증명 검증 + 마지막 로그인 시각 갱신 + 토큰 발급) / `RefreshAsync`(로테이션 위임) / `LogoutAsync`(Refresh 토큰 무효화) 세 가지로 컨트롤러에 얇은 인터페이스 제공.

### 4.4 TokenBlacklistService — Redis + DB 이중 저장 블랙리스트

Access 토큰 블랙리스트는 **실시간 검증용 Redis**와 **영속 기록/복구용 MySQL**을 함께 사용하는 이중 저장 구조로 구현되어 있습니다. DB 기록은 요청 경로에서 직접 처리하지 않고, 인메모리 큐(`BlacklistWriteQueue`)를 거쳐 백그라운드에서 배치로 처리하도록 설계되어 있습니다.

- **실시간 검증(`IsBlacklistedAsync`)**: `blacklist:{jti}` 키를 `KeyExistsAsync`로 조회 — **Redis만** 사용합니다. `OnTokenValidated` 이벤트 등 매 요청마다 호출되는 경로이므로 DB 조회를 끼워 넣지 않아 지연시간을 최소화합니다.
- **블랙리스트 등록(`AddToBlacklistAsync`)**: `blacklist:{jti}` 키에 값 `"1"`을 남은 만료시간(TTL)과 함께 저장한 뒤, `BlacklistWriteQueue.Enqueue`로 (Jti, ExpiresAt)을 인메모리 큐에 적재하고 즉시 반환합니다. 이 경로에서는 스코프 생성이나 DB 접근이 전혀 일어나지 않아, 트래픽이 많아져도 요청당 처리 비용이 일정하게 유지됩니다.
- **큐 배치 소비(`BlacklistDbWriterHostedService`)**: 유일한 컨슈머로서 큐에 쌓인 항목을 기본 500ms 또는 최대 200건 단위로 모아, 스코프를 한 번만 생성해 `IBlacklistedAccessTokenRepository.AddRangeAsync`로 일괄 INSERT합니다. 이미 존재하는 Jti는 배치 내부에서 걸러내 유니크 제약 위반을 방지합니다. 개별 저장 실패는 로그만 남기고 다음 배치로 넘어갑니다(best-effort).
- **서버 기동 시 웜업(`BlacklistWarmupHostedService`)**: `IHostedService.StartAsync`에서 앱이 요청을 받기 시작하기 전에 1회 실행되어, DB에 남아있는 아직 만료되지 않은 블랙리스트 항목을 Redis에 남은 TTL만큼 재등록합니다. Redis가 재시작되거나 장애로 데이터가 유실된 경우에도, 이미 폐기됐어야 할 토큰이 다시 유효해지는 것을 방지합니다.
- **만료 레코드 정리(`BlacklistCleanupHostedService`)**: `BackgroundService`로 등록되어 기본 6시간 주기로 DB의 만료된(`ExpiresAt <= now`) 레코드를 `ExecuteDeleteAsync`로 일괄 삭제합니다. Redis는 TTL로 자동 만료되지만 DB는 별도 정리가 필요하기 때문입니다.

정리하면, Redis는 "지금 이 토큰이 막혀 있는가"를 빠르게 답하는 캐시 역할을, DB는 "언제 어떤 토큰이 왜 폐기됐는지" 남기는 감사 로그이자 Redis 장애 시 복구 소스 역할을 각각 담당합니다. 요청 경로(웹 API 스레드)와 DB 기록 경로(백그라운드 스레드)가 큐로 분리되어 있어, 로그아웃 트래픽이 몰려도 DB 접근 빈도가 트래픽에 비례해 늘어나지 않고 일정한 배치 주기로 수렴합니다.

## 5. Web API 계층

### 5.1 엔드포인트 목록 (Json / Protobuf 공통 라우트)

| Controller | Method & Route | 인증 | 설명 |
|---|---|---|---|
| AuthController | `POST /api/auth/register` | 익명 | 회원가입 |
| AuthController | `POST /api/auth/login` | 익명 | 로그인 → Access/Refresh 토큰 발급 |
| AuthController | `POST /api/auth/refresh` | 익명 | Refresh 토큰 로테이션 |
| AuthController | `POST /api/auth/logout` | 익명 | Refresh 토큰 무효화 |
| AdminController | `GET /api/admin/all` | Admin | 전체 유저 조회 |
| AdminController | `GET /api/admin/admins` | Admin | 활성 관리자 목록 |
| AdminController | `GET /api/admin/users` | Admin | 활성 일반유저 목록 |
| AdminController | `POST /api/admin/register` | 익명 | 관리자 계정 생성 |
| AdminController | `POST /api/admin/login` | 익명 | 관리자 로그인 |
| AdminController | `DELETE /api/admin/user/{id}` | Admin | 유저 비활성화(soft delete) |
| AdminController | `PATCH /api/admin/user/{id}` | Admin | 유저 재활성화 |
| UserController | `GET /api/user/userinfo` | JWT | 토큰 클레임 기반 사용자 정보 |
| UserController | `GET /api/user/tokeninfo` | JWT | Access/Refresh 만료 정보 조회 |
| ProtectedController | `GET /api/protected/test` | JWT | 인증 동작 확인용 |

(Protobuf 프로젝트는 위와 동일한 라우트를 `application/x-protobuf`로 제공하며, Admin 컨트롤러는 확인 결과 Json 프로젝트에만 존재합니다.)

### 5.2 인증/인가 파이프라인 (JwtAuthService.Json/Program.cs)

1. `AddAuthentication` + `AddJwtBearer`: Issuer/Audience/서명키/수명 전부 검증.
2. `JwtBearerEvents.OnMessageReceived`: `[Authorize]`가 없는 엔드포인트는 검사 생략, Authorization 헤더 누락/포맷 오류 시 즉시 401 JSON 응답.
3. `JwtBearerEvents.OnTokenValidated`: 서명·만료 검증을 통과한 토큰이라도 `jti`가 Redis 블랙리스트에 있으면 401 처리 — **표준 JWT 미들웨어 검증 + 커스텀 블랙리스트 검증**을 이벤트 훅으로 결합한 부분이 인상적입니다.
4. `AddAuthorization`의 `"AdminOnly"` 정책(`RequireRole("Admin")`)으로 관리자 전용 API 보호.
5. 별도의 `JwtValidationMiddleware`도 파이프라인에 등록되어 있는데, `AddJwtBearer` 인증과 기능이 상당 부분 중복됩니다(둘 다 토큰 파싱·서명검증·블랙리스트 체크 수행). 학습/실험 과정에서 두 가지 구현 방식을 함께 남겨둔 것으로 보이며, 실제 운영에서는 하나로 정리하는 편이 좋습니다.

### 5.3 앱 시작 시 초기화

- `EnsureCreated()`로 DB/테이블 자동 생성 (마이그레이션이 아닌 스키마 동기화 방식 — 프로토타입 단계에 적합, 운영에서는 EF Migration 권장).
- 개발 환경에서 Swagger UI 활성화.
- Redis는 `ConnectionMultiplexer.Connect`로 싱글톤 등록.
- `BlacklistWarmupHostedService`가 앱이 요청을 받기 시작하기 전에 실행되어 DB에 남아있는 유효한 블랙리스트 항목을 Redis로 복원하고, `BlacklistDbWriterHostedService`가 `BlacklistWriteQueue`에 쌓인 항목을 배치로 모아 DB에 기록하며, `BlacklistCleanupHostedService`가 이후 백그라운드에서 주기적으로(기본 6시간) DB의 만료 레코드를 정리합니다. 세 서비스 모두 `builder.Services.AddHostedService<>()`로 등록됩니다.

## 6. 데이터베이스 설계

`Create_RefreshTokens_Table.sql` 기준 MySQL(`utf8mb4`) 스키마:

- **Users**: `AUTO_INCREMENT = 101`로 시작 지점을 조정해 관리자(1~100)/일반유저(101~) ID 대역을 분리. `UserRepository.AddAdminAsync`에서는 `FOR UPDATE` 잠금으로 admin ID 채번 시 동시성 문제를 방지.
- **RefreshTokens**: `UserId`, `UserId+DeviceId` 복합 인덱스로 기기별 조회 최적화.
- **BlacklistedAccessTokens**: `Jti` 유니크 인덱스 — Redis 블랙리스트와 함께 사용되는 영속 저장소로, 감사 로그 및 Redis 장애 복구용 데이터를 보관.
- 시드 데이터로 관리자 1명(`admin01`) + 일반유저 10명(`user01~10`)이 동일 비밀번호(`Pass123!`)로 삽입되어 있어 즉시 테스트 가능.

### 6.1 테이블 상세 (`Create_RefreshTokens_Table.sql` 기준)

세 테이블 모두 `ENGINE=InnoDB`, `CHARSET=utf8mb4`로 생성되며, 스크립트 최상단에서 `DROP TABLE IF EXISTS`로 기존 테이블을 먼저 제거한 뒤 재생성하는 초기화용 스크립트입니다.

#### Users — 사용자 테이블

| 컬럼 | 타입 | 제약/기본값 | 설명 |
|---|---|---|---|
| `Id` | `BIGINT` | PK, `AUTO_INCREMENT`(101부터 시작) | 사용자 고유 ID. 1~100은 관리자용으로 예약, 101부터 일반 사용자에게 채번 |
| `UserName` | `VARCHAR(100)` | `NOT NULL`, UNIQUE(`uq_Users_UserName`) | 로그인 아이디, 중복 불가 |
| `Password_Hash` | `VARCHAR(255)` | `NOT NULL` | BCrypt로 해시된 비밀번호 |
| `Email` | `VARCHAR(255)` | `NULL` 허용 | 이메일(선택 입력) |
| `Role` | `VARCHAR(50)` | `NOT NULL` | 사용자 역할(`Admin` / `User`) |
| `IsActive` | `BIT(1)` | `NOT NULL` | 계정 활성화 여부. `false`면 로그인 차단(정지 계정 처리) |
| `CreatedAt` | `DATETIME` | 기본값 `CURRENT_TIMESTAMP` | 계정 생성 시각 |
| `LastLoginAt` | `DATETIME` | `NULL` 허용, 기본값 없음 | 마지막 로그인 시각. 로그인 성공 시마다 갱신 |
| `IsActiveChangedAt` | `DATETIME` | `NULL` 허용 | 활성화 상태가 마지막으로 변경된 시각(정지/재활성 이력용) |

- **인덱스**: `UNIQUE KEY uq_Users_UserName (UserName)` — 아이디 중복 가입 방지 및 로그인 시 조회 성능 확보.
- **AUTO_INCREMENT 오프셋**: `ALTER TABLE Users AUTO_INCREMENT = 101`로 관리자(1~100)와 일반 사용자(101~)의 ID 대역을 물리적으로 분리. `UserRepository.AddAdminAsync`가 `SELECT ... FOR UPDATE`로 100 미만 구간을 조회해 관리자 ID를 수동 채번하는 로직과 맞물려 동작.
- **시드 데이터**: 관리자 1건(`Id=1, admin01`), 일반 사용자 10건(`Id=101~110, user01~10`) — 모두 동일한 BCrypt 해시(`Pass123!`)로 삽입.

#### RefreshTokens — 리프레시 토큰 테이블

| 컬럼 | 타입 | 제약/기본값 | 설명 |
|---|---|---|---|
| `Id` | `BIGINT` | PK, `AUTO_INCREMENT` | 토큰 레코드 고유 ID |
| `UserId` | `BIGINT` | `NOT NULL` | `Users.Id` 참조(외래키 제약은 미설정, 애플리케이션 레벨에서만 연관) |
| `Token` | `VARCHAR(512)` | `NOT NULL` | Base64로 인코딩된 리프레시 토큰 값(64바이트 랜덤) |
| `DeviceId` | `VARCHAR(256)` | `NULL` 허용 | 클라이언트가 전달한 기기 식별자. 동일 기기의 중복 세션 제어에 사용 |
| `ExpiresAt` | `DATETIME` | `NOT NULL` | 토큰 만료 시각(UTC) |
| `CreatedAt` | `DATETIME` | `NOT NULL`, 기본값 `CURRENT_TIMESTAMP` | 토큰 발급 시각 |
| `RevokedAt` | `DATETIME` | `NULL` 허용 | 폐기 시각. `NULL`이면 아직 활성 상태 |
| `ReplacedByToken` | `VARCHAR(512)` | `NULL` 허용 | 로테이션으로 이 토큰을 대체한 새 토큰 값(폐기 이력 추적용) |

- **인덱스**: `IX_RefreshTokens_UserId (UserId)` — 사용자별 유효 토큰 조회, `IX_RefreshTokens_UserId_DeviceId (UserId, DeviceId)` — 특정 기기의 활성 토큰 조회(로그인 시 기존 토큰 무효화, 로테이션 시 기기 일치 검증에 사용).
- **활성 여부 판정**: DB 컬럼으로 별도 플래그를 두지 않고, `RevokedAt IS NULL AND ExpiresAt > NOW()` 조건으로 계산(엔티티의 `IsActive` 계산 속성과 대응).
- **콜레이션**: `utf8mb4_unicode_ci` 명시(다른 두 테이블과 달리 명시적으로 지정됨).

#### BlacklistedAccessTokens — 블랙리스트 액세스 토큰 테이블

| 컬럼 | 타입 | 제약/기본값 | 설명 |
|---|---|---|---|
| `Id` | `BIGINT` | PK, `AUTO_INCREMENT` | 레코드 고유 ID |
| `Jti` | `VARCHAR(255)` | `NOT NULL`, UNIQUE(`UQ_BlacklistedAccessTokens_Jti`) | 폐기 대상 Access 토큰의 JWT 고유 식별자(JTI) |
| `ExpiresAt` | `DATETIME` | `NOT NULL` | 원본 Access 토큰의 만료 시각(UTC) |
| `CreatedAt` | `DATETIME` | `NOT NULL`, 기본값 `CURRENT_TIMESTAMP` | 블랙리스트에 등록된 시각 |

- **인덱스**: `Jti`에 유니크 인덱스를 걸어 동일 토큰의 중복 등록을 방지.
- **저장/조회 경로**: 실시간 블랙리스트 검사(`OnTokenValidated` 이벤트, `JwtValidationMiddleware`)는 지연시간 최소화를 위해 **Redis만** 조회합니다(`TokenBlacklistService.IsBlacklistedAsync`). 이 MySQL 테이블은 `TokenBlacklistService.AddToBlacklistAsync`가 `BlacklistWriteQueue`에 적재한 항목을 `BlacklistDbWriterHostedService`가 배치로 모아 `IBlacklistedAccessTokenRepository.AddRangeAsync`로 기록하며, 감사 로그(누가 언제 어떤 토큰을 왜 폐기했는지 추적) 및 `BlacklistWarmupHostedService`가 서버 기동 시 Redis를 복원하는 소스로 사용됩니다. 만료된 레코드는 `BlacklistCleanupHostedService`가 주기적으로 삭제합니다.

### 6.2 테이블 간 관계 요약

```
Users (1) ────< (N) RefreshTokens        # UserId로 연결 (앱 레벨 참조, FK 제약 없음)
Users (1) ────< (N) [JWT Access Token]   # DB에 직접 저장되지 않고 매 요청 시 서명 검증
BlacklistedAccessTokens                  # RefreshTokens/Users와 직접적인 FK 관계 없음, Jti로 독립 식별
```

Access 토큰 자체는 DB에 저장되지 않는 stateless 방식이며, 폐기가 필요한 순간(로그아웃 등)에만 `Jti`를 블랙리스트에 등록하는 구조입니다. 세 테이블 모두 외래키(FK) 제약은 선언되어 있지 않아, 참조 무결성은 애플리케이션 로직(`UserRepository`, `RefreshTokenRepository`)에서 책임집니다.

## 7. 테스트

`AuthFlowIntegrationTests`(Json/Protobuf 각각)는 `WebApplicationFactory` 기반 통합 테스트로 **회원가입 → 로그인 → 사용자 정보 조회 → 토큰 정보 조회 → 토큰 갱신 → 로그아웃** 전체 흐름을 한 번에 검증합니다. 실제 서버(MySQL/Redis 연결)를 띄우고 `dotnet test`로 실행하는 방식이며, xUnit `ITestOutputHelper`로 상세 로그를 출력하도록 되어 있습니다.

## 8. 사용 패키지 (JwtAuthCommon 기준)

| 패키지 | 용도 |
|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT Bearer 인증 |
| `System.IdentityModel.Tokens.Jwt` | 토큰 생성/파싱 |
| `Microsoft.EntityFrameworkCore` + `Pomelo.EntityFrameworkCore.MySql` | MySQL ORM |
| `StackExchange.Redis` | 블랙리스트 캐시 |
| `BCrypt.Net-Next` | 비밀번호 해시 |

Protobuf 프로젝트에는 `Google.Protobuf`, `Grpc.Tools`(코드 생성 전용, 서버 실행에는 미사용)가 추가됩니다.

## 9. 특징 및 설계 평가 요약

**잘 구성된 부분**
- 공통 로직(`JwtAuthCommon`)과 전송 계층(Json/Protobuf)의 분리가 명확함.
- Refresh Token Rotation + 기기 바인딩 + 탈취 감지(기기 불일치 시 즉시 폐기) 등 실무형 보안 패턴 반영.
- Access 토큰 블랙리스트를 **Redis(실시간 검증) + MySQL(영속 기록·복구)** 이중 저장 구조로 설계하여, 평상시 조회 성능은 유지하면서 Redis 장애 시에도 `BlacklistWarmupHostedService`로 복원 가능.
- 블랙리스트 DB 기록을 요청 경로에서 바로 처리하지 않고 `BlacklistWriteQueue`(인메모리 큐) → `BlacklistDbWriterHostedService`(배치 소비)로 분리해, 트래픽이 몰려도 요청당 스코프 생성/DB 접근 비용이 늘지 않고 일정한 배치 주기로 흡수되도록 설계.
- `BlacklistCleanupHostedService`가 주기적으로 만료된 블랙리스트 DB 레코드를 정리해 테이블이 무한정 커지는 문제를 방지.
- Admin ID 채번 시 `FOR UPDATE` 비관적 잠금으로 동시성 제어.
- 통합 테스트로 전체 인증 흐름을 검증.

**참고/개선 여지가 있는 부분**
- `JwtValidationMiddleware`와 `AddJwtBearer` 인증이 기능적으로 중복됨.
- `EnsureCreated()`는 스키마 변경 이력 관리가 안 되므로 운영 전환 시 EF Migration 전환 필요.
- `appsettings.json`에 JWT Secret과 DB 비밀번호가 평문으로 커밋되어 있어(예: `villdev!@`), 실제 배포 전에는 반드시 User Secrets/환경변수/Key Vault 등으로 분리 필요.
- Protobuf 프로젝트의 `services.proto`는 gRPC 서비스 정의가 있으나 `GrpcServices="None"`이라 실제로는 REST+Protobuf 방식만 사용 — 두 방식(gRPC vs REST-protobuf) 중 향후 방향을 정리하면 좋을 것 같습니다.
- `BlacklistWriteQueue`가 `Unbounded` 채널이라, DB 쪽 장애가 길어지면(`BlacklistDbWriterHostedService`가 계속 실패) 큐가 무제한으로 쌓여 메모리를 압박할 수 있습니다. 매우 긴 DB 장애까지 대비하려면 채널을 `Bounded`로 바꾸고 초과분 처리 정책(드롭/블로킹 등)을 정의하는 것도 고려해볼 만합니다.


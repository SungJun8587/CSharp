# FcmPushAgent

MySQL에 저장된 대량(최대 천만 명 이상) 유저에게 예약된 시각에 Firebase Cloud Messaging(FCM) 푸시 알림을 발송하는 C# 백그라운드 서비스입니다.

발송 예약은 DB(`push_jobs` 테이블)에 행을 추가하는 것만으로 등록되며, 프로세스는 이 테이블을 주기적으로 폴링하면서 예약 시각이 도래한 작업을 찾아 자동으로 실행합니다.

---

## 목차

1. [핵심 설계 목표](#핵심-설계-목표)
2. [전체 아키텍처](#전체-아키텍처)
3. [동작 흐름](#동작-흐름)
4. [데이터베이스 스키마](#데이터베이스-스키마)
5. [파일 구성](#파일-구성)
6. [동시성 모델](#동시성-모델)
7. [데이터 접근 계층 (Dapper)](#데이터-접근-계층-dapper)
8. [FCM 처리율 제어](#fcm-처리율-제어)
9. [모니터링 및 알림](#모니터링-및-알림)
10. [장애 복구 (체크포인트 & Lease)](#장애-복구-체크포인트--lease)
11. [설정 항목](#설정-항목)
12. [실행 방법](#실행-방법)
13. [운영 시 참고사항](#운영-시-참고사항)
14. [향후 개선 여지](#향후-개선-여지)

---

## 핵심 설계 목표

| 목표 | 해결 방법 |
|---|---|
| 천만 건 규모의 유저를 효율적으로 순회 | `OFFSET` 대신 **keyset pagination** (`WHERE id > lastId ORDER BY id LIMIT n`) 사용 |
| DB 읽기와 FCM 발송 속도 차이 흡수 | `Channel<T>` 기반 **Producer-Consumer 파이프라인** |
| 예약 발송을 코드 재배포 없이 등록 | 예약시간/알림 내용을 **DB 테이블(push_jobs)**에서 읽음 |
| 여러 발송 작업을 동시에 처리 | 한 프로세스 안에서 **최대 N개 job 병렬 실행** |
| 여러 서버 인스턴스로 수평 확장 | `FOR UPDATE SKIP LOCKED` 기반 **분산 잠금(lease)** |
| 프로세스 중단 후 안전한 재개 | **Consumer 발송 완료 지점(`LastConfirmedId`) 기준 체크포인트** + 재시작 시 자동 이어하기 |
| FCM 무효 토큰 누적 방지 | `Unregistered`/`InvalidArgument` 응답 시 **무효 토큰 Bulk UPDATE로 일괄 정리** |
| FCM 쿼터 초과 방지 | 프로세스 전체가 공유하는 **토큰 버킷 Rate Limiter**로 초당 호출 수 제한 |
| FCM 일시적 오류 발송 성공률 극대화 | `QuotaExceeded`/`Unavailable` 실패 토큰만 분리해 **점증 백오프 재시도** |
| 재개 시 중복 발송 범위 최소화 | 채널 전체 용량 대신 **Consumer 처리 완료 지점 + 1배치 마진**으로 재개 지점 계산 |
| 장애/이상 징후를 신속히 인지 | **구조화된 로깅(Serilog)** + 실패율 임계치 초과 시 **Webhook 알림** |
| DB 코드의 가독성/안전성 확보 | raw `MySqlCommand` 대신 **Dapper**로 파라미터 바인딩과 결과 매핑 자동화 |
| Firebase 자격증명 보안 | deprecated `GoogleCredential.FromFile` 대신 **`CredentialFactory.FromFile<ServiceAccountCredential>`** 사용 |

---

## 전체 아키텍처

```
┌─────────────────────────────────────────────────────────────────┐
│                         Program.cs (진입점)                       │
│   appsettings.json 로드 → 의존성 구성 → RunForeverAsync 시작       │
└───────────────────────────────┬───────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                    FcmPushAgent.RunForeverAsync              │
│         (push_jobs 테이블을 PollingIntervalSeconds 주기로 폴링)     │
│                                                                     │
│   빈 슬롯 있음? → ClaimRunnableJobsAsync 로 최대 N개 job 선점        │
│   각 job마다 ExecuteJobAsync 를 별도 Task로 병렬 실행                │
└───────────────────────────────┬───────────────────────────────────┘
                                 │  (job마다 독립적으로 아래 파이프라인 실행)
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│                    FcmPushAgent.ExecuteJobAsync               │
│                                                                     │
│   ┌──────────────┐      Channel<T>       ┌───────────────────┐    │
│   │   Producer   │ ───── (버퍼) ────────▶ │  Consumer × N개     │    │
│   │ (users 테이블 │                       │ (FcmRateLimiter    │    │
│   │  keyset 읽기) │                       │  토큰 획득 후       │    │
│   └──────────────┘                       │  FCM Multicast      │    │
│                                           │  묶음 발송)         │    │
│                                           └───────────────────┘    │
│                                                                     │
│   ┌──────────────────────────────────────────────────────────┐   │
│   │         RenewLeaseLoop (claimed_at 주기적 갱신)              │   │
│   └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                 │                              │
                 ▼                              ▼
        ┌─────────────────┐           ┌─────────────────────┐
        │  push_jobs 테이블  │           │   FCM (Firebase)     │
        │ (진행상황/lease 저장)│          │  무효 토큰 → users 정리 │
        └─────────────────┘           └─────────────────────┘
                 │
                 ▼ (job 완료/오류/실패율 임계치 초과 시)
        ┌─────────────────────┐
        │  AlertNotifier        │
        │  (Slack 호환 Webhook)  │
        └─────────────────────┘
```

> `FcmRateLimiter`는 job 단위가 아니라 **프로세스 전체에서 1개만 생성**되어 모든 job/Consumer가 공유합니다. 여러 job이 동시에 돌아도 합산 FCM 호출량이 쿼터를 넘지 않도록 하기 위함입니다.

---

## 동작 흐름

### 1. 새 발송 작업 등록

운영자가 `push_jobs` 테이블에 행을 하나 추가합니다. 별도 배포나 재시작이 필요 없습니다.

```sql
INSERT INTO push_jobs (job_id, scheduled_time, notification_title, notification_body)
VALUES ('push-2026-06-20-event', '2026-06-20 10:00:00', '이벤트 안내', '오늘부터 신규 이벤트가 시작됩니다!');
```

### 2. 폴링 및 선점

`FcmPushAgent.RunForeverAsync`가 `PollingIntervalSeconds`(기본 10초)마다 다음 조건을 만족하는 작업을 조회합니다.

- `status = 'pending'` 이고 `scheduled_time`이 현재 UTC 시각보다 이전이거나 같은 작업
- 또는 `status = 'running'`이지만 `claimed_at`이 `StaleLeaseMinutes`(기본 5분) 이상 갱신되지 않은 작업 (죽은 워커가 처리하던 작업)

조회는 `SELECT ... FOR UPDATE SKIP LOCKED` 트랜잭션으로 이루어져, 여러 워커가 동시에 폴링해도 같은 작업을 중복으로 가져가지 않습니다.

### 3. 작업 실행 (Producer-Consumer 파이프라인)

선점된 job마다 `ExecuteJobAsync`가 독립적으로 실행됩니다.

- **Producer** (1개): `users` 테이블을 `id` 기준으로 `DbFetchSize`만큼씩 읽어 `Channel<T>`에 적재합니다.
- **Consumer** (`ConsumerCount`개, 기본 8개): 채널에서 토큰을 꺼내 `FcmBatchSize`(최대 500)만큼 모아 FCM에 묶음 발송합니다. 응답은 세 가지로 분류됩니다.
  - **성공**: 카운터 반영 후 종료
  - **영구 실패** (`Unregistered`, `InvalidArgument`): DB에서 즉시 Bulk 무효화
  - **일시적 실패** (`QuotaExceeded`, `Unavailable`): 해당 토큰만 분리해 점증 백오프(1초, 2초) 후 최대 3회 재시도
- **Lease 갱신 루프** (1개): `LeaseRenewalSeconds`(기본 60초)마다 `claimed_at`을 갱신해, 처리 시간이 길어도 다른 워커에게 작업을 빼앗기지 않게 합니다.

### 4. 완료 또는 중단

- 정상 완료 시: 최종 진행상황을 저장하고 `status`를 `completed`로 변경, `claimed_by`/`claimed_at`을 해제합니다.
- 예외나 취소 발생 시: 현재까지의 진행상황만 저장하고 종료합니다. `status`는 `running`으로 남아있다가, lease가 만료되면 다른(혹은 같은) 워커가 자동으로 재선점하여 이어서 처리합니다.

---

## 데이터베이스 스키마

### `users` — 발송 대상 유저

| 컬럼 | 타입 | Null 허용 | 설명 |
|---|---|---|---|
| `id` | `BIGINT AUTO_INCREMENT PK` | N | keyset pagination 기준 키 |
| `push_token` | `VARCHAR(255)` | Y | FCM 디바이스 토큰. NULL/빈 값이면 발송 대상에서 제외 |
| `push_token_invalidated_at` | `DATETIME` | Y | FCM이 무효 응답 시 토큰이 무효화된 시각 |
| `created_at` | `DATETIME` | N | 유저 생성 시각 |
| `updated_at` | `DATETIME` | N | 유저 행 최종 수정 시각 (어떤 컬럼이든 변경되면 자동 갱신) |

> 인덱스: `idx_users_id_token (id, push_token)` — keyset pagination 성능의 핵심.

### `push_jobs` — 발송 작업 (예약/진행상태/분산락 통합 관리)

| 컬럼 | 타입 | Null 허용 | 설명 |
|---|---|---|---|
| `job_id` | `VARCHAR(100) PK` | N | 작업 고유 식별자 |
| `scheduled_time` | `DATETIME` | N | 발송 예약 시각 (UTC 비교) |
| `status` | `ENUM('pending','running','completed','failed')` | N | 작업 상태 |
| `notification_title` | `VARCHAR(255)` | N | FCM 알림 제목 |
| `notification_body` | `VARCHAR(1000)` | N | FCM 알림 본문 |
| `last_processed_id` | `BIGINT` | N | 재개 시 시작할 `users.id` 커서 (체크포인트) |
| `total_read` / `total_success` / `total_failure` | `BIGINT` | N | 누적 처리 통계 |
| `claimed_by` | `VARCHAR(100)` | Y | 현재 선점 중인 워커 인스턴스 식별자 |
| `claimed_at` | `DATETIME` | Y | 마지막 lease 갱신 시각 (UTC) |
| `created_at` | `DATETIME` | N | 작업 등록 시각 |
| `updated_at` | `DATETIME` | N | 작업 행 최종 수정 시각 (어떤 컬럼이든 변경되면 자동 갱신) |

> 인덱스: `idx_status_scheduled (status, scheduled_time)`, `idx_status_claimed (status, claimed_at)`

발송 처리량(배치 크기, 동시성 등)은 job별로 다르게 두지 않고 `appsettings.json`의 전역 설정(`PushDefaults`)을 사용합니다.

전체 DDL은 [`schema.sql`](./schema.sql)을 참고하세요.

---

## 파일 구성

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점. 설정 로드, 의존성 구성, Ctrl+C 취소 처리, 폴링 루프 시작 |
| `FcmPushAgent.cs` | 핵심 로직. 폴링 루프(`RunForeverAsync`), job 실행(`ExecuteJobAsync`), Producer/Consumer/Lease 갱신, FCM 발송(`SendBatchAsync`) |
| `PushJobRepository.cs` | `push_jobs` 테이블 CRUD. 작업 선점(`ClaimRunnableJobsAsync`), 진행상황 갱신, lease 갱신/해제 |
| `TokenCleanupRepository.cs` | FCM에서 무효 응답을 받은 토큰을 `users` 테이블에서 정리 |
| `FcmRateLimiter.cs` | 토큰 버킷 방식으로 FCM 초당 호출 수를 제한하는 Rate Limiter |
| `AlertNotifier.cs` | job 완료/오류/실패율 임계치 초과를 Slack 호환 Webhook으로 알림 |
| `schema.sql` | `users`, `push_jobs` 테이블 DDL (컬럼 코멘트 포함) |
| `appsettings.json` | DB 연결 문자열, 전역 발송 옵션, Firebase 자격증명 경로, 모니터링 설정 |
| `FcmPushAgent.csproj` | 프로젝트 정의 및 NuGet 패키지 참조 |

### 주요 클래스/메서드 요약

**`FcmPushAgent` (FcmPushAgent.cs)**
- `RunForeverAsync(ct)` — push_jobs를 폴링하며 빈 슬롯만큼 작업을 선점해 병렬 실행하는 메인 루프
- `ExecuteJobAsync(job, ct)` — 작업 1건을 Producer-Consumer 파이프라인으로 끝까지 처리
  - `ProduceAsync()` (로컬 함수) — keyset pagination으로 `users`를 읽어 채널에 적재
  - `ConsumeAsync(workerId)` (로컬 함수) — 채널에서 꺼내 FCM 배치 발송
  - `RenewLeaseLoopAsync()` (로컬 함수) — 주기적으로 `claimed_at` 갱신
- `SendBatchAsync(...)` — FcmRateLimiter 토큰 획득 후 FCM Multicast 호출. 응답을 성공/영구실패(`Unregistered`, `InvalidArgument`)/일시적 실패(`QuotaExceeded`, `Unavailable`)로 분류해 일시적 실패 토큰만 점증 백오프 재시도. 영구 실패 토큰은 Bulk UPDATE로 즉시 정리. 발송 완료 후 `counters.LastConfirmedId`를 CAS로 갱신

**`PushJobRepository` (PushJobRepository.cs)**
- `ClaimRunnableJobsAsync(maxJobs, workerInstanceId, staleLeaseThreshold, ct)` — `FOR UPDATE SKIP LOCKED`로 실행 가능한 작업을 선점
- `RenewLeaseAsync(jobId, ct)` — lease 갱신
- `UpdateStatusAsync(jobId, status, ct)` — 상태 전이 (완료/실패 시 lease 해제)
- `UpdateProgressAsync(...)` — 체크포인트(진행 커서, 누적 카운터) 저장

**`TokenCleanupRepository` (TokenCleanupRepository.cs)**
- `InvalidateTokensAsync(tokens, ct)` — 전달된 토큰들의 `push_token`을 `NULL` 처리

**`FcmRateLimiter` (FcmRateLimiter.cs)**
- `AcquireAsync(ct)` — 토큰 1개를 획득할 때까지 대기 (FCM 호출 직전 반드시 호출)
- 내부적으로 `SemaphoreSlim`을 토큰 버킷처럼 사용하며, 1초마다 타이머로 토큰을 리필

**`AlertNotifier` (AlertNotifier.cs)**
- `NotifyJobCompletedAsync(...)` — job 완료 요약 알림
- `NotifyIfFailureRateExceededAsync(...)` — 실패율이 임계치를 초과할 때만 경고 알림
- `NotifyJobErrorAsync(...)` — job이 예외로 중단되었을 때 알림

---

## 동시성 모델

### 1) Job 내부 동시성 — `Channel<T>`

```
[Producer: 1개]  →  Channel<(long Id, string Token)>  →  [Consumer: ConsumerCount개]
   (DB 읽기)         BoundedChannel, FullMode=Wait              (FCM 발송)
```

- `BoundedChannel`을 사용해 채널 버퍼가 `ChannelCapacity`를 넘으면 Producer가 자동으로 대기합니다. DB 읽기 속도가 FCM 발송 속도보다 빠른 경우 메모리 사용량이 무한정 늘어나는 것을 방지합니다.
- `SingleWriter = true`(Producer 1개), `SingleReader = false`(Consumer 여러 개)로 설정해 약간의 성능 이점을 얻습니다.

### 2) Job 간 동시성 — 동일 프로세스 내 병렬 처리

`RunForeverAsync`는 `Dictionary<jobId, Task>`로 진행 중인 작업을 추적하며, `MaxConcurrentJobs`(기본 3개) 한도 내에서 여러 job을 동시에 `ExecuteJobAsync`로 실행합니다. 각 job은 완전히 독립된 Channel과 Producer/Consumer 그룹을 가지므로 서로 간섭하지 않습니다.

### 3) 프로세스 간 동시성 — 분산 잠금 (Lease)

여러 서버에 동일 프로그램을 띄워도 안전하게 동작하도록, DB 트랜잭션 기반의 분산 잠금을 사용합니다.

```sql
SELECT job_id FROM push_jobs
WHERE (status = 'pending' AND scheduled_time <= UTC_TIMESTAMP())
   OR (status = 'running' AND (claimed_at IS NULL OR claimed_at <= @staleBefore))
ORDER BY (status = 'running') DESC, scheduled_time ASC
LIMIT @maxJobs
FOR UPDATE SKIP LOCKED
```

`SKIP LOCKED` 덕분에 다른 워커(같은 프로세스의 다른 폴링 사이클이든, 다른 서버의 다른 프로세스이든)가 이미 잠근 행은 자동으로 건너뜁니다. 선점 직후 같은 트랜잭션에서 `status='running'`, `claimed_by`, `claimed_at`을 기록해 "이 워커가 처리 중"이라는 lease를 남깁니다.

### 동시 실행 작업 단위 규모 (기본 설정 기준)

| 항목 | 개수 |
|---|---|
| Job당 동시 Task (Producer 1 + Consumer 8 + Lease 갱신 1) | 10개 |
| 동시 처리 Job 수 (`MaxConcurrentJobs`) | 최대 3개 |
| 전체 동시 Task | 최대 약 30개 |

이 작업들은 모두 I/O(DB 쿼리, FCM HTTP 호출) 대기가 대부분인 비동기 작업이라, 실제로 점유하는 OS 스레드 수는 .NET ThreadPool이 동적으로 관리하며 Task 개수보다 훨씬 적습니다.

---

## 데이터 접근 계층 (Dapper)

모든 MySQL 접근은 raw `MySqlCommand`/`MySqlDataReader` 대신 **Dapper**(IDbConnection 확장 메서드 기반 Micro-ORM)를 통해 이루어집니다. 연결 자체는 기존과 동일하게 `MySqlConnection`을 직접 열고 닫지만(연결 수명 관리 방식은 변경 없음), 쿼리 실행/파라미터 바인딩/결과 매핑을 Dapper가 대신합니다.

### 적용 범위

| 파일 | 사용 예 |
|---|---|
| `PushJobRepository.cs` | `ClaimRunnableJobsAsync`의 `SELECT ... FOR UPDATE SKIP LOCKED` + `UPDATE`, 진행상황/lease 갱신 등 모든 쿼리 |
| `TokenCleanupRepository.cs` | 무효 토큰 일괄 `UPDATE ... WHERE push_token IN @Tokens` |
| `FcmPushAgent.cs` | `ProduceAsync`의 `users` 테이블 keyset pagination 조회 |

### 달라진 점

- **파라미터 바인딩**: `cmd.Parameters.AddWithValue(...)`를 직접 호출하던 코드가 익명 객체(`new { LastId = ..., Limit = ... }`)로 대체되어, 파라미터 이름과 값이 한눈에 보입니다.
- **IN 절 자동 전개**: 토큰 목록이나 job_id 목록처럼 가변 개수의 값을 `IN` 절에 넣을 때, 기존에는 `@t0, @t1, @t2...`처럼 파라미터 이름을 수동으로 생성해야 했습니다. Dapper는 `List<string>`을 파라미터로 넘기면 `IN @Tokens` 형태 그대로 자동으로 전개해줍니다.
- **결과 매핑**: `reader.GetInt64(0)`, `reader.GetString(1)`처럼 컬럼 순서에 의존하던 매핑이 `QueryAsync<PushJobRow>`, `QueryAsync<UserTokenRow>`처럼 타입 기반 자동 매핑으로 바뀌었습니다. SQL의 컬럼 별칭(`AS JobId`, `AS PushToken` 등)이 DTO 프로퍼티명과 매칭됩니다.
- **트랜잭션/취소 토큰 전달**: `CommandDefinition`을 사용해 SQL, 파라미터, 트랜잭션, `CancellationToken`을 한 번에 묶어 전달합니다. `ClaimRunnableJobsAsync`처럼 트랜잭션 안에서 `SELECT ... FOR UPDATE`와 `UPDATE`를 함께 실행하는 코드에서도 동일하게 동작합니다.
- **ENUM 매핑**: `push_jobs.status`는 MySQL `ENUM('pending','running',...)`이라 소문자 문자열로 내려오는 반면, C# 쪽은 `JobStatus`(PascalCase) enum입니다. Dapper는 이름이 정확히 일치할 때만 문자열→enum을 자동 매핑하므로, `status`를 `string`으로 받는 내부 DTO(`PushJobRow`)를 두고 `Enum.Parse(..., ignoreCase: true)`로 수동 변환합니다.
- **Firebase 자격증명**: deprecated된 `GoogleCredential.FromFile(path)`를 사용하지 않고 `CredentialFactory.FromFile<ServiceAccountCredential>(path).ToGoogleCredential()`을 사용합니다. 잠재적 보안 위험이 제거된 권장 방식입니다.

---

## FCM 처리율 제어

`ConsumerCount`만으로 동시성을 제어하면, 동시에 여러 job이 돌거나 Consumer 수가 많을 때 순간적으로 FCM 프로젝트의 초당 쿼터를 초과해 `429 Too Many Requests`나 `QuotaExceeded` 오류가 발생할 수 있습니다. 이를 막기 위해 `FcmRateLimiter`가 프로세스 전체의 FCM 호출 속도를 명시적으로 제한합니다.

### 동작 방식 (토큰 버킷)

```
시작 시: 토큰 MaxFcmCallsPerSecond개로 가득 채움
매 1초: 토큰을 다시 MaxFcmCallsPerSecond개까지 리필
FCM 호출 직전: AcquireAsync()로 토큰 1개 획득 (없으면 다음 리필까지 대기)
```

- `SemaphoreSlim`을 토큰 버킷처럼 사용합니다. 가용 토큰 수가 곧 세마포어의 현재 카운트입니다.
- 백그라운드 `Timer`가 1초마다 `RefillTokens`를 호출해 토큰을 `MaxFcmCallsPerSecond`까지 채웁니다.
- **모든 job, 모든 Consumer가 이 Limiter 인스턴스 하나를 공유**합니다 (`FcmPushAgent` 생성자에서 1회 생성). job마다 따로 만들면 job 개수만큼 쿼터가 곱해져 버리므로, 반드시 프로세스 전체에서 공유되어야 합니다.
- 재시도(`SendBatchAsync`의 retry 루프) 시에도 매번 토큰을 다시 소비하므로, 재시도 트래픽까지 포함해 쿼터 안에서 동작합니다.

### 처리량 계산 예시

`MaxFcmCallsPerSecond = 20`, `FcmBatchSize = 500`인 경우:

```
초당 최대 발송 = 20 × 500 = 10,000명/초
천만 명 전체 발송 소요 시간 ≈ 1,000초 (약 16.7분, 이론적 최댓값)
```

실제로는 DB 읽기 속도, 네트워크 지연, 재시도 등으로 이보다 더 걸릴 수 있습니다. FCM 콘솔에서 실제 허용 쿼터를 확인한 뒤 여유 있게 설정하는 것을 권장합니다.

### FCM 오류 분류 및 재시도 전략

`SendEachForMulticastAsync` 응답은 배치 내 토큰별로 개별 결과를 반환합니다. 배치 전체가 아닌 **토큰 단위**로 오류를 분류해 처리합니다.

| 분류 | 오류 코드 | 처리 방식 |
|---|---|---|
| 성공 | — | 카운터 반영 후 완료 |
| 영구 실패 | `Unregistered`, `InvalidArgument` | DB에서 즉시 Bulk 무효화, 재시도 없음 |
| 일시적 실패 | `QuotaExceeded`, `Unavailable` | 해당 토큰만 분리, 최대 3회 점증 백오프 재시도 |
| 기타 실패 | 그 외 | 로그만 기록, 재시도 없음 |

일시적 실패 재시도 시에도 Rate Limiter 토큰을 다시 소비하므로, 재시도 트래픽까지 포함해 초당 쿼터를 넘지 않습니다. FCM 호출 자체가 실패(네트워크 오류 등)한 경우에는 배치 전체를 재시도합니다.

---

## 모니터링 및 알림

### 구조화된 로깅 (Serilog)

기존 `Console.WriteLine` 기반 로깅을 Serilog로 전환했습니다.

- **콘솔 출력**: 사람이 읽기 좋은 텍스트 형식 (개발/운영 중 실시간 확인용)
- **파일 출력**: `logs/mass-push-YYYYMMDD.json` 경로에 [Compact JSON 포맷](https://github.com/serilog/serilog-formatting-compact)으로 저장. 날짜별로 롤링되며 최근 14일치만 보관됩니다.
- JSON 형식이라 ELK, Datadog, Grafana Loki 같은 로그 수집 파이프라인에 별도 파싱 없이 연결할 수 있습니다.
- `job_id`, `worker_id`, 진행 건수 등 주요 값이 메시지 템플릿의 named property(`{JobId}`, `{TotalRead}` 등)로 구조화되어, 로그 검색/필터링이 단순 문자열 grep보다 훨씬 쉬워집니다.

### Webhook 알림 (Slack 호환)

`AlertNotifier`가 다음 3가지 이벤트에서 Slack Incoming Webhook 형식(`{"text": "..."}`)으로 알림을 보냅니다. Discord, Mattermost 등 동일 포맷을 지원하는 협업툴에도 그대로 연동됩니다.

| 이벤트 | 발생 시점 | 메서드 |
|---|---|---|
| 작업 완료 요약 | job이 정상적으로 끝났을 때 (성공/실패 무관) | `NotifyJobCompletedAsync` |
| 실패율 임계치 초과 경고 | job 완료 시점에 실패율이 `FailureRateAlertThreshold`(기본 5%)를 넘었을 때만 | `NotifyIfFailureRateExceededAsync` |
| 작업 오류 알림 | job 처리 중 예외가 발생해 중단되었을 때 | `NotifyJobErrorAsync` |

`Monitoring:AlertWebhookUrl`을 비워두면 `AlertNotifier.IsEnabled`가 `false`가 되어 모든 알림이 조용히 무시됩니다. 즉, 알림 설정 없이도 기존과 동일하게 동작하며, 운영 환경에서만 URL을 채워 넣으면 됩니다.

알림 전송 자체가 실패(네트워크 오류 등)하더라도 예외를 흡수하고 경고 로그만 남기므로, 알림 인프라 장애가 푸시 발송 파이프라인에 영향을 주지 않습니다.

---

## 장애 복구 (체크포인트 & Lease)

### 체크포인트 (재개 지점 저장)

`ProduceAsync`는 DB 배치(`DbFetchSize`)를 읽을 때마다 `UpdateProgressAsync`를 호출해 다음 값을 저장합니다.

- `last_processed_id` — 다음 재개 시 시작할 `users.id` 커서 (`GetSafeResumeId()` 계산값)
- `total_read`, `total_success`, `total_failure` — 누적 통계

### 재개 시 안전 마진 (LastConfirmedId 기반)

크래시 시점에는 Channel 버퍼에 있던 데이터와 Consumer가 처리 중이던 배치가 유실될 수 있습니다. 재개 지점은 **Producer가 읽은 위치(lastReadId)** 가 아니라 **Consumer가 FCM 발송을 완료한 위치(`counters.LastConfirmedId`)** 를 기준으로 계산합니다.

```
재개 지점 = max(0, LastConfirmedId - (FcmBatchSize × ConsumerCount))
```

| 항목 | 기존 방식 | 개선된 방식 |
|---|---|---|
| 기준 커서 | Producer가 읽은 `lastReadId` | Consumer가 발송 완료한 `LastConfirmedId` |
| 마진 크기 | `ChannelCapacity + FcmBatchSize × ConsumerCount` | `FcmBatchSize × ConsumerCount` |
| 중복 발송 최대 범위 | 최대 ChannelCapacity(기본 20,000)건 | 최대 ConsumerCount × FcmBatchSize(기본 4,000)건 |

`LastConfirmedId`는 각 Consumer가 배치 발송을 마칠 때마다 CAS(`CompareExchange`) 루프로 단조 증가 갱신합니다. 여러 Consumer가 동시에 갱신해도 항상 더 큰 값만 기록됩니다.

이 설계는 **"발송 누락"보다 "약간의 중복 발송"을 선택**한 것입니다. 공지성 푸시 알림 특성상 일부 중복 수신이 누락보다 안전하다는 전제입니다.

### 무효 토큰 정리 (Bulk UPDATE)

`SendBatchAsync`가 FCM 응답에서 `Unregistered`/`InvalidArgument` 토큰을 수집한 뒤 `TokenCleanupRepository.InvalidateTokensAsync`를 호출합니다. 내부는 Dapper의 `IN @Tokens` 자동 전개를 활용한 **단일 Bulk UPDATE** 쿼리로 동작해, 건당 쿼리 없이 한 번에 처리합니다.

```sql
UPDATE users
SET push_token = NULL,
    push_token_invalidated_at = NOW()
WHERE push_token IN (?, ?, ?, ...)
```

### Lease 기반 죽은 워커 감지

- 처리 중인 워커는 `LeaseRenewalSeconds`(기본 60초)마다 `claimed_at`을 갱신합니다.
- 프로세스가 크래시하면 `claimed_at` 갱신이 멈춥니다.
- `StaleLeaseMinutes`(기본 5분) 이상 갱신되지 않은 `running` 작업은 다른 워커가 재선점하여 저장된 `last_processed_id`부터 자동으로 이어서 처리합니다.

### Graceful Shutdown

`Ctrl+C` 입력 시 프로세스를 즉시 종료하지 않고 `CancellationToken`을 통해 안전하게 취소를 전파합니다. 현재 진행 중인 job들은 진행상황을 DB에 저장할 시간을 번 뒤 종료됩니다.

---

## 설정 항목

`appsettings.json`의 `PushDefaults` 섹션에서 전역으로 관리합니다.

| 키 | 기본값 | 설명 |
|---|---|---|
| `DbFetchSize` | 2000 | DB에서 한 번에 읽어올 row 수 |
| `FcmBatchSize` | 500 | FCM Multicast 1회 호출당 토큰 수 (FCM 정책상 최대 500) |
| `ConsumerCount` | 8 | job 1개당 동시에 FCM 발송할 Consumer 워커 수 |
| `ChannelCapacity` | 20000 | Producer-Consumer 간 Channel 버퍼 최대 크기 |
| `PollingIntervalSeconds` | 10 | push_jobs 테이블 폴링 주기(초) |
| `MaxConcurrentJobs` | 3 | 한 프로세스가 동시에 처리할 최대 job 개수 |
| `StaleLeaseMinutes` | 5 | 이 시간 이상 lease가 갱신되지 않으면 재선점 허용 |
| `LeaseRenewalSeconds` | 60 | lease(`claimed_at`) 갱신 주기(초) |
| `MaxFcmCallsPerSecond` | 20 | 프로세스 전체가 공유하는 초당 최대 FCM 호출(Multicast) 수 |

`ConnectionStrings:MySql`에 DB 연결 문자열을, `Firebase:CredentialPath`에 서비스 계정 키 파일 경로를 지정합니다.

`Monitoring` 섹션에서 알림 관련 설정을 관리합니다.

| 키 | 기본값 | 설명 |
|---|---|---|
| `AlertWebhookUrl` | (빈 문자열) | Slack 호환 Webhook URL. 비워두면 알림 비활성화 |
| `EnvironmentLabel` | `production` | 알림 메시지에 표시할 환경 이름 |
| `FailureRateAlertThreshold` | 0.05 | 이 실패율(5%)을 초과하면 경고 알림 발송 |

---

## 실행 방법

### 1. 사전 준비

#### 1-1. 프로젝트 생성 (이미 .csproj가 있다면 건너뛰기)

```bash
dotnet new console -n FcmPushAgent
cd FcmPushAgent
```

#### 1-2. NuGet 패키지 설치

`FcmPushAgent.csproj`에 이미 모든 패키지가 명시되어 있으므로, 아래처럼 한 번에 복원해도 됩니다.

```bash
dotnet restore
```

새 프로젝트에 하나씩 추가하거나 버전을 직접 맞추고 싶다면 다음 명령을 사용하세요.

```bash
# MySQL 접속
dotnet add package MySqlConnector --version 2.3.5

# 경량 Micro-ORM (SQL 직접 작성 + 결과 매핑/파라미터 바인딩 자동화)
dotnet add package Dapper --version 2.1.35

# FCM(구글 푸시) 발송
dotnet add package FirebaseAdmin --version 2.4.0

# appsettings.json 로드
dotnet add package Microsoft.Extensions.Configuration --version 8.0.0
dotnet add package Microsoft.Extensions.Configuration.Json --version 8.0.0
dotnet add package Microsoft.Extensions.Configuration.FileExtensions --version 8.0.0

# 구조화된 로깅 (콘솔 + 파일(JSON) 동시 출력)
dotnet add package Serilog --version 4.0.1
dotnet add package Serilog.Sinks.Console --version 6.0.0
dotnet add package Serilog.Sinks.File --version 6.0.0
dotnet add package Serilog.Formatting.Compact --version 3.0.0
```

| 패키지 | 용도 |
|---|---|
| `MySqlConnector` | MySQL 연결(`MySqlConnection`) |
| `Dapper` | SQL 결과를 C# 객체로 매핑, 파라미터 바인딩 자동화 |
| `FirebaseAdmin` | FCM Multicast 발송(`FirebaseMessaging`) |
| `Microsoft.Extensions.Configuration` 외 2종 | `appsettings.json` 로드 |
| `Serilog` 외 3종 | 콘솔/파일(JSON) 동시 로깅 |

#### 1-3. DB 스키마 생성

```bash
mysql -u <user> -p <database> < schema.sql
```

- Firebase 콘솔에서 서비스 계정 키 JSON을 다운로드해 `serviceAccountKey.json`으로 프로젝트 폴더에 배치합니다.
- `appsettings.json`에서 `ConnectionStrings:MySql`을 실제 DB 정보로 수정합니다.

### 2. 발송 작업 등록

```sql
INSERT INTO push_jobs (job_id, scheduled_time, notification_title, notification_body)
VALUES ('push-2026-07-01-notice', '2026-07-01 09:00:00', '공지사항', '새로운 업데이트가 도착했습니다!');
```

### 3. 실행

```bash
dotnet run
```

프로세스가 떠 있는 동안 `push_jobs` 테이블을 계속 폴링하며, 예약 시각이 된 작업을 자동으로 처리합니다.

---

## 운영 시 참고사항

- **MaxConcurrentJobs를 늘릴 때**: job마다 MySQL 연결과 FCM 동시 호출이 함께 늘어나므로, MySQL의 `max_connections`와 FCM 처리율(quota)을 함께 점검해야 합니다.
- **여러 서버로 확장할 때**: 동일한 `appsettings.json`(같은 DB를 바라보는)으로 프로그램을 여러 대에 띄우기만 하면 됩니다. lease 메커니즘이 중복 처리를 방지합니다.
- **무효 토큰 처리**: FCM이 `Unregistered`/`InvalidArgument`로 응답한 토큰은 자동으로 `users.push_token = NULL`로 정리되어 다음 발송부터 제외됩니다.
- **인덱스**: `users` 테이블 규모가 커질수록 `idx_users_id_token` 인덱스가 keyset pagination 성능에 필수적입니다.
- **MaxFcmCallsPerSecond 설정**: 여러 서버 인스턴스를 동시에 띄우는 경우, 이 값은 인스턴스별로 각각 적용됩니다(현재는 프로세스 로컬 Rate Limiter). 인스턴스 3대를 띄우고 각각 20으로 설정했다면 합산 최대 호출량은 60/초가 되므로, FCM 전체 쿼터를 인스턴스 수로 나눠서 설정하세요.
- **알림 빈도 조절**: `FailureRateAlertThreshold`를 너무 낮게 잡으면 정상적인 변동(예: 일시적 네트워크 지연)에도 알림이 자주 발생할 수 있습니다. 운영 초기에는 여유 있게(예: 10%) 설정한 뒤 점진적으로 낮추는 것을 권장합니다.

---

## 향후 개선 여지

- **스케줄 자체의 영속성 강화**: 현재 폴링 주기(`PollingIntervalSeconds`)는 단순 `Task.Delay` 기반입니다. 보다 정교한 스케줄링(우선순위, cron 표현식 등)이 필요하다면 Quartz.NET의 ADO JobStore 같은 영속 스케줄러 도입을 고려할 수 있습니다.
- **인스턴스 간 분산 Rate Limit**: 현재 `FcmRateLimiter`는 프로세스 로컬(in-memory)입니다. 여러 서버 인스턴스를 띄울 경우 Redis 등을 이용한 분산 토큰 버킷으로 바꾸면, 전체 클러스터의 FCM 호출량을 정확히 하나의 쿼터로 통합 관리할 수 있습니다.
- **알림 쿨다운/디듀플리케이션**: 현재 `NotifyJobErrorAsync`는 호출될 때마다 알림을 보냅니다. 동일 job이 짧은 시간 내 반복적으로 재시도-실패를 반복하면 알림이 몰릴 수 있으므로, 동일 job_id에 대해 일정 시간 내 중복 알림을 억제하는 쿨다운 로직을 추가할 수 있습니다.
- **메트릭 노출**: 현재는 로그와 알림 위주입니다. Prometheus `/metrics` 엔드포인트 등을 추가해 처리량, 실패율, 큐 적체량 등을 시계열로 시각화하면 대시보드 구성에 유용합니다.

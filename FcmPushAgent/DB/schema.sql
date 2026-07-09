-- ============================================================
-- users 테이블
--   푸시 발송 대상 유저. id 기준 keyset pagination으로 순회하므로
--   id는 반드시 정렬 가능한 정수 PK(AUTO_INCREMENT)여야 합니다.
-- ============================================================
CREATE TABLE IF NOT EXISTS users (
    id                          BIGINT AUTO_INCREMENT PRIMARY KEY
        COMMENT 'keyset pagination 기준 키. 정렬/연속성이 보장되어야 함',
    push_token                  VARCHAR(255) NULL
        COMMENT 'FCM 디바이스 토큰. NULL/빈값이면 발송 대상에서 제외됨',
    push_token_invalidated_at   DATETIME NULL
        COMMENT 'FCM이 Unregistered/InvalidArgument 응답 시 토큰이 무효화된 시각',
    created_at                  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        COMMENT '유저 생성 시각',
    updated_at                  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        COMMENT '유저 행 최종 수정 시각 (push_token 등 어떤 컬럼이라도 변경되면 자동 갱신됨)',

    -- push_token이 NULL/빈값인 행을 빠르게 걸러내기 위한 인덱스 (keyset pagination 성능 핵심)
    INDEX idx_users_id_token (id, push_token)
) COMMENT = '푸시 발송 대상 유저 테이블';

-- 이미 users 테이블이 존재하는 경우, 누락된 컬럼만 추가
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS push_token_invalidated_at DATETIME NULL
        COMMENT 'FCM이 Unregistered/InvalidArgument 응답 시 토큰이 무효화된 시각';

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        COMMENT '유저 행 최종 수정 시각 (push_token 등 어떤 컬럼이라도 변경되면 자동 갱신됨)';

-- ============================================================
-- push_jobs 테이블
--   예약 시간, 알림 내용, 진행 상태(체크포인트), 분산 잠금(lease)을
--   모두 DB에서 관리합니다. 새 발송은 이 테이블에 INSERT만 하면 됩니다.
--   발송 처리량 옵션(배치 크기, 동시성 등)은 appsettings.json의
--   PushDefaults에서 전역으로 관리합니다.
-- ============================================================
CREATE TABLE IF NOT EXISTS push_jobs (
    job_id               VARCHAR(100) PRIMARY KEY
        COMMENT '작업 고유 식별자 (사람이 읽을 수 있는 이름 권장, 예: push-2026-06-20-event)',
    scheduled_time       DATETIME NOT NULL
        COMMENT '발송 예약 시각 (UTC 기준으로 비교됨). 이 시각이 지나면 pending -> running으로 선점됨',
    status               ENUM('pending','running','completed','failed') NOT NULL DEFAULT 'pending'
        COMMENT '작업 상태. pending=대기, running=처리중(또는 중단되어 재개 대기), completed=완료, failed=영구 실패',

    -- 알림 내용
    notification_title   VARCHAR(255) NOT NULL
        COMMENT 'FCM 알림 제목',
    notification_body    VARCHAR(1000) NOT NULL
        COMMENT 'FCM 알림 본문',

    -- 진행 상태 (체크포인트) - 크래시 후 재개를 위해 주기적으로 갱신됨
    last_processed_id    BIGINT NOT NULL DEFAULT 0
        COMMENT '재개 시 시작할 users.id 위치 (keyset pagination 커서)',
    total_read           BIGINT NOT NULL DEFAULT 0
        COMMENT '지금까지 DB에서 읽은 누적 유저 수',
    total_success        BIGINT NOT NULL DEFAULT 0
        COMMENT 'FCM 발송 성공 누적 건수',
    total_failure        BIGINT NOT NULL DEFAULT 0
        COMMENT 'FCM 발송 실패 누적 건수 (무효 토큰 포함)',

    -- 동시 실행 제어 (분산 lease)
    claimed_by           VARCHAR(100) NULL
        COMMENT '현재 이 job을 선점하여 처리 중인 워커 인스턴스 식별자 (머신명-PID-GUID)',
    claimed_at           DATETIME NULL
        COMMENT '마지막으로 선점/갱신된 시각(UTC). StaleLeaseMinutes 이상 지나면 죽은 워커로 간주하고 재선점됨',

    created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        COMMENT '작업 등록 시각',
    updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        COMMENT '작업 행 최종 수정 시각',

    -- 폴링 시 'pending 상태 + 예약시간 도달' 조회 및 'running + claimed_at' 조회가 빠르도록 인덱스 구성
    INDEX idx_status_scheduled (status, scheduled_time),
    INDEX idx_status_claimed (status, claimed_at)
) COMMENT = '예약 푸시 발송 작업 테이블 (예약시간/진행상태/분산락 관리, 발송 옵션은 전역 설정 사용)';

-- ============================================================
-- 예시: 새 발송 작업 등록
-- ============================================================
-- INSERT INTO push_jobs (job_id, scheduled_time, notification_title, notification_body)
-- VALUES ('push-2026-06-20-event', '2026-06-20 10:00:00', '이벤트 안내', '오늘부터 신규 이벤트가 시작됩니다!');

-- 캐릭터 상태 저장소 스키마 (MySQL 8 / InnoDB, utf8mb4). 여러 번 실행해도 안전하다.
-- Persistence.MySql 의 MySqlPlayerStore 가 쓴다.

CREATE TABLE IF NOT EXISTS players (
    char_id       BIGINT UNSIGNED NOT NULL,
    hero_id       INT UNSIGNED    NOT NULL DEFAULT 0,
    has_vitals    TINYINT(1)      NOT NULL DEFAULT 0,        -- 0이면 hp/mp 를 가져가지 않는다(죽어서 나간 경우 등)
    hp            INT             NOT NULL DEFAULT 0,
    mp            INT             NOT NULL DEFAULT 0,
    arrival_zone  INT UNSIGNED    NOT NULL DEFAULT 0,        -- 0이면 도착 지점 없음. 아니면 이어지는 존 번호 + 1 (PlayerState.ArrivalZone 과 같다)
    arrival_x     FLOAT           NOT NULL DEFAULT 0,
    arrival_y     FLOAT           NOT NULL DEFAULT 0,
    level         INT UNSIGNED    NOT NULL DEFAULT 1,        -- 0이면 저장된 레벨 없음(입장 시 1로 취급)
    exp           INT UNSIGNED    NOT NULL DEFAULT 0,        -- 현재 레벨에서 쌓은 경험치
    updated_at    DATETIME(3)     NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
    PRIMARY KEY (char_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS player_items (
    char_id   BIGINT UNSIGNED NOT NULL,
    slot      SMALLINT UNSIGNED NOT NULL,                    -- 가방 칸 순서(저장할 때 목록의 순서)
    item_id   INT UNSIGNED    NOT NULL,
    item_cnt  INT UNSIGNED    NOT NULL,
    PRIMARY KEY (char_id, slot)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS player_currency (
    char_id      BIGINT UNSIGNED NOT NULL,
    currency_id  INT UNSIGNED    NOT NULL,                   -- GameData 의 Currency 표의 재화 ID
    amount       INT UNSIGNED    NOT NULL,
    PRIMARY KEY (char_id, currency_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS player_skill (
    char_id   BIGINT UNSIGNED NOT NULL,
    skill_id  INT UNSIGNED    NOT NULL,                      -- GameData 의 Skill 표의 스킬 ID
    slot      TINYINT UNSIGNED NOT NULL DEFAULT 0,           -- 장착 칸 1~6, 0 이면 장착하지 않음
    PRIMARY KEY (char_id, skill_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

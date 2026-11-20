-- 001_players.sql 을 player_currency 표가 생기기 전에 이미 적용한 데이터베이스용. 여러 번 실행해도 안전하다.

CREATE TABLE IF NOT EXISTS player_currency (
    char_id      BIGINT UNSIGNED NOT NULL,
    currency_id  INT UNSIGNED    NOT NULL,                   -- GameData 의 Currency 표의 재화 ID
    amount       INT UNSIGNED    NOT NULL,
    PRIMARY KEY (char_id, currency_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- 001_players.sql 을 level/exp 열이 생기기 전에 이미 적용한 데이터베이스용 변경. 한 번만 실행한다(열이 있으면 실패한다).
-- 새로 만드는 데이터베이스는 001 만 실행하면 된다.

ALTER TABLE players
    ADD COLUMN level INT UNSIGNED NOT NULL DEFAULT 1 AFTER arrival_y,
    ADD COLUMN exp   INT UNSIGNED NOT NULL DEFAULT 0 AFTER level;

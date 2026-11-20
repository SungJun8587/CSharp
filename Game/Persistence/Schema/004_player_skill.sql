-- 캐릭터가 보유한 스킬과 장착 칸. 003 까지 적용한 데이터베이스용이며 여러 번 실행해도 안전하다. 새 데이터베이스는 001 + 이 파일을 실행한다.
-- 재화 지갑(player_currency)은 캐릭터 단위다. 유료 재화는 계정 단위 지갑과 거래 장부로 따로 둘 예정이다.

CREATE TABLE IF NOT EXISTS player_skill (
    char_id   BIGINT UNSIGNED NOT NULL,
    skill_id  INT UNSIGNED    NOT NULL,                      -- GameData 의 Skill 표의 스킬 ID
    slot      TINYINT UNSIGNED NOT NULL DEFAULT 0,           -- 장착 칸 1~6, 0 이면 장착하지 않음
    PRIMARY KEY (char_id, skill_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

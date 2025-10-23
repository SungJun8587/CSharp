DROP TABLE IF EXISTS `Users`;
DROP TABLE IF EXISTS `RefreshTokens`;
DROP TABLE IF EXISTS `BlacklistedAccessTokens`;


-- 사용자 테이블
CREATE TABLE `Users` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '사용자 고유 ID',
    `UserName` VARCHAR(100) NOT NULL COMMENT '사용자 이름',
    `Password_Hash` VARCHAR(255) NOT NULL COMMENT 'BCrypt 해시된 비밀번호',
    `Email` VARCHAR(255) DEFAULT NULL COMMENT '사용자 이메일',
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT '생성 시간',
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uq_Users_UserName` (`UserName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='사용자 테이블';

-- 리플레시 토큰 테이블
CREATE TABLE `RefreshTokens` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT COMMENT 'Primary key',
  `UserId` BIGINT NOT NULL COMMENT 'Reference to Users.Id',
  `Token` VARCHAR(512) NOT NULL COMMENT 'Refresh token value (base64)',
  `DeviceId` VARCHAR(256) NULL COMMENT 'Device identifier (client-provided)',
  `ExpiresAt` DATETIME NOT NULL COMMENT 'UTC expiration timestamp',
  `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT 'Creation timestamp (UTC)',
  `RevokedAt` DATETIME NULL COMMENT 'When token was revoked (NULL if active)',
  `ReplacedByToken` VARCHAR(512) NULL COMMENT 'If rotated, the new token string',
  PRIMARY KEY (`Id`),
  INDEX `IX_RefreshTokens_UserId` (`UserId`),
  INDEX `IX_RefreshTokens_UserId_DeviceId` (`UserId`, `DeviceId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Refresh tokens for JWT rotation';


-- 블랙리스트에 등록된 액세스 토큰 정보 저장
CREATE TABLE `BlacklistedAccessTokens` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '토큰 ID',
    `Jti` VARCHAR(255) NOT NULL COMMENT 'JWT 고유 식별자 (JTI)',
    `ExpiresAt` DATETIME NOT NULL COMMENT '토큰 만료 일시 (UTC)',
    `CreatedAt` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '토큰 블랙리스트 등록 일시 (UTC)',
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UQ_BlacklistedAccessTokens_Jti` (`Jti`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='블랙리스트에 등록된 액세스 토큰 엔티티';

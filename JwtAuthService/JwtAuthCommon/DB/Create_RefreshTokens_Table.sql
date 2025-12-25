DROP TABLE IF EXISTS `Users`;
DROP TABLE IF EXISTS `RefreshTokens`;
DROP TABLE IF EXISTS `BlacklistedAccessTokens`;


-- 사용자 테이블
CREATE TABLE `Users` (
    `Id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '사용자 고유 ID',
    `UserName` VARCHAR(100) NOT NULL COMMENT '사용자 이름',
    `Password_Hash` VARCHAR(255) NOT NULL COMMENT 'BCrypt 해시된 비밀번호',
    `Email` VARCHAR(255) DEFAULT NULL COMMENT '사용자 이메일',
    `Role` VARCHAR(50) NOT NULL COMMENT '사용자 역할',
    `IsActive` BIT(1) NOT NULL COMMENT '계정 활성화 여부(true/false : 유/무)', 
    `CreatedAt` DATETIME DEFAULT CURRENT_TIMESTAMP COMMENT '생성 시간',
    `LastLoginAt` DATETIME DEFAULT NULL COMMENT '마지막 로그인 시간',
    `IsActiveChangedAt` DATETIME DEFAULT NULL COMMENT '계정 활성화 상태 변경 시간',
    PRIMARY KEY (`Id`),
    UNIQUE KEY `uq_Users_UserName` (`UserName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='사용자 테이블';

-- 관리자는 1 ~ 100, 일반 사용자는 101부터 시작
ALTER TABLE `Users` AUTO_INCREMENT = 101;


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


-- Password : Pass123!
INSERT INTO `Users` (`Id`, `UserName`, `Password_Hash`, `Email`, `Role`, `IsActive`)
VALUES
(1, 'admin01', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'admin01@example.com', 'Admin', true);

INSERT INTO `Users` (`Id`, `UserName`, `Password_Hash`, `Email`, `Role`, `IsActive`)
VALUES
(101, 'user01', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user01@example.com', 'User', true),
(102, 'user02', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user02@example.com', 'User', true),
(103, 'user03', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user03@example.com', 'User', true),
(104, 'user04', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user04@example.com', 'User', true),
(105, 'user05', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user05@example.com', 'User', true),
(106, 'user06', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user06@example.com', 'User', true),
(107, 'user07', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user07@example.com', 'User', true),
(108, 'user08', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user08@example.com', 'User', true),
(109, 'user09', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user09@example.com', 'User', true),
(110, 'user10', '$2a$11$8dI6GKmjwyKC5lJ/wdDTE.ANZaVtIlX54/n1TI5gHjTKpgIX8HYCe', 'user10@example.com', 'User', true);

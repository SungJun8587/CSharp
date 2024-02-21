DROP TABLE IF EXISTS `Account`;

CREATE TABLE `Account` (
  `UserNo` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '유저 계정 번호',
  `DeviceID` varchar(100) NOT NULL COMMENT '기기 ID',
  `IsDeleted` bit(1) NOT NULL COMMENT '삭제 유무(1/0 : 삭제/정상)',
  `InsertDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '가입 일시',
  `DeleteDate` datetime DEFAULT NULL COMMENT '삭제 일시',
  PRIMARY KEY (`UserNo`),
  KEY `idx_Account_UserNo_IsDeleted` (`UserNo`,`IsDeleted`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='유저 기본 정보';


DROP TABLE IF EXISTS `Log_User_Register`;

CREATE TABLE `Log_User_Register` (
   `InsertDate` int unsigned NOT NULL COMMENT '가입 일',
   `UserNo` bigint unsigned NOT NULL COMMENT '유저 계정번호',
   `No` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '일련번호',
   `FpID` varchar(100) DEFAULT NULL COMMENT '고유 유저키값',
   `DeviceID` varchar(100) NOT NULL COMMENT '기기 ID',
   `InsertDateTime` datetime NOT NULL COMMENT '가입 일시',
   PRIMARY KEY (`InsertDate`,`UserNo`,`No`),
   KEY `ix_NC_Log_User_Register_UserNo` (`UserNo`),
   KEY `ix_NC_Log_User_Register_No` (`No`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='유저 가입 로그';


DROP TABLE IF EXISTS `Log_User_Login`;

CREATE TABLE `Log_User_Login` (
   `InsertDate` int unsigned NOT NULL COMMENT '로그인 일',
   `UserNo` bigint unsigned NOT NULL COMMENT '유저 계정번호',
   `No` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '일련번호',
   `FpID` varchar(100) DEFAULT NULL COMMENT '고유 유저키값',
   `DeviceID` varchar(100) NOT NULL COMMENT '기기 ID',
   `InsertDateTime` datetime NOT NULL COMMENT '로그인 일시',
   PRIMARY KEY (`InsertDate`,`UserNo`,`No`),
   KEY `ix_NC_Log_User_Login_UserNo` (`UserNo`),
   KEY `ix_NC_Log_User_Login_No` (`No`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='유저 로그인 로그';


DROP TABLE IF EXISTS `Log_GameHubError`;

CREATE TABLE `Log_GameHubError` (
   `No` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '일련번호',
   `PlayerNo` bigint unsigned NOT NULL COMMENT '플레이어 번호',
   `Source` text COMMENT '오류를 발생시키는 응용 프로그램 또는 개체의 이름',
   `Message` text COMMENT '예외를 설명하는 메시지',
   `StackTrace` text COMMENT '호출 스택의 직접 실행 프레임 문자열 표현',
   `ServerIP` varchar(20) DEFAULT NULL COMMENT '서버 IP',
   `UserIP` varchar(20) DEFAULT NULL COMMENT '접속한 유저 IP',
   `ConfirmAdminIdx` int unsigned DEFAULT '0' COMMENT '확인한 관리자 일련번호',
   `InsertDate` datetime NOT NULL COMMENT '등록 일시',
   PRIMARY KEY (`No`),
   KEY `ix_Log_GameHubError_PlayerNo` (`PlayerNo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='게임허브 에러 로그';


DROP TABLE IF EXISTS `Players`;

CREATE TABLE `Players` (
  `PlayerNo` bigint unsigned NOT NULL COMMENT '플레이어 번호',
  `UserNo` bigint unsigned NOT NULL COMMENT '유저 계정 번호',
  `Name` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci DEFAULT NULL COMMENT '닉네임',
  `Icon` int unsigned NOT NULL COMMENT '프로필 아이콘',
  `InsertDate` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '등록 일시(플레이어 생성 일시)',
  PRIMARY KEY (`PlayerNo`),
  UNIQUE KEY `ix_U_Players_Name` (`Name`), 
  KEY `ix_U_Players_UserNo` (`UserNo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='플레이어 기본 정보';


DROP TABLE IF EXISTS `Log_Player_Register`;

CREATE TABLE `Log_Player_Register` (
   `InsertDate` int unsigned NOT NULL COMMENT '가입 일',
   `PlayerNo` bigint unsigned NOT NULL COMMENT '플레이어 번호',
   `No` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '일련번호',
   `UserNo` bigint unsigned NOT NULL COMMENT '유저 계정번호',
   `InsertDateTime` datetime NOT NULL COMMENT '등록 일시',
   PRIMARY KEY (`InsertDate`,`PlayerNo`,`No`),
   KEY `ix_NC_Log_Player_Register_PlayerNo` (`PlayerNo`),
   KEY `ix_NC_Log_Player_Register_No` (`No`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='플레이어 가입 로그';


DROP TABLE IF EXISTS `Log_Player_Login`;

CREATE TABLE `Log_Player_Login` (
   `InsertDate` int unsigned NOT NULL COMMENT '로그인 일',
   `PlayerNo` bigint unsigned NOT NULL COMMENT '플레이어 번호',
   `No` bigint unsigned NOT NULL AUTO_INCREMENT COMMENT '일련번호',
   `UserNo` bigint unsigned NOT NULL COMMENT '유저 계정번호',
   `Name` varchar(50) NOT NULL COMMENT '닉네임',
   `InsertDateTime` datetime NOT NULL COMMENT '등록 일시',
   PRIMARY KEY (`InsertDate`,`PlayerNo`,`No`),
   KEY `ix_NC_Log_Player_Login_PlayerNo` (`PlayerNo`),
   KEY `ix_NC_Log_Player_Login_No` (`No`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci COMMENT='플레이어 로그인 로그';




using Common.Lib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Protocol;

namespace Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private readonly GlobalWriteDBContext _globalWriteDB;
        private readonly GlobalReadDBContext _globalReadDB;
        private readonly ILoggerService _logger;

        public UserController(
            GlobalWriteDBContext globalWriteDB,
            GlobalReadDBContext globalReadDB,
            ILoggerService logger
        )
        {
            _globalWriteDB = globalWriteDB;
            _globalReadDB = globalReadDB;
            _logger = logger;
        }

        [Route("GlobalJoin")]   // 유저 글로벌 가입
        [HttpPost]
        public async Task<AckGlobalJoin> GlobalJoin([FromBody] ReqGlobalJoin reqGlobalJoin)
        {
            AckGlobalJoin ackGlobalJoin = new AckGlobalJoin();
            ulong userNo = 0;
            string fpID = string.Empty;
            DateTime joinDateTime;
            DateTime nowDate = SgTime.I.NowDateTime;
            DateTime zeroDate = Convert.ToDateTime("0001-01-01 00:00:00");

            Account account = null;

            // string이 비면 그냥 넘어가서 회원가입을 진행한다
            if (String.IsNullOrEmpty(reqGlobalJoin.Id) == false)
            {
                // reqGlobalJoin.Id가 fpid로써 유효한지 선체크하고 난 뒤에 쿼리 실행
                if (SecurityUtility.GetUserNo(reqGlobalJoin.Id, out userNo) == false)
                {
                    // 유효하지 않은 fpid면 에러 발생
                    ackGlobalJoin.RetCode = ERROR_CODE_SPEC.NotAvailableGuid;
                    ackGlobalJoin.RetMessage = ERROR_CODE_SPEC.NotAvailableGuid.ToString();
                    return ackGlobalJoin;
                }

                // 유효한 fpid면 얻은 userNo 쿼리 실행
                account = await _globalReadDB.Account.Where(p => p.UserNo == userNo && p.IsDeleted == false).SingleOrDefaultAsync();
            }

            // 2. 기존 유저(소셜 연동 유저 + 게스트 유저)이면 유저 UUID를 리턴
            if (account != null)
            {
                ackGlobalJoin.FpID = SecurityUtility.GetFpID(account.UserNo);
                ackGlobalJoin.IsFirstJoin = false;
                ackGlobalJoin.JoinDateTime = account.InsertDate;
                ackGlobalJoin.RetCode = ERROR_CODE_SPEC.Success;
                ackGlobalJoin.RetMessage = ERROR_CODE_SPEC.Success.ToString();
                return ackGlobalJoin;
            }

            // 3. 기존 유저가 아니면 신규로 생성
            // 3.1. 유저 기본 정보 생성
            account = new Account
            {
                DeviceID = reqGlobalJoin.DeviceID,
                IsDeleted = false,
                InsertDate = nowDate,
                DeleteDate = zeroDate
            };
            _globalWriteDB.Account.Add(account);

            try
            {
                // UserNo, FpID 발생을 위한 처리
                await _globalWriteDB.SaveChangesAsync();
            }
            catch
            {
                ackGlobalJoin.RetCode = ERROR_CODE_SPEC.DB_Error;
                ackGlobalJoin.RetMessage = ERROR_CODE_SPEC.DB_Error.ToString();
                return ackGlobalJoin;
            }

            userNo = account.UserNo;
            fpID = SecurityUtility.GetFpID(userNo);
            joinDateTime = account.InsertDate;

            await _globalWriteDB.SaveChangesAsync();

            // 3.3. 최초 가입일 경우 가입 로그 기록
            LogContainer logContainer = new LogContainer();
            logContainer.globalLogs.Add(new Log_User_Register
            {
                InsertDate = Convert.ToUInt32(SgTime.I.NowDateTime.ToString("yyyyMMdd")),
                UserNo = userNo,
                FpID = fpID,
                DeviceID = reqGlobalJoin.DeviceID,
                InsertDateTime = nowDate
            });
            _logger.Add(logContainer);

            ackGlobalJoin.FpID = fpID;
            ackGlobalJoin.IsFirstJoin = true;
            ackGlobalJoin.JoinDateTime = joinDateTime;
            ackGlobalJoin.RetCode = ERROR_CODE_SPEC.Success;
            ackGlobalJoin.RetMessage = ERROR_CODE_SPEC.Success.ToString();

            return ackGlobalJoin;
        }

        [Route("GlobalLogin")]  // 유저 글로벌 로그인
        [HttpPost]
        public async Task<AckGlobalLogin> GlobalLogin([FromBody] ReqGlobalLogin reqGlobalLogin)
        {
            AckGlobalLogin ackGlobalLogin = new AckGlobalLogin();

            byte state = 0;
            DateTime curDateTime = SgTime.I.NowDateTime;

            // 1. fpID 유효성 체크
            if (SecurityUtility.GetUserNo(reqGlobalLogin.FpID, out ulong userNo) == false)
            {
                ackGlobalLogin.RetCode = ERROR_CODE_SPEC.NotAvailableGuid;
                ackGlobalLogin.RetMessage = ERROR_CODE_SPEC.NotAvailableGuid.ToString();
                return ackGlobalLogin;
            }

            // 2. 해당 유저가 게임에 가입했는지 체크(유저 UUID 존재 유무)
            Account account = await _globalReadDB.Account.Where(p => p.UserNo == userNo && p.IsDeleted == false).SingleOrDefaultAsync();
            if (account == null)
            {
                ackGlobalLogin.RetCode = ERROR_CODE_SPEC.NonExistsUser;
                ackGlobalLogin.RetMessage = ERROR_CODE_SPEC.NonExistsUser.ToString();
                return ackGlobalLogin;
            }

            // 3. 정상 접속일 경우에만 Global 로그인 로그 기록
            if (state == 0 && account != null)
            {
                LogContainer logContainer = new LogContainer();
                logContainer.globalLogs.Add(new Log_User_Login
                {
                    InsertDate = Convert.ToUInt32(SgTime.I.NowDateTime.ToString("yyyyMMdd")),
                    UserNo = account.UserNo,
                    FpID = reqGlobalLogin.FpID,
                    DeviceID = reqGlobalLogin.DeviceID,
                    InsertDateTime = SgTime.I.NowDateTime
                });
                _logger.Add(logContainer);
            }

            ackGlobalLogin.State = state;    // 상태값(0/1/2/3 : 진입가능/점검/제재유저/탈퇴유저)
            ackGlobalLogin.Account = new PAccount
            {
                UserNo = account.UserNo,
                FpID = SecurityUtility.GetFpID(account.UserNo),
                DeviceID = account.DeviceID,
                IsDeleted = account.IsDeleted
            };

            return ackGlobalLogin;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using MySqlConnector;

namespace FcmPushAgent
{
    /// <summary>
    /// FCM에서 Unregistered/InvalidArgument로 응답된 토큰을 DB에서 정리(무효화)합니다.
    /// 다음 발송 시 동일 토큰으로 재발송 시도를 막아 불필요한 호출을 줄입니다.
    /// 모든 DB 접근은 Dapper를 통해 이루어집니다.
    /// </summary>
    public class TokenCleanupRepository
    {
        // MySQL 연결 문자열. 호출마다 새 연결을 열고 닫는 단순한 구조(연결 풀은 MySqlConnector가 내부적으로 관리).
        private readonly string _connectionString;

        /// <param name="connectionString">MySQL 연결 문자열</param>
        public TokenCleanupRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// 전달된 토큰들의 push_token을 NULL로 설정하여 다음 발송 대상에서 제외시킵니다.
        /// FCM이 더 이상 유효하지 않다고 응답한 토큰(앱 삭제, 토큰 만료 등)을 정리할 때 호출합니다.
        /// users.updated_at은 ON UPDATE CURRENT_TIMESTAMP에 의해 자동으로 갱신됩니다.
        /// </summary>
        /// <param name="tokens">무효화할 FCM 토큰 목록</param>
        /// <param name="ct">취소 토큰</param>
        public async Task InvalidateTokensAsync(IEnumerable<string> tokens, CancellationToken ct)
        {
            var tokenList = tokens.ToList();
            // 무효화할 토큰이 없으면 쿼리를 실행할 필요가 없으므로 바로 반환 (불필요한 DB 왕복 방지)
            if (tokenList.Count == 0)
                return;

            using var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Dapper의 IN 절 자동 전개: List<string>을 @Tokens에 넘기면
            // IN (@Tokens)를 IN (?,?,?...) 형태로 자동으로 펼쳐줍니다.
            // 기존의 수동 파라미터 생성(@t0, @t1, ...) 코드가 필요 없어집니다.
            await conn.ExecuteAsync(
                new CommandDefinition(
                    @"UPDATE users
                      SET push_token = NULL,
                          push_token_invalidated_at = NOW()
                      WHERE push_token IN @Tokens",
                    new { Tokens = tokenList },
                    cancellationToken: ct));
        }
    }
}

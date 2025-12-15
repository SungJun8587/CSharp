using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Json.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;

namespace JwtAuthService.Json.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProtectedController : ControllerBase
    {
        /// <summary>
        /// 테스트용 보호된 엔드포인트
        /// </summary>
        /// <returns>인증 성공 메시지</returns>
        [Authorize]
        [HttpGet("test")]
        public IActionResult TestProtected()
        {
            return Ok(new { message = "You have accessed a protected endpoint!" });
        }
    }
}

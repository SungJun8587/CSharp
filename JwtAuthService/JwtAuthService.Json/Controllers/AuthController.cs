using JwtAuthCommon.Entities;
using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Json.Models;
using Microsoft.AspNetCore.Mvc;

namespace JwtAuthService.Json.Controllers
{
    /// <summary>
    /// 인증 관련 API 컨트롤러
    /// 회원가입, 로그인, 토큰 리프레시, 로그아웃 기능 제공
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _userRepo;
        private readonly IAuthService _authService;

        /// <summary>
        /// 생성자: 의존성 주입
        /// </summary>
        /// <param name="userRepo">사용자 저장소</param>
        /// <param name="authService">인증 서비스</param>
        public AuthController(IUserRepository userRepo, IAuthService authService)
        {
            _userRepo = userRepo;
            _authService = authService;
        }

        /// <summary>
        /// 사용자 회원가입
        /// </summary>
        /// <param name="request">회원가입 요청 DTO</param>
        /// <returns>회원가입 성공 메시지</returns>
        [HttpPost("register")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterRequest request)
        {
            // 1. 입력 유효성 체크
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new ResponseData()
                {
                    Success = false,
                    Message = "Username and password are required."
                });
            }

            // 2. 사용자 엔티티 생성
            var user = new UserEntity
            {
                Username = request.UserName,
                Email = request.Email,
                Password_Hash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Role = !string.IsNullOrEmpty(request.Role) ? request.Role : "User",
                IsActive = true
            };

            // 3. 비동기 DB 저장
            await _userRepo.AddAsync(user);

            // 4. 성공 응답 반환
            return Ok(new ResponseData() 
            { 
                Success = true,
                Message = "User registered successfully." 
            });
        }

        /// <summary>
        /// 사용자 로그인
        /// </summary>
        /// <param name="request">로그인 요청 DTO</param>
        /// <returns>액세스 토큰, 리프레시 토큰, 만료시간</returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // 1. 로그인 시도
            var (accessToken, refreshToken, expiresIn) = await _authService.LoginAsync(request.UserName, request.Password, request.DeviceId);

            // 2. 인증 실패 시
            if (accessToken == null || refreshToken == null)
            {
                return Unauthorized(new LoginResponse() 
                {
                    Success = false,
                    Message = "Invalid credentials" 
                });
            }

            // 3. 인증 성공 시 토큰 반환
            return Ok(new LoginResponse
            { 
                Success = true,
                Message = "User Login successfully",
                Token = new TokenResponse()
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                }
            });
        }

        /// <summary>
        /// 리프레시 토큰으로 액세스 토큰 갱신
        /// </summary>
        /// <param name="request">리프레시 요청 DTO</param>
        /// <returns>새 액세스 토큰 및 리프레시 토큰</returns>
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            // 1. 리프레시 토큰 갱신
            var (accessToken, refreshToken) = await _authService.RefreshAsync(request.RefreshToken, request.DeviceId);

            // 2. 갱신 실패 시
            if (accessToken == null || refreshToken == null)
            {
                return Unauthorized(new RefreshResponse()
                {
                    Success = false,
                    Message = "invalid_or_expired"
                });
            }

            // 3. 갱신 성공 시 새 토큰 반환
            return Ok(new RefreshResponse()
            {
                Success = true,
                Message = "Token refresh successfully",
                Token = new TokenResponse()
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                }
            });
        }

        /// <summary>
        /// 로그아웃 처리(리프레시 토큰 무효화)
        /// </summary>
        /// <param name="request">로그아웃 요청 DTO</param>
        /// <returns>로그아웃 성공 메시지</returns>
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            // 1. 리프레시 토큰 무효화
            await _authService.LogoutAsync(request.RefreshToken, request.DeviceId);

            // 2. 로그아웃 성공 메시지 반환
            return Ok(new ResponseData
            {
                Success = true,
                Message = "Logged out"
            });
        }
    }
}

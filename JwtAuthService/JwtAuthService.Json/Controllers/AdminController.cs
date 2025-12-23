using JwtAuthCommon.Entities;
using JwtAuthCommon.Repositories;
using JwtAuthCommon.Services;
using JwtAuthService.Json.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly IAuthService _authService;

    /// <summary>
    /// 생성자: 의존성 주입
    /// </summary>
    /// <param name="userRepo">사용자 저장소</param>
    /// <param name="authService">인증 서비스</param>
    public AdminController(IUserRepository userRepo, IAuthService authService)
    {
        _userRepo = userRepo;
        _authService = authService;
    }

    /// <summary>
    /// 모든 유저 목록 조회
    /// </summary>
    [HttpGet("all")]
    public async Task<IActionResult> All()
    {
        var users = await _userRepo.GetAllAsync();
        return Ok(users);
    }

    /// <summary>
    /// 계정 활성화된 관리자 목록 조회
    /// </summary>
    [HttpGet("admins")]
    public async Task<IActionResult> GetAdmins()
    {
        var admins = await _userRepo.GetAdminsAsync();
        return Ok(admins);
    }

    /// <summary>
    /// 계정 활성화된 유저 목록 조회
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _userRepo.GetUsersAsync();
        return Ok(users);
    }

    /// <summary>
    /// 관리자 추가
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAdmin([FromBody] RegisterRequest request)
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
            Role = "Admin",
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
    /// 유저 계정 비활성화(삭제)
    /// </summary>
    [HttpDelete("user/{id:long}")]
    public async Task<IActionResult> DeleteUser(long id)
    {
        await _userRepo.DeleteUserAsync(id);
        return Ok(new ResponseData()
        {
            Success = true,
            Message = "User deleted successfully."
        });
    }

    /// <summary>
    /// 비활성화(삭제)된 유저 활성화
    /// </summary>
    [HttpPatch("user/{id:long}")]
    public async Task<IActionResult> ActiveUser(long id)
    {
        await _userRepo.ActiveUserAsync(id);
        return Ok(new ResponseData()
        {
            Success = true,
            Message = "User actived successfully."
        });
    }
}

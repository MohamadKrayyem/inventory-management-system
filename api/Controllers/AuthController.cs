using System.Security.Claims;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(Db db, IConfiguration config) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        using var conn = db.Connect();
        var user = await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Email = @Email", new { req.Email });

        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = DateTime.UtcNow.AddHours(8),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["name"] = user.FullName,
                ["email"] = user.Email,
                ["role"] = user.Role
            }
        });

        return Ok(new LoginResponse(token, ToDto(user)));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        using var conn = db.Connect();
        var user = await conn.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE Id = @Id", new { Id = int.Parse(User.FindFirstValue("sub")!) });

        return user == null ? NotFound(new { message = "User not found." }) : Ok(ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.FullName, u.Email, u.Role);
}

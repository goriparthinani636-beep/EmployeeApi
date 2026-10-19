
using EmployeeApi.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;

    public AuthController(AppDbContext context)
    {
        _context = context;
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                message = "Email and password are required"
            });
        }

        var email = request.Email.Trim().ToLower();

        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == email);

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password"
            });
        }

        // Verify Identity password hashes; migrate legacy plain-text values after
        // a successful login so existing accounts continue to work safely.
        var passwordHasher = new PasswordHasher<EmployeeApi.Models.User>();
        PasswordVerificationResult verification;
        try
        {
            verification = passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, request.Password);
        }
        catch (FormatException)
        {
            if (!string.Equals(user.PasswordHash, request.Password, StringComparison.Ordinal))
            {
                return Unauthorized(new
                {
                    message = "Invalid email or password"
                });
            }

            verification = PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password"
            });
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        if (!user.IsActive)
        {
            return Unauthorized(new
            {
                message = "User account is inactive"
            });
        }

        return Ok(new
        {
            accessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.dummy_access_token",
            refreshToken = "dummy_refresh_token_789xyz",
            expiresIn = 900,
            user = new
            {
                id = user.Id,
                name = $"{user.FirstName} {user.LastName}",
                role = user.Role
            }
        });
    }


    [HttpGet("test")]
    public IActionResult Test() => Ok(new { message = "EmployeeApi is reachable" });
}

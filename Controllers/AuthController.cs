
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext context,
        IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        _passwordHasher = new PasswordHasher<User>();
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = string.Empty;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                message = "Email and password are required."
            });
        }

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == email);

        if (user == null || !user.IsActive)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        if (request.Role != user.Role || user.Role is not ("Manager" or "Employee") || (user.Role == "Employee" && user.EmployeeId == null))
            return Unauthorized(new { message = "Invalid email, password, or login type." });

        var storedPassword = user.PasswordHash ?? string.Empty;
        PasswordVerificationResult verification;

        if (storedPassword.StartsWith(
                "AQAAAA", StringComparison.Ordinal))
        {
            try
            {
                verification = _passwordHasher.VerifyHashedPassword(
                    user, storedPassword, request.Password);
            }
            catch (FormatException)
            {
                return Unauthorized(new
                {
                    message = "Stored password format is invalid."
                });
            }
            catch (ArgumentException)
            {
                return Unauthorized(new
                {
                    message = "Stored password format is invalid."
                });
            }
        }
        else
        {
            verification = string.Equals(
                storedPassword,
                request.Password,
                StringComparison.Ordinal)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password."
            });
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(
                user, request.Password);

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        var accessToken = CreateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        // Requires RefreshToken and RefreshTokenExpiry properties
        // on User.cs, plus a database migration.
        user.RefreshTokenHash = HashRefreshToken(refreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Login successful",
            accessToken,
            refreshToken,
            expiresIn = 900,
            user = new
            {
                id = user.Id,
                name = $"{user.FirstName} {user.LastName}".Trim(),
                role = user.Role,
                email = user.Email,
                employeeId = user.EmployeeId
            }
        });
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Unauthorized(new { message = "Refresh token is required." });
        }

        var refreshHash = HashRefreshToken(request.RefreshToken);

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.RefreshTokenHash == refreshHash);

        if (user == null ||
            !user.IsActive ||
            user.RefreshTokenExpiry == null ||
            user.RefreshTokenExpiry <= DateTime.UtcNow)
        {
            return Unauthorized(new
            {
                message = "Refresh token is invalid or expired. Please log in again."
            });
        }

        if (user.Role is not ("Manager" or "Employee") || (user.Role == "Employee" && user.EmployeeId == null))
            return Unauthorized();

        var newAccessToken = CreateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();

        // Rotate refresh token so the old token cannot be reused.
        user.RefreshTokenHash = HashRefreshToken(newRefreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(30);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken,
            expiresIn = 900,
            user = new
            {
                id = user.Id,
                name = $"{user.FirstName} {user.LastName}".Trim(),
                role = user.Role,
                email = user.Email,
                employeeId = user.EmployeeId
            }
        });
    }

    // POST /api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userId, out var id))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == id);

        if (user != null)
        {
            user.SecurityVersion++;
            user.UpdatedAt = DateTime.UtcNow;
            user.RefreshTokenHash = null;
            user.RefreshTokenExpiry = null;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Logged out successfully." });
    }

    // GET /api/auth/me
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = User.FindFirst(
            ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userId, out var id))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity in token."
            });
        }

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Id == id);

        if (user == null || !user.IsActive)
        {
            return Unauthorized(new
            {
                message = "User account is unavailable."
            });
        }

        return Ok(new
        {
            user = new
            {
                id = user.Id,
                name = $"{user.FirstName} {user.LastName}".Trim(),
                role = user.Role,
                email = user.Email,
                employeeId = user.EmployeeId
            }
        });
    }

    // GET /api/auth/test
    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

    [Authorize(Roles = "Manager,Employee")]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        if (request.NewPassword.Length < 6 || request.NewPassword == request.CurrentPassword)
            return BadRequest(new { message = "Use a different new password with at least 6 characters." });
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == User.UserId() && u.IsActive);
        if (user == null) return Unauthorized();
        PasswordVerificationResult result;
        try { result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword); }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        { result = PasswordVerificationResult.Failed; }
        if (result == PasswordVerificationResult.Failed)
            return BadRequest(new { message = "Current password is incorrect. Sign in once before changing a legacy password." });
        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityVersion++;
        user.RefreshTokenHash = null;
        user.RefreshTokenExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Password changed. Please sign in again." });
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new { message = "EmployeeApi is reachable", apiVersion = "employee-access-v2" });
    }

    private string CreateAccessToken(User user)
    {
        var jwtKey = _configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(jwtKey) ||
            Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException(
                "JWT key must be configured and at least 32 bytes long.");
        }

        var issuer = _configuration["Jwt:Issuer"] ?? "EmployeeApi";
        var audience = _configuration["Jwt:Audience"] ?? "EmployeeApp";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new("employeeId", user.EmployeeId?.ToString() ?? ""),
            new("sv", user.SecurityVersion.ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64));
    }

    private static string HashRefreshToken(string token)
    {
        return Convert.ToBase64String(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

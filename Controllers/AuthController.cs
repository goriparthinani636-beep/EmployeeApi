
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.Email.ToLower() == email);

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid email or password"
            });
        }

        if (!user.IsActive)
        {
            return Unauthorized(new
            {
                message = "User account is inactive"
            });
        }

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
                    message = "Stored password format is invalid. Reset the password."
                });
            }
            catch (ArgumentException)
            {
                return Unauthorized(new
                {
                    message = "Stored password format is invalid. Reset the password."
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
                message = "Invalid email or password"
            });
        }

        if (verification ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(
                user, request.Password);

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // Read JWT settings from configuration / Render environment.
        var jwtKey = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"] ?? "EmployeeApi";
        var audience = _configuration["Jwt:Audience"] ?? "EmployeeApp";

        if (string.IsNullOrWhiteSpace(jwtKey) ||
            Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            return StatusCode(500, new
            {
                message = "JWT configuration is missing or invalid."
            });
        }

        var securityKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtKey));

        var signingCredentials = new SigningCredentials(
            securityKey,
            SecurityAlgorithms.HmacSha256);

        var userName = $"{user.FirstName} {user.LastName}".Trim();
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: signingCredentials);

        var accessToken = new JwtSecurityTokenHandler()
            .WriteToken(token);

        return Ok(new
        {
            message = "Login successful",
            accessToken = accessToken,
            refreshToken = (string?)null,
            expiresIn = 900,
            user = new
            {
                id = user.Id,
                name = userName,
                role = user.Role
            }
        });
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new
        {
            message = "EmployeeApi is reachable"
        });
    }
}


using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PasswordHasher<User> _passwordHasher;

    public AuthController(AppDbContext context)
    {
        _context = context;
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

        // Check whether the stored value is an ASP.NET Identity hash.
        if (storedPassword.StartsWith(
                "AQAAAA", StringComparison.Ordinal))
        {
            try
            {
                verification = _passwordHasher.VerifyHashedPassword(
                    user,
                    storedPassword,
                    request.Password);
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
            // Legacy plain-text password compatibility.
            // Replace these legacy values with proper hashes after login.
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
                user,
                request.Password);

            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        // NOTE:
        // This endpoint does not issue a real JWT yet.
        // Do not use a dummy token to authorize protected API endpoints.

        return Ok(new
        {
            message = "Login successful",
            accessToken = (string?)null,
            refreshToken = (string?)null,
            expiresIn = 900,
            user = new
            {
                id = user.Id,
                name = $"{user.FirstName} {user.LastName}".Trim(),
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

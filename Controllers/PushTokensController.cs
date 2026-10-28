using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using EmployeeApi.Data;
using EmployeeApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/push-tokens")]
[Authorize(Roles = "Manager,Employee")]
public class PushTokensController(IDevicePushRegistry devices) : ControllerBase
{
    public class PushTokenRequest
    {
        [Required, MaxLength(256)] public string PushToken { get; set; } = "";
        [Required, MaxLength(16)] public string Platform { get; set; } = "android";
    }
    public static bool ValidToken(string token) => token.Length <= 256 &&
        Regex.IsMatch(token, @"^(ExpoPushToken|ExponentPushToken)\[[A-Za-z0-9_-]+\]$", RegexOptions.CultureInvariant);

    [HttpPost]
    public async Task<IActionResult> Register(PushTokenRequest request)
    {
        var userId = User.UserId();
        if (userId == null || !int.TryParse(User.FindFirst("sv")?.Value, out var version)) return Unauthorized();
        var token = request.PushToken.Trim();
        var platform = request.Platform.Trim().ToLowerInvariant();
        if (!ValidToken(token)) return BadRequest(new { message = "A valid Expo push token is required." });
        if (platform is not ("android" or "ios")) return BadRequest(new { message = "Platform must be android or ios." });
        if (!await devices.Register(userId.Value, version, token, platform)) return Unauthorized();
        return Ok(new { message = "Notification device registered." });
    }
    [HttpDelete]
    public async Task<IActionResult> Unregister(PushTokenRequest request)
    {
        var userId = User.UserId();
        if (userId == null) return Unauthorized();
        await devices.Unregister(userId.Value, request.PushToken.Trim());
        return NoContent();
    }
}

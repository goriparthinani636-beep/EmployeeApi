using System.Net.Http.Headers;
using System.Net.Http.Json;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public sealed record WhatsAppDeliveryResult(string Status, string? Warning = null);

public interface IWhatsAppCloudApiService
{
    Task<WhatsAppDeliveryResult> NotifyTaskAssigned(int employeeId, string taskTitle, CancellationToken cancellationToken = default);
    Task<WhatsAppDeliveryResult> NotifyLeaveDecision(LeaveRequest leave, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional WhatsApp Cloud API notifications. Credentials remain on the API server;
/// no request is made unless the feature is enabled and the recipient opted in.
/// </summary>
public sealed class WhatsAppCloudApiService(
    AppDbContext db,
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<WhatsAppCloudApiService> logger) : IWhatsAppCloudApiService
{
    public async Task<WhatsAppDeliveryResult> NotifyTaskAssigned(
        int employeeId, string taskTitle, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.FirstName, e.Phone, e.WhatsAppOptIn })
            .SingleOrDefaultAsync(cancellationToken);
        if (employee is null || !employee.WhatsAppOptIn)
            return new("skipped");

        return await SendTemplate(
            employee.Phone,
            configuration["WhatsApp:TaskAssignedTemplateName"],
            [employee.FirstName, taskTitle],
            "task assignment",
            cancellationToken);
    }

    public Task<WhatsAppDeliveryResult> NotifyLeaveDecision(
        LeaveRequest leave, CancellationToken cancellationToken = default)
    {
        var employee = leave.Employee;
        if (employee is null || !employee.WhatsAppOptIn)
            return Task.FromResult(new WhatsAppDeliveryResult("skipped"));

        var dates = $"{leave.StartDate:yyyy-MM-dd} to {leave.EndDate:yyyy-MM-dd}";
        return SendTemplate(
            employee.Phone,
            configuration["WhatsApp:LeaveDecisionTemplateName"],
            [employee.FirstName, dates, leave.Status],
            "leave decision",
            cancellationToken);
    }

    private async Task<WhatsAppDeliveryResult> SendTemplate(
        string? rawPhone,
        string? templateName,
        string[] parameters,
        string notificationKind,
        CancellationToken cancellationToken)
    {
        var enabled = bool.TryParse(configuration["WhatsApp:Enabled"], out var isEnabled) && isEnabled;
        var accessToken = configuration["WhatsApp:AccessToken"];
        var phoneNumberId = configuration["WhatsApp:PhoneNumberId"];
        if (!enabled || string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(templateName))
            return new("skipped");

        var recipient = NormalizePhone(rawPhone, configuration["WhatsApp:DefaultCountryCode"] ?? "91");
        if (recipient is null)
        {
            logger.LogWarning("WhatsApp {NotificationKind} skipped because the opted-in employee phone is not a valid international number.", notificationKind);
            return new("failed", "WhatsApp notification could not be sent: check the employee phone number format.");
        }

        var apiVersion = configuration["WhatsApp:GraphApiVersion"] ?? "v27.0";
        var request = new
        {
            messaging_product = "whatsapp",
            to = recipient,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = configuration["WhatsApp:LanguageCode"] ?? "en_US" },
                components = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = parameters.Select(value => new { type = "text", text = value }).ToArray()
                    }
                }
            }
        };

        try
        {
            using var client = clients.CreateClient("WhatsAppCloudApi");
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(phoneNumberId)}/messages")
            {
                Content = JsonContent.Create(request)
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(message, cancellationToken);
            if (response.IsSuccessStatusCode)
                return new("sent");

            logger.LogWarning("WhatsApp {NotificationKind} was rejected with HTTP {StatusCode}.",
                notificationKind, (int)response.StatusCode);
            return new("failed", "WhatsApp notification could not be sent; the employee can still check the app.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never log the request body, access token, or recipient number.
            logger.LogWarning(exception, "WhatsApp {NotificationKind} could not be sent.", notificationKind);
            return new("failed", "WhatsApp notification could not be sent; the employee can still check the app.");
        }
    }

    private static string? NormalizePhone(string? value, string defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 10 && defaultCountryCode.All(char.IsDigit)) digits = defaultCountryCode + digits;
        else if (digits.Length == 11 && digits[0] == '0' && defaultCountryCode.All(char.IsDigit))
            digits = defaultCountryCode + digits[1..];
        return digits.Length is >= 8 and <= 15 ? digits : null;
    }
}

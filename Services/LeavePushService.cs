using System.Net.Http.Json;
using System.Text.Json;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public class LeavePushService(AppDbContext db, IHttpClientFactory clients,
    PushReceiptTracker receipts, ILogger<LeavePushService> logger)
{
    public async Task<string?> NotifyDecision(LeaveRequest leave)
    {
        const string warning = "Decision saved, but the notification could not be sent. The employee can see the status in Leave.";
        try
        {
            var devices = await db.DevicePushTokens.AsNoTracking()
                .Where(d => d.User!.EmployeeId == leave.EmployeeId && d.User.Role == "Employee" &&
                    d.User.IsActive && d.User.Employee!.Status.ToLower() == "active")
                .Select(d => new PushRecipient(d.Id, d.UserId, d.PushToken, d.UpdatedAt)).ToListAsync();
            if (devices.Count == 0) return "Decision saved. The employee has no notification-enabled device registered.";
            var accepted = 0;
            foreach (var batch in devices.Chunk(100))
            {
                // Leave reasons, dates and manager comments stay inside the authenticated API.
                var messages = batch.Select(d => new { to = d.PushToken, sound = "default", channelId = "tasks", ttl = 3600,
                    title = "Leave request updated", body = "Your manager reviewed your leave request. Open EmployeeApp to view the decision.",
                    data = new { type = "leave-decision", leaveId = leave.Id, employeeId = leave.EmployeeId, recipientUserId = d.UserId } });
                using var response = await clients.CreateClient("ExpoPush").PostAsJsonAsync("send", messages.ToArray());
                if (!response.IsSuccessStatusCode) continue;
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!document.RootElement.TryGetProperty("data", out var tickets) || tickets.ValueKind != JsonValueKind.Array) continue;
                var results = tickets.EnumerateArray().ToArray();
                for (var i = 0; i < Math.Min(results.Length, batch.Length); i++)
                {
                    var ticket = results[i];
                    if (ticket.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
                        ticket.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } ticketId)
                    {
                        accepted++;
                        receipts.Add(ticketId, batch[i]);
                    }
                    else if (TaskPushService.IsUnregistered(ticket)) await TaskPushService.RemoveInvalidDevice(db, batch[i]);
                }
            }
            return accepted == devices.Count ? null : warning;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Leave decision notification failed for request {LeaveId}.", leave.Id);
            return warning;
        }
    }
}

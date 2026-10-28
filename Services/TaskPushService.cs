using System.Net.Http.Json;
using System.Text.Json;
using EmployeeApi.Data;
using EmployeeApi.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public interface ITaskPushService
{
    Task<string?> NotifyAssignment(TaskItem task);
}

public record PushRecipient(int Id, int UserId, string PushToken, DateTime UpdatedAt);
public record PendingPushReceipt(string TicketId, PushRecipient Device, DateTime CreatedAt);

public class PushReceiptTracker
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingPushReceipt> pending = new();
    public void Add(string ticketId, PushRecipient device)
    {
        if (pending.Count < 10000) pending[ticketId] = new(ticketId, device, DateTime.UtcNow);
    }
    public PendingPushReceipt[] Due()
    {
        foreach (var entry in pending.Values.Where(p => p.CreatedAt < DateTime.UtcNow.AddHours(-24)))
            pending.TryRemove(entry.TicketId, out _);
        return pending.Values.Where(p => p.CreatedAt <= DateTime.UtcNow.AddMinutes(-15)).Take(100).ToArray();
    }
    public void Remove(string id) => pending.TryRemove(id, out _);
}

public class TaskPushService(AppDbContext db, IHttpClientFactory clients,
    PushReceiptTracker receipts, ILogger<TaskPushService> logger) : ITaskPushService
{
    private const string FailedWarning = "Task saved, but the notification could not be sent. The employee can still view the task in the app.";

    public async Task<string?> NotifyAssignment(TaskItem task)
    {
        try
        {
            var devices = await db.DevicePushTokens.AsNoTracking()
                .Where(d => d.User!.EmployeeId == task.EmployeeId && d.User.IsActive &&
                    d.User.Employee!.Status.ToLower() == "active")
                .Select(d => new PushRecipient(d.Id, d.UserId, d.PushToken, d.UpdatedAt)).ToListAsync();
            if (devices.Count == 0)
                return "Task saved. The employee has not registered a notification-enabled device yet.";
            var accepted = 0;
            foreach (var batch in devices.Chunk(100))
            {
                var messages = batch.Select(device => new {
                    to = device.PushToken, sound = "default", channelId = "tasks", ttl = 3600,
                    title = "New task assigned", body = "Open EmployeeApp to view your assigned task.",
                    data = new { type = "task-assigned", taskId = task.Id,
                        employeeId = task.EmployeeId, recipientUserId = device.UserId }
                }).ToArray();
                using var response = await clients.CreateClient("ExpoPush").PostAsJsonAsync("send", messages);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Push service rejected task {TaskId}: HTTP {Status}.", task.Id, (int)response.StatusCode);
                    continue;
                }
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
                    else if (IsUnregistered(ticket)) await RemoveInvalidDevice(db, batch[i]);
                    else logger.LogWarning("Push ticket failed for task {TaskId}.", task.Id);
                }
            }
            // Acceptance is not proof of delivery; receipts are checked separately.
            return accepted == devices.Count ? null : FailedWarning;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Notification failed after task {TaskId} was saved.", task.Id);
            return FailedWarning;
        }
    }

    internal static bool IsUnregistered(JsonElement result) =>
        result.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
        details.TryGetProperty("error", out var error) && error.GetString() == "DeviceNotRegistered";

    internal static async Task RemoveInvalidDevice(AppDbContext db, PushRecipient snapshot)
    {
        // A late receipt must not delete a device that has since changed owners.
        await db.DevicePushTokens.Where(d => d.Id == snapshot.Id && d.UserId == snapshot.UserId &&
            d.PushToken == snapshot.PushToken && d.UpdatedAt == snapshot.UpdatedAt).ExecuteDeleteAsync();
    }
}

public class PushReceiptWorker(IServiceScopeFactory scopes, IHttpClientFactory clients,
    PushReceiptTracker receipts, ILogger<PushReceiptWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var due = receipts.Due();
                if (due.Length == 0) continue;
                try
                {
                    using var response = await clients.CreateClient("ExpoPush").PostAsJsonAsync("getReceipts",
                        new { ids = due.Select(p => p.TicketId).ToArray() }, stoppingToken);
                    if (!response.IsSuccessStatusCode) continue;
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stoppingToken));
                    if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) continue;
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    foreach (var receipt in due)
                    {
                        if (!data.TryGetProperty(receipt.TicketId, out var result)) continue;
                        if (TaskPushService.IsUnregistered(result)) await TaskPushService.RemoveInvalidDevice(db, receipt.Device);
                        if (result.TryGetProperty("status", out var status) && status.GetString() == "error")
                            logger.LogWarning("Push delivery failed for a registered device.");
                        receipts.Remove(receipt.TicketId);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { logger.LogWarning(exception, "Push receipt check failed; pending receipts will be retried."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

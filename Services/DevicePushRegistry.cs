using EmployeeApi.Data;
using Microsoft.EntityFrameworkCore;

namespace EmployeeApi.Services;

public interface IDevicePushRegistry
{
    Task<bool> Register(int userId, int securityVersion, string token, string platform);
    Task Unregister(int userId, string token);
}

public class DevicePushRegistry(AppDbContext db) : IDevicePushRegistry
{
    public async Task<bool> Register(int userId, int securityVersion, string token, string platform)
    {
        // Serialize with logout/password changes. Do not rebind from an invalid session.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH owner AS (
                SELECT "Id" FROM "Users"
                WHERE "Id" = {userId} AND "IsActive" AND "SecurityVersion" = {securityVersion}
                FOR UPDATE
            )
            INSERT INTO "DevicePushTokens" ("UserId", "PushToken", "Platform", "UpdatedAt")
            SELECT "Id", {token}, {platform}, {DateTime.UtcNow} FROM owner
            ON CONFLICT ("PushToken") DO UPDATE
            SET "UserId" = EXCLUDED."UserId", "Platform" = EXCLUDED."Platform", "UpdatedAt" = EXCLUDED."UpdatedAt";
            """);
        return affected > 0;
    }

    public async Task Unregister(int userId, string token) =>
        await db.DevicePushTokens.Where(d => d.UserId == userId && d.PushToken == token).ExecuteDeleteAsync();
}

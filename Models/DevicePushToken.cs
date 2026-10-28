using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EmployeeApi.Models;

public class DevicePushToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(256)]
    public string PushToken { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string Platform { get; set; } = "android";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public User? User { get; set; }
}

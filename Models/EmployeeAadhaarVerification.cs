namespace EmployeeApi.Models;

// The full Aadhaar number is kept encrypted; the keyed last-four hash is used for comparison.
public class EmployeeAadhaarVerification
{
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string? EncryptedAadhaar { get; set; }
    public string? AadhaarNonce { get; set; }
    public string? AadhaarTag { get; set; }
    public string PreEnrolledLastFourHash { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int FailedAttempts { get; set; }
    public bool IsLocked { get; set; }
}

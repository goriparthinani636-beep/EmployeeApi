using System.Security.Cryptography;
using System.Text;

namespace EmployeeApi.Services;

public sealed class AadhaarNumberProtector(IConfiguration configuration)
{
    public AadhaarProtectedValue Protect(string aadhaarNumber)
    {
        var key = LoadKey(configuration);
        byte[]? lastFourKey = null;
        try
        {
            var clearBytes = Encoding.ASCII.GetBytes(aadhaarNumber);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[clearBytes.Length];
            var tag = new byte[16];
            try
            {
                using var aes = new AesGcm(key, tag.Length);
                aes.Encrypt(nonce, clearBytes, ciphertext, tag, Encoding.UTF8.GetBytes("employee-aadhaar-v1"));
            }
            finally { CryptographicOperations.ZeroMemory(clearBytes); }

            lastFourKey = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("EmployeeApi:Aadhaar:last-four:v1"));
            var lastFour = aadhaarNumber[^4..];
            var hashInput = Encoding.UTF8.GetBytes($"employee-last4:{lastFour}");
            var lastFourHash = Convert.ToHexString(HMACSHA256.HashData(lastFourKey, hashInput));

            return new AadhaarProtectedValue(
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                lastFourHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (lastFourKey != null) CryptographicOperations.ZeroMemory(lastFourKey);
        }
    }

    public bool MatchesLastFour(string lastFour, string expectedHash)
    {
        var key = LoadKey(configuration);
        byte[]? lastFourKey = null;
        try
        {
            lastFourKey = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("EmployeeApi:Aadhaar:last-four:v1"));
            var input = Encoding.UTF8.GetBytes($"employee-last4:{lastFour}");
            var supplied = HMACSHA256.HashData(lastFourKey, input);
            byte[] expected;
            try { expected = Convert.FromHexString(expectedHash); }
            catch (FormatException) { return false; }
            return expected.Length == supplied.Length && CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (lastFourKey != null) CryptographicOperations.ZeroMemory(lastFourKey);
        }
    }

    private static byte[] LoadKey(IConfiguration configuration)
    {
        var configured = configuration["Aadhaar:DataKey"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Aadhaar data key is not configured. Set Aadhaar__DataKey to a base64-encoded 32-byte secret.");
        byte[] key;
        try { key = Convert.FromBase64String(configured); }
        catch (FormatException) { throw new InvalidOperationException("Aadhaar data key must be valid base64."); }
        if (key.Length != 32)
            throw new InvalidOperationException("Aadhaar data key must decode to exactly 32 bytes.");
        return key;
    }
}

public sealed record AadhaarProtectedValue(
    string EncryptedAadhaar,
    string AadhaarNonce,
    string AadhaarTag,
    string LastFourHash);

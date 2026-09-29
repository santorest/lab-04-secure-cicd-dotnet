namespace TicketApi.Auth;

/// <summary>Settings from the "Jwt" configuration section. The signing key never lives in the repo.</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";

    /// <summary>Base64-encoded HMAC-SHA256 key, at least 32 bytes.</summary>
    public string SigningKey { get; set; } = "";

    public int LifetimeMinutes { get; set; } = 15;

    public byte[] KeyBytes() => Convert.FromBase64String(SigningKey);

    /// <summary>Fails startup on missing or weak settings instead of running with them.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required.");
        }

        byte[] key;
        try
        {
            key = KeyBytes();
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be base64.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes (256 bits).");
        }

        if (LifetimeMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException("Jwt:LifetimeMinutes must be between 1 and 60.");
        }
    }
}

namespace RemoteHealthWatch.ServiceDefaults.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "remote-health-watch-identity";
    public string Audience { get; set; } = "remote-health-watch";

    /// <summary>HMAC key shared by identity (signs) and every API (validates). At least 32 bytes.</summary>
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 7;
}

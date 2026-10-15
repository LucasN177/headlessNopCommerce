namespace Nop.Plugin.Misc.HeadlessApi.Models.Auth;

/// <summary>
/// Represents an issued API token
/// </summary>
public record TokenModel
{
    public string AccessToken { get; set; }

    public string TokenType { get; set; } = "Bearer";

    public DateTime ExpiresOnUtc { get; set; }

    public Guid CustomerGuid { get; set; }

    public bool IsRegistered { get; set; }
}

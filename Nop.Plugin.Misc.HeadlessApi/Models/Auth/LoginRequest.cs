namespace Nop.Plugin.Misc.HeadlessApi.Models.Auth;

/// <summary>
/// Represents a login request
/// </summary>
/// <remarks>
/// Depending on the customer settings ("Usernames enabled") either the username or the email is used
/// </remarks>
public record LoginRequest
{
    public string Email { get; set; }

    public string Username { get; set; }

    public string Password { get; set; }
}

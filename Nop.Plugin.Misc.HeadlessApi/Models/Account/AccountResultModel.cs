using Nop.Plugin.Misc.HeadlessApi.Models.Auth;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Account;

/// <summary>
/// Represents the result of an account action (account activation, email revalidation, password recovery)
/// </summary>
public record AccountResultModel
{
    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the token of the customer; only set if the customer has been logged in by the action, so the frontend has to use this token from now on
    /// </summary>
    public TokenModel Token { get; set; }
}

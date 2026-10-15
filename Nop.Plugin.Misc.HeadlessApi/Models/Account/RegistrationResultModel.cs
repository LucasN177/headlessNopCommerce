using Nop.Plugin.Misc.HeadlessApi.Models.Auth;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Account;

/// <summary>
/// Represents the result of a registration
/// </summary>
public record RegistrationResultModel
{
    /// <summary>
    /// Gets or sets the registration type of the store (Standard, EmailValidation, AdminApproval)
    /// </summary>
    public string RegistrationType { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the token of the registered customer; only set if the customer is logged in right away (registration type "Standard")
    /// </summary>
    public TokenModel Token { get; set; }
}

using Nop.Web.Models.Customer;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Account;

/// <summary>
/// Represents a request to set a new password from the password recovery link
/// </summary>
public record PasswordRecoveryConfirmRequest : PasswordRecoveryConfirmModel
{
    /// <summary>
    /// Gets or sets the token from the link
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// Gets or sets the customer email from the link (used by links of previous versions)
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// Gets or sets the customer GUID from the link
    /// </summary>
    public Guid Guid { get; set; }
}

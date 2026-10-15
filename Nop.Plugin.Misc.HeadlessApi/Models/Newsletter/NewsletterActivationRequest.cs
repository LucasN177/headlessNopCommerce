namespace Nop.Plugin.Misc.HeadlessApi.Models.Newsletter;

/// <summary>
/// Represents a request to confirm or cancel a newsletter subscription (values of the email link)
/// </summary>
public record NewsletterActivationRequest
{
    /// <summary>
    /// Gets or sets the subscription token (GUID) of the email link
    /// </summary>
    public Guid Token { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to activate (true) or to delete (false) the subscription
    /// </summary>
    public bool Active { get; set; }
}

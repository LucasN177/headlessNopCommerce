namespace Nop.Plugin.Misc.HeadlessApi.Models.Newsletter;

/// <summary>
/// Represents a request to subscribe to or unsubscribe from the newsletter
/// </summary>
public record NewsletterSubscribeRequest
{
    /// <summary>
    /// Gets or sets the email
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to subscribe (true) or unsubscribe (false)
    /// </summary>
    public bool Subscribe { get; set; } = true;
}

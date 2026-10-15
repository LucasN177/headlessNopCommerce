namespace Nop.Plugin.Misc.HeadlessApi.Models.Newsletter;

/// <summary>
/// Represents the result of a newsletter subscription request
/// </summary>
public record NewsletterSubscribeResultModel
{
    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }
}

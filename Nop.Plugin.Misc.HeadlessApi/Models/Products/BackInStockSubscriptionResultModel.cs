namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents the result of a back in stock subscription toggle
/// </summary>
public record BackInStockSubscriptionResultModel
{
    public int ProductId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the customer is subscribed now (false if the customer has been unsubscribed)
    /// </summary>
    public bool Subscribed { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }
}

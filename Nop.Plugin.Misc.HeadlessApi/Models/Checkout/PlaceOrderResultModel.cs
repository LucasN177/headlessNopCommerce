namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents the result of placing an order (or re-posting a payment)
/// </summary>
public record PlaceOrderResultModel
{
    public int OrderId { get; set; }

    public string CustomOrderNumber { get; set; }

    /// <summary>
    /// Gets or sets the URL of the payment provider the frontend has to redirect the customer to (redirection payment methods);
    /// after the payment the provider returns the customer to the storefront, which redirects to the frontend if configured
    /// </summary>
    public string PaymentRedirectUrl { get; set; }

    /// <summary>
    /// Gets or sets the URL of the frontend "checkout completed" page (if configured)
    /// </summary>
    public string CompletedUrl { get; set; }
}

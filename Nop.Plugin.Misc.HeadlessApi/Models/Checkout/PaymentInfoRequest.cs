namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents a request to enter the payment information
/// </summary>
/// <remarks>
/// The values are passed to the payment plugin like the fields of its storefront form
/// (e.g. "CardholderName", "CardNumber", "ExpireMonth", "ExpireYear", "CardCode" or "PurchaseOrderNumber")
/// </remarks>
public record PaymentInfoRequest
{
    public Dictionary<string, string> Values { get; set; } = new();
}

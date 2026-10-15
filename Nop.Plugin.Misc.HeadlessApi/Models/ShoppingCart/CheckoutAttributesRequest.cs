namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to save the checkout attributes of the shopping cart
/// </summary>
public record CheckoutAttributesRequest
{
    /// <summary>
    /// Gets or sets the selected checkout attributes, using the same keys as the storefront form, e.g.
    /// "checkout_attribute_1": "5" (value id of a dropdown/radio/color/image squares attribute), "checkout_attribute_2": "6,7" (checkboxes),
    /// "checkout_attribute_3": "text", "checkout_attribute_4_day": "31", "checkout_attribute_4_month": "12", "checkout_attribute_4_year": "2026" (date picker),
    /// "checkout_attribute_5": "download GUID" (file upload, see cart/checkout-attributes/{attributeId}/upload).
    /// Attributes not passed are removed
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();
}

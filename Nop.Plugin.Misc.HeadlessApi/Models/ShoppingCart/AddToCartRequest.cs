namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to add a product to the shopping cart or wishlist
/// </summary>
public record AddToCartRequest
{
    public int ProductId { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the selected product attributes and gift card fields, using the same keys as the storefront form, e.g.
    /// "product_attribute_5": "12" (value id), "product_attribute_6": "13,14" (checkboxes), "product_attribute_7": "text",
    /// "giftcard_1.RecipientName": "...", "giftcard_1.RecipientEmail": "...", "giftcard_1.SenderName": "...", "giftcard_1.Message": "..."
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>
    /// Gets or sets the price entered by the customer (only for products with "Customer enters price"), in the working currency
    /// </summary>
    public decimal? CustomerEnteredPrice { get; set; }

    /// <summary>
    /// Gets or sets the rental start date (only for rental products)
    /// </summary>
    public DateTime? RentalStartDate { get; set; }

    /// <summary>
    /// Gets or sets the rental end date (only for rental products)
    /// </summary>
    public DateTime? RentalEndDate { get; set; }
}

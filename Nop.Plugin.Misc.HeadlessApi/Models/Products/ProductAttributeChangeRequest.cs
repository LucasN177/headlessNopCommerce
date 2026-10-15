namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents a request to recalculate the product details after the customer changed the selected attributes
/// </summary>
public record ProductAttributeChangeRequest
{
    /// <summary>
    /// Gets or sets the selected product attributes, using the same keys as the storefront form (see AddToCartRequest.Attributes), e.g.
    /// "product_attribute_5": "12" (value id), "product_attribute_6": "13,14" (checkboxes), "product_attribute_7": "text"
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>
    /// Gets or sets the rental start date (only for rental products)
    /// </summary>
    public DateTime? RentalStartDate { get; set; }

    /// <summary>
    /// Gets or sets the rental end date (only for rental products)
    /// </summary>
    public DateTime? RentalEndDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to validate the attribute conditions (enabled/disabled attributes)
    /// </summary>
    public bool ValidateAttributeConditions { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to load the picture of the selected combination or attribute values
    /// </summary>
    public bool LoadPicture { get; set; } = true;
}

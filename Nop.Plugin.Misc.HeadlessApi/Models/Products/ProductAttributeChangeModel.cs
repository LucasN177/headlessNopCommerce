namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents the product details recalculated for the selected attributes
/// </summary>
public record ProductAttributeChangeModel
{
    public int ProductId { get; set; }

    public string Gtin { get; set; }

    public string Mpn { get; set; }

    public string Sku { get; set; }

    /// <summary>
    /// Gets or sets the formatted unit price (including discounts) for the selected attributes; empty if prices are hidden or the customer enters the price
    /// </summary>
    public string Price { get; set; }

    /// <summary>
    /// Gets or sets the unit price (including discounts) for the selected attributes in the working currency
    /// </summary>
    public decimal? PriceValue { get; set; }

    /// <summary>
    /// Gets or sets the formatted old price of the product; null if there is no old price
    /// </summary>
    public string OldPrice { get; set; }

    /// <summary>
    /// Gets or sets the old price of the product in the working currency
    /// </summary>
    public decimal? OldPriceValue { get; set; }

    /// <summary>
    /// Gets or sets the formatted base price (PAngV)
    /// </summary>
    public string BasePricePAngV { get; set; }

    public string StockAvailability { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the attribute mappings whose conditions are met
    /// </summary>
    public IList<int> EnabledAttributeMappingIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the identifiers of the attribute mappings whose conditions are not met
    /// </summary>
    public IList<int> DisabledAttributeMappingIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the full size URL of the picture of the selected combination or attribute values
    /// </summary>
    public string PictureFullSizeUrl { get; set; }

    /// <summary>
    /// Gets or sets the default size URL (product details page) of the picture of the selected combination or attribute values
    /// </summary>
    public string PictureDefaultSizeUrl { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the pictures of the selected combination or attribute values
    /// </summary>
    public IList<int> PictureIds { get; set; } = new List<int>();

    public bool IsFreeShipping { get; set; }

    /// <summary>
    /// Gets or sets the warnings of the attribute parser (e.g. required attributes not selected yet)
    /// </summary>
    public IList<string> Messages { get; set; } = new List<string>();
}

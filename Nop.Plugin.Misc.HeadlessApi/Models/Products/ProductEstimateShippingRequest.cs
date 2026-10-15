namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents a request to estimate the shipping of a product (product details page)
/// </summary>
public record ProductEstimateShippingRequest
{
    public int? CountryId { get; set; }

    public int? StateProvinceId { get; set; }

    public string ZipPostalCode { get; set; }

    public string City { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the selected product attributes, using the same keys as the storefront form (see AddToCartRequest.Attributes)
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

namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to estimate the shipping of the shopping cart
/// </summary>
public record EstimateShippingRequest
{
    public int? CountryId { get; set; }

    public int? StateProvinceId { get; set; }

    public string ZipPostalCode { get; set; }

    /// <summary>
    /// Gets or sets the city (required instead of the ZIP code if "Use city name for shipping estimation" is enabled)
    /// </summary>
    public string City { get; set; }

    /// <summary>
    /// Gets or sets the checkout attributes to save before (see <see cref="CheckoutAttributesRequest.Attributes"/>);
    /// null to keep the currently saved ones
    /// </summary>
    public Dictionary<string, string> CheckoutAttributes { get; set; }
}

namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to select a shipping option returned by the shipping estimation
/// </summary>
public record SelectShippingOptionRequest : EstimateShippingRequest
{
    /// <summary>
    /// Gets or sets the name of the shipping option (see ShippingOptions[].Name of the estimation result)
    /// </summary>
    public string Name { get; set; }
}

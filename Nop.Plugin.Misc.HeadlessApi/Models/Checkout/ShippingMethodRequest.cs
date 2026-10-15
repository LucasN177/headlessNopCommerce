namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents a request to select the shipping method of the checkout
/// </summary>
public record ShippingMethodRequest
{
    /// <summary>
    /// Gets or sets the selected shipping option as "{name}___{shippingRateComputationMethodSystemName}" (as in the storefront)
    /// </summary>
    public string ShippingOption { get; set; }

    /// <summary>
    /// Gets or sets the desired delivery date (only if customers are allowed to choose it)
    /// </summary>
    public DateTime? DesiredDeliveryDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to pick up the order in store (if pickup is displayed on the shipping method step)
    /// </summary>
    public bool PickupInStore { get; set; }

    /// <summary>
    /// Gets or sets the selected pickup point as "{pickupPointId}___{providerSystemName}"
    /// </summary>
    public string PickupPoint { get; set; }
}

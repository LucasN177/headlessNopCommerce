using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents a request to set the billing or shipping address of the checkout
/// </summary>
public record CheckoutAddressRequest
{
    /// <summary>
    /// Gets or sets the identifier of an existing address of the customer; 0 to use the new address
    /// </summary>
    public int AddressId { get; set; }

    /// <summary>
    /// Gets or sets the new address (used if AddressId is 0)
    /// </summary>
    public AddressModel NewAddress { get; set; }

    /// <summary>
    /// Gets or sets the custom address attributes, using the same keys as the storefront form (e.g. "address_attribute_1": "2")
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether to ship to the billing address (billing address only)
    /// </summary>
    public bool ShipToSameAddress { get; set; }

    /// <summary>
    /// Gets or sets the VAT number (billing address of guests only, if EU VAT is enabled)
    /// </summary>
    public string VatNumber { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to pick up the order in store instead of shipping (shipping address only)
    /// </summary>
    public bool PickupInStore { get; set; }

    /// <summary>
    /// Gets or sets the selected pickup point as "{pickupPointId}___{providerSystemName}" (see the pickup points of the shipping address model)
    /// </summary>
    public string PickupPoint { get; set; }
}

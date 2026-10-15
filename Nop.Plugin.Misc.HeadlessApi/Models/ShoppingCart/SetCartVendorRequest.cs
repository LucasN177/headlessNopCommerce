namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to select the vendor whose products are checked out (items of other vendors are set aside)
/// </summary>
public record SetCartVendorRequest
{
    /// <summary>
    /// Gets or sets the vendor identifier (see AvailableVendors of the cart); null or 0 to check out the products of all vendors
    /// </summary>
    public int? VendorId { get; set; }
}

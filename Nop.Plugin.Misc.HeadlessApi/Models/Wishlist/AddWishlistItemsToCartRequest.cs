namespace Nop.Plugin.Misc.HeadlessApi.Models.Wishlist;

/// <summary>
/// Represents a request to add wishlist items to the shopping cart
/// </summary>
public record AddWishlistItemsToCartRequest
{
    /// <summary>
    /// Gets or sets the identifiers of the wishlist items to add
    /// </summary>
    public IList<int> ItemIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the GUID of the customer who shared the wishlist; null for the own wishlist of the current customer
    /// </summary>
    public Guid? CustomerGuid { get; set; }

    /// <summary>
    /// Gets or sets the custom wishlist identifier; null for the default wishlist
    /// </summary>
    public int? ListId { get; set; }
}

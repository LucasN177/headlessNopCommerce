namespace Nop.Plugin.Misc.HeadlessApi.Models.Wishlist;

/// <summary>
/// Represents a request to create or rename a custom wishlist
/// </summary>
public record CustomWishlistRequest
{
    /// <summary>
    /// Gets or sets the wishlist name
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the identifier of a product to move from the default wishlist to the new wishlist (only on creation; optional)
    /// </summary>
    public int? ProductId { get; set; }
}

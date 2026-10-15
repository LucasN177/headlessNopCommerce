namespace Nop.Plugin.Misc.HeadlessApi.Models.Wishlist;

/// <summary>
/// Represents a request to move a wishlist item to another wishlist
/// </summary>
public record MoveWishlistItemRequest
{
    /// <summary>
    /// Gets or sets the target custom wishlist identifier; null or 0 for the default wishlist
    /// </summary>
    public int? ListId { get; set; }
}

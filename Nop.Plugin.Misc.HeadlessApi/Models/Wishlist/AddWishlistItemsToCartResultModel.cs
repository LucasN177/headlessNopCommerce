using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Wishlist;

/// <summary>
/// Represents the result of adding wishlist items to the shopping cart
/// </summary>
public record AddWishlistItemsToCartResultModel
{
    /// <summary>
    /// Gets or sets the number of items added to the shopping cart
    /// </summary>
    public int AddedItemsCount { get; set; }

    /// <summary>
    /// Gets or sets the warnings of items that couldn't be added
    /// </summary>
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the message to display to the customer if there are warnings
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the updated wishlist (items may have been moved to the cart)
    /// </summary>
    public WishlistModel Wishlist { get; set; }
}

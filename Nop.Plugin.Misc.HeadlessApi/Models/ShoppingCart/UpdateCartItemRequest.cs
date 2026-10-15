namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to change the quantity of a shopping cart or wishlist item
/// </summary>
public record UpdateCartItemRequest
{
    public int Quantity { get; set; }
}

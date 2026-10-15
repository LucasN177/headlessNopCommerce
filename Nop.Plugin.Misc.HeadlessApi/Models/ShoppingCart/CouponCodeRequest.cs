namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents a request to apply a discount coupon code or a gift card code
/// </summary>
public record CouponCodeRequest
{
    public string Code { get; set; }
}

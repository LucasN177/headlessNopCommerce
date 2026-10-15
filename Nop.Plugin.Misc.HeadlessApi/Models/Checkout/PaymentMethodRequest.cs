namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents a request to select the payment method of the checkout
/// </summary>
public record PaymentMethodRequest
{
    public string PaymentMethodSystemName { get; set; }

    public bool UseRewardPoints { get; set; }
}

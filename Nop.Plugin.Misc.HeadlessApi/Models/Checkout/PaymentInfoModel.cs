namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents the payment info step
/// </summary>
public record PaymentInfoModel
{
    public string PaymentMethodSystemName { get; set; }

    public string PaymentMethodName { get; set; }

    /// <summary>
    /// Gets or sets the payment method type (Standard, Redirection, Button)
    /// </summary>
    public string PaymentMethodType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the payment info step can be skipped (no form fields)
    /// </summary>
    public bool SkipPaymentInfo { get; set; }

    public string PaymentMethodDescription { get; set; }

    public bool DisplayOrderTotals { get; set; }
}

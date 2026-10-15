namespace Nop.Plugin.Misc.HeadlessApi.Models.Checkout;

/// <summary>
/// Represents the result of a checkout step: the next step the frontend should display and its data
/// </summary>
/// <remarks>
/// Steps are skipped like in the one page checkout of the storefront (e.g. shipping when not required,
/// shipping/payment method selection when only one is available, payment info for methods without a form)
/// </remarks>
public record CheckoutStepModel
{
    /// <summary>
    /// Gets or sets the next step: billing_address, shipping_address, shipping_method, payment_method, payment_info, confirm
    /// </summary>
    public string NextStep { get; set; }

    /// <summary>
    /// Gets or sets the model of the next step (e.g. CheckoutShippingMethodModel for shipping_method)
    /// </summary>
    public object Model { get; set; }

    /// <summary>
    /// Gets or sets warnings to display (e.g. an invalid VAT number), the step has been saved anyway
    /// </summary>
    public IList<string> Warnings { get; set; } = new List<string>();
}

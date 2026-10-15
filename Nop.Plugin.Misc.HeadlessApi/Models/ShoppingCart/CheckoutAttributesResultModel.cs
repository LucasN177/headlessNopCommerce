using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents the result of saving the checkout attributes
/// </summary>
public record CheckoutAttributesResultModel
{
    /// <summary>
    /// Gets or sets the updated shopping cart
    /// </summary>
    public ShoppingCartModel Cart { get; set; }

    /// <summary>
    /// Gets or sets the identifiers of the conditional attributes whose condition is met (to be displayed)
    /// </summary>
    public IList<int> EnabledAttributeIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the identifiers of the conditional attributes whose condition is not met (to be hidden)
    /// </summary>
    public IList<int> DisabledAttributeIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the warnings of the selected attributes (e.g. required attributes not selected), which prevent the checkout
    /// </summary>
    public IList<string> Warnings { get; set; } = new List<string>();
}

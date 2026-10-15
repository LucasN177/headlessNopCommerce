namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents the state of the conditional checkout attributes
/// </summary>
public record CheckoutAttributeConditionsModel
{
    /// <summary>
    /// Gets or sets the identifiers of the conditional attributes whose condition is met
    /// </summary>
    public IList<int> EnabledAttributeIds { get; set; } = new List<int>();

    /// <summary>
    /// Gets or sets the identifiers of the conditional attributes whose condition is not met
    /// </summary>
    public IList<int> DisabledAttributeIds { get; set; } = new List<int>();
}

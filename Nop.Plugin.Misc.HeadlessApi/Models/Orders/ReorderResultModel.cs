namespace Nop.Plugin.Misc.HeadlessApi.Models.Orders;

/// <summary>
/// Represents the result of a re-order (the products of an order are added to the shopping cart)
/// </summary>
public record ReorderResultModel
{
    /// <summary>
    /// Gets or sets the warnings of products that couldn't be added to the shopping cart
    /// </summary>
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the message to display to the customer if not all products could be added
    /// </summary>
    public string Message { get; set; }
}

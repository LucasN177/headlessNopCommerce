namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents the result of a helpfulness vote for a product review
/// </summary>
public record ReviewHelpfulnessResultModel
{
    public int ProductReviewId { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Result { get; set; }

    public int TotalYes { get; set; }

    public int TotalNo { get; set; }
}

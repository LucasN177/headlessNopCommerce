using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents the result of adding a product review
/// </summary>
public record AddProductReviewResultModel
{
    /// <summary>
    /// Gets or sets the identifier of the added review
    /// </summary>
    public int ProductReviewId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the review is approved (visible) right away
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Gets or sets the updated product reviews
    /// </summary>
    public ProductReviewsModel ProductReviews { get; set; }
}

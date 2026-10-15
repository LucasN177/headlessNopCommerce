namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents a request to vote for the helpfulness of a product review
/// </summary>
public record ReviewHelpfulnessRequest
{
    public bool WasHelpful { get; set; }
}

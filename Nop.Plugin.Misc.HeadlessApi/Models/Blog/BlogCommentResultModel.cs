using Nop.Web.Models.Blogs;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Blog;

/// <summary>
/// Represents the result of adding a blog comment
/// </summary>
public record BlogCommentResultModel
{
    /// <summary>
    /// Gets or sets a value indicating whether the comment is approved (otherwise it's displayed after the approval by the store owner)
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// Gets or sets the result message
    /// </summary>
    public string Result { get; set; }

    /// <summary>
    /// Gets or sets the updated blog post (incl. comments)
    /// </summary>
    public BlogPostModel BlogPost { get; set; }
}

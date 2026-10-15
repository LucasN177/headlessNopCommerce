namespace Nop.Plugin.Misc.HeadlessApi.Models.Common;

/// <summary>
/// Represents the result of resolving an SEO friendly URL (slug)
/// </summary>
public record UrlResolveModel
{
    /// <summary>
    /// Gets or sets the entity name (e.g. Product, Category, Manufacturer, Vendor, Topic)
    /// </summary>
    public string EntityName { get; set; }

    public int EntityId { get; set; }

    public int LanguageId { get; set; }

    /// <summary>
    /// Gets or sets the slug the frontend should redirect to (permanently), if the requested one is no longer active
    /// </summary>
    public string RedirectToSlug { get; set; }
}

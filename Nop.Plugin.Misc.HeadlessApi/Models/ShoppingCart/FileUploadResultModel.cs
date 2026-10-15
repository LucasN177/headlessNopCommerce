namespace Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;

/// <summary>
/// Represents the result of uploading a file for a checkout attribute
/// </summary>
public record FileUploadResultModel
{
    /// <summary>
    /// Gets or sets the download GUID to pass as the attribute value
    /// </summary>
    public Guid DownloadGuid { get; set; }

    /// <summary>
    /// Gets or sets the storefront URL to download the uploaded file
    /// </summary>
    public string DownloadUrl { get; set; }

    public string Message { get; set; }
}

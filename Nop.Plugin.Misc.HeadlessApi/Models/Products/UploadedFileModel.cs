namespace Nop.Plugin.Misc.HeadlessApi.Models.Products;

/// <summary>
/// Represents the result of a file upload for a "file upload" product attribute
/// </summary>
public record UploadedFileModel
{
    /// <summary>
    /// Gets or sets the download GUID; pass it as value of the attribute (e.g. "product_attribute_5": "{downloadGuid}") when adding the product to the cart
    /// </summary>
    public Guid DownloadGuid { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }
}

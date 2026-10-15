namespace Nop.Plugin.Misc.HeadlessApi.Models.ReturnRequests;

/// <summary>
/// Represents the result of a file upload for a return request
/// </summary>
public record ReturnRequestFileModel
{
    /// <summary>
    /// Gets or sets the GUID of the uploaded file; pass it as "uploadedFileGuid" when submitting the return request
    /// </summary>
    public Guid DownloadGuid { get; set; }

    /// <summary>
    /// Gets or sets the message to display to the customer
    /// </summary>
    public string Message { get; set; }
}

namespace Nop.Plugin.Misc.HeadlessApi.Models.Downloads;

/// <summary>
/// Represents an external download; returned instead of the file when the download is stored as a URL
/// </summary>
public record DownloadUrlModel
{
    /// <summary>
    /// Gets or sets the URL of the file
    /// </summary>
    public string DownloadUrl { get; set; }
}

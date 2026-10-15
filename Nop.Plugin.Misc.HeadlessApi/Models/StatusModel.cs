namespace Nop.Plugin.Misc.HeadlessApi.Models;

/// <summary>
/// Represents the API status model
/// </summary>
public record StatusModel
{
    public string ApiVersion { get; set; }

    public string NopVersion { get; set; }

    public int StoreId { get; set; }

    public string StoreName { get; set; }
}

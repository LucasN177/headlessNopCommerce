namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents a request to change the working language
/// </summary>
public record SetLanguageRequest
{
    public int LanguageId { get; set; }
}

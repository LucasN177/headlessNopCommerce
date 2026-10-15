namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents a language model
/// </summary>
public record LanguageModel
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string LanguageCulture { get; set; }

    public string UniqueSeoCode { get; set; }

    public bool Rtl { get; set; }
}

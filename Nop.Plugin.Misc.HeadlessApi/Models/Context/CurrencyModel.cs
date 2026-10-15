namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents a currency model
/// </summary>
public record CurrencyModel
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string CurrencyCode { get; set; }
}

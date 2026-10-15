namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents a request to change the working currency
/// </summary>
public record SetCurrencyRequest
{
    public int CurrencyId { get; set; }
}

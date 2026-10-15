using Nop.Core.Domain.Tax;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents a request to change the tax display type (prices including or excluding tax)
/// </summary>
public record SetTaxTypeRequest
{
    public TaxDisplayType TaxDisplayType { get; set; }
}

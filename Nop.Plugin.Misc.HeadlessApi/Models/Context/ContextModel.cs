using Nop.Core.Domain.Tax;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents the context of the current request (customer, store, language, currency, tax display type)
/// </summary>
public record ContextModel
{
    public CustomerInfoModel Customer { get; set; }

    public int StoreId { get; set; }

    public string StoreName { get; set; }

    public LanguageModel WorkingLanguage { get; set; }

    public CurrencyModel WorkingCurrency { get; set; }

    public IList<LanguageModel> AvailableLanguages { get; set; } = new List<LanguageModel>();

    public IList<CurrencyModel> AvailableCurrencies { get; set; } = new List<CurrencyModel>();

    /// <summary>
    /// Gets or sets the tax display type of the current customer (prices including or excluding tax)
    /// </summary>
    public TaxDisplayType TaxDisplayType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the customer is allowed to change the tax display type
    /// </summary>
    public bool AllowCustomersToSelectTaxDisplayType { get; set; }
}

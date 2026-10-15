using Nop.Web.Models.Catalog;
using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Common;

/// <summary>
/// Represents the data of the common layout (header, footer, selectors) composed of the storefront view component models
/// </summary>
public record LayoutModel
{
    public LogoModel Logo { get; set; }

    public HeaderLinksModel HeaderLinks { get; set; }

    public FooterModel Footer { get; set; }

    public SocialModel Social { get; set; }

    /// <summary>
    /// Gets or sets the favicon and app icons; HeadCode contains the HTML tags configured by the store owner to be added to the head element
    /// </summary>
    public FaviconAndAppIconsModel FaviconAndAppIcons { get; set; }

    /// <summary>
    /// Gets or sets the language selector (the storefront hides it if there is only one language)
    /// </summary>
    public LanguageSelectorModel LanguageSelector { get; set; }

    /// <summary>
    /// Gets or sets the currency selector (the storefront hides it if there is only one currency)
    /// </summary>
    public CurrencySelectorModel CurrencySelector { get; set; }

    /// <summary>
    /// Gets or sets the tax type selector; null if customers are not allowed to select the tax display type
    /// </summary>
    public TaxTypeSelectorModel TaxTypeSelector { get; set; }

    public SearchBoxModel SearchBox { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the store is closed
    /// </summary>
    public bool StoreClosed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current customer can access the store although it is closed (e.g. administrators)
    /// </summary>
    public bool CanAccessClosedStore { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current customer can navigate the public store (otherwise only login/registration are available)
    /// </summary>
    public bool NavigationAllowed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to display the EU cookie law warning to the current customer
    /// </summary>
    public bool DisplayEuCookieLawWarning { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to display the mini shopping cart (flyout cart)
    /// </summary>
    public bool MiniShoppingCartEnabled { get; set; }

    public bool BlogEnabled { get; set; }

    public bool SitemapEnabled { get; set; }

    public bool AllowCustomersToContactVendors { get; set; }
}

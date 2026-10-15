namespace Nop.Plugin.Misc.HeadlessApi.Models.Directory;

/// <summary>
/// Represents the address type the countries are requested for
/// </summary>
public enum CountryAddressType
{
    /// <summary>
    /// All published countries
    /// </summary>
    All = 0,

    /// <summary>
    /// Countries allowing billing (billing address form)
    /// </summary>
    Billing = 10,

    /// <summary>
    /// Countries allowing shipping (shipping address and estimate shipping forms)
    /// </summary>
    Shipping = 20
}

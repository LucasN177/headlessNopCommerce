namespace Nop.Plugin.Misc.HeadlessApi.Models.Directory;

/// <summary>
/// Represents a country available in the address forms
/// </summary>
public record CountryModel
{
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the localized name
    /// </summary>
    public string Name { get; set; }

    public string TwoLetterIsoCode { get; set; }

    public string ThreeLetterIsoCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether billing is allowed to this country
    /// </summary>
    public bool AllowsBilling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether shipping is allowed to this country
    /// </summary>
    public bool AllowsShipping { get; set; }

    public bool SubjectToVat { get; set; }
}

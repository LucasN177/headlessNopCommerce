namespace Nop.Plugin.Misc.HeadlessApi.Models.Catalog;

/// <summary>
/// Represents the available filter level values (cascading selection: level 1, then level 2, then level 3)
/// </summary>
public record FilterLevelValuesModel
{
    /// <summary>
    /// Gets or sets the text of the default (empty) item of the selection lists
    /// </summary>
    public string DefaultItemText { get; set; }

    /// <summary>
    /// Gets or sets the filter level values
    /// </summary>
    public IList<FilterLevelValueItemModel> Values { get; set; } = new List<FilterLevelValueItemModel>();
}

/// <summary>
/// Represents a filter level value
/// </summary>
public record FilterLevelValueItemModel
{
    public string FilterLevel1Value { get; set; }

    /// <summary>
    /// Gets or sets the level 2 value; null as long as no level 1 value is selected
    /// </summary>
    public string FilterLevel2Value { get; set; }

    /// <summary>
    /// Gets or sets the level 3 value; null as long as no level 2 value is selected
    /// </summary>
    public string FilterLevel3Value { get; set; }
}

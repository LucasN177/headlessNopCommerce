namespace Nop.Plugin.Misc.HeadlessApi.Models.Account;

/// <summary>
/// Represents the result of the username availability check
/// </summary>
public record UsernameAvailabilityModel
{
    /// <summary>
    /// Gets or sets a value indicating whether the username is available
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// Gets or sets the status text to display to the customer
    /// </summary>
    public string Text { get; set; }
}

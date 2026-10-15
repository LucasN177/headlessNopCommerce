using Nop.Web.Models.Profile;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Profiles;

/// <summary>
/// Represents the public profile of a customer
/// </summary>
public record CustomerProfileModel
{
    /// <summary>
    /// Gets or sets the profile page model (title, paging of the latest forum posts)
    /// </summary>
    public ProfileIndexModel Profile { get; set; }

    /// <summary>
    /// Gets or sets the profile info (avatar, location, join date, forum statistics, etc.)
    /// </summary>
    public ProfileInfoModel Info { get; set; }
}

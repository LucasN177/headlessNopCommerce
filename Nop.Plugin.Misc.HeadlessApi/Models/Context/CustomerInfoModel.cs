namespace Nop.Plugin.Misc.HeadlessApi.Models.Context;

/// <summary>
/// Represents the basic information about the current customer
/// </summary>
public record CustomerInfoModel
{
    public Guid CustomerGuid { get; set; }

    public bool IsRegistered { get; set; }

    public string Email { get; set; }

    public string Username { get; set; }

    public string FullName { get; set; }
}

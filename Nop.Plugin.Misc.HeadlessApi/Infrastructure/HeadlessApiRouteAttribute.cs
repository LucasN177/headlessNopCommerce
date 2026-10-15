using Microsoft.AspNetCore.Mvc;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Represents the route attribute of the API controllers, which prepends the API route prefix (api/headless/v1)
/// </summary>
/// <remarks>
/// A [Route] attribute on the base controller can't be used, because route attributes of base and derived classes
/// aren't combined: [HeadlessApiRoute("catalog")] results in "api/headless/v1/catalog"
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HeadlessApiRouteAttribute : RouteAttribute
{
    #region Ctor

    /// <summary>
    /// Create instance of the attribute
    /// </summary>
    /// <param name="template">Route template of the controller, relative to the API route prefix; empty for the prefix only</param>
    public HeadlessApiRouteAttribute(string template = "")
        : base(string.IsNullOrEmpty(template) ? HeadlessApiDefaults.RoutePrefix : $"{HeadlessApiDefaults.RoutePrefix}/{template.Trim('/')}")
    {
    }

    #endregion
}

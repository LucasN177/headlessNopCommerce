using Microsoft.AspNetCore.Http;

namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents plugin constants
/// </summary>
public class HeadlessApiDefaults
{
    /// <summary>
    /// Gets a plugin system name
    /// </summary>
    public static string SystemName => "Misc.HeadlessApi";

    /// <summary>
    /// Gets the current API version
    /// </summary>
    public const string ApiVersion = "v1";

    /// <summary>
    /// Gets the route prefix of all API endpoints
    /// </summary>
    public const string RoutePrefix = $"api/headless/{ApiVersion}";

    /// <summary>
    /// Gets the base path of all API requests (all versions)
    /// </summary>
    public static PathString BasePath => new("/api/headless");

    /// <summary>
    /// Gets the name of the CORS policy applied to the API endpoints
    /// </summary>
    public const string CorsPolicyName = "HeadlessApiCorsPolicy";

    /// <summary>
    /// Gets the name of the authentication scheme for the API tokens
    /// </summary>
    public const string AuthenticationScheme = "HeadlessApi";

    /// <summary>
    /// Gets the issuer of the API tokens
    /// </summary>
    public static string TokenIssuer => "nopCommerce.HeadlessApi";

    /// <summary>
    /// Gets the name of the token claim that contains the customer security stamp
    /// </summary>
    public static string SecurityStampClaim => "stamp";

    /// <summary>
    /// Gets the name of the generic attribute that stores the customer security stamp
    /// </summary>
    /// <remarks>
    /// Changing the stamp invalidates all tokens previously issued for the customer
    /// </remarks>
    public static string SecurityStampAttribute => "HeadlessApi.SecurityStamp";

    /// <summary>
    /// Gets the key of the HTTP context item that contains the customer authenticated by the token
    /// </summary>
    public static string AuthenticatedCustomerItemKey => "HeadlessApi.AuthenticatedCustomer";
}

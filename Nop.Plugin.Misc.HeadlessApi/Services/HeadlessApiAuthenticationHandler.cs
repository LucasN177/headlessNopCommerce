using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the authentication handler for the API tokens (Authorization: Bearer {token})
/// </summary>
public class HeadlessApiAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    #region Fields

    protected readonly HeadlessApiTokenService _headlessApiTokenService;

    #endregion

    #region Ctor

    public HeadlessApiAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        HeadlessApiTokenService headlessApiTokenService) : base(options, logger, encoder)
    {
        _headlessApiTokenService = headlessApiTokenService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Authenticates the request by the passed token
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the authentication result</returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string bearerPrefix = "Bearer ";

        string authorization = Request.Headers.Authorization;
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var customer = await _headlessApiTokenService.GetCustomerByTokenAsync(authorization[bearerPrefix.Length..].Trim());
        if (customer == null)
            return AuthenticateResult.Fail("The token is invalid, expired or revoked");

        //share the customer with the authentication service, so it doesn't need to be loaded again
        Context.Items[HeadlessApiDefaults.AuthenticatedCustomerItemKey] = customer;

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, customer.CustomerGuid.ToString(), ClaimValueTypes.String, HeadlessApiDefaults.TokenIssuer)
        }, Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    /// <summary>
    /// Responds with 401 (Unauthorized) instead of redirecting to the login page
    /// </summary>
    /// <param name="properties">Authentication properties</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";

        return Task.CompletedTask;
    }

    #endregion
}

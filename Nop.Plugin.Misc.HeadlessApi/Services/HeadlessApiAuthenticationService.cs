using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Nop.Core.Domain.Customers;
using Nop.Services.Authentication;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the authentication service that identifies the customer of API requests by the token,
/// for all other requests the default cookie authentication is used
/// </summary>
/// <remarks>
/// Since the work context gets the current customer from this service, all nopCommerce services
/// (prices, shopping cart, ACL, etc.) work with the customer of the token without further changes
/// </remarks>
public class HeadlessApiAuthenticationService : CookieAuthenticationService
{
    #region Ctor

    public HeadlessApiAuthenticationService(CustomerSettings customerSettings,
        ICustomerService customerService,
        IHttpContextAccessor httpContextAccessor) : base(customerSettings, customerService, httpContextAccessor)
    {
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets a value indicating whether the current request is an API request
    /// </summary>
    protected virtual bool IsApiRequest()
    {
        return _httpContextAccessor.HttpContext?.Request.Path.StartsWithSegments(HeadlessApiDefaults.BasePath, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Sign in
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="isPersistent">Whether the authentication session is persisted across multiple requests</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task SignInAsync(Customer customer, bool isPersistent)
    {
        if (!IsApiRequest())
        {
            await base.SignInAsync(customer, isPersistent);
            return;
        }

        //no cookie for API requests, the client gets a token instead
        ArgumentNullException.ThrowIfNull(customer);
        _cachedCustomer = customer;
    }

    /// <summary>
    /// Sign out
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task SignOutAsync()
    {
        if (!IsApiRequest())
        {
            await base.SignOutAsync();
            return;
        }

        //tokens are revoked by the API itself
        _cachedCustomer = null;
    }

    /// <summary>
    /// Get authenticated customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the customer</returns>
    public override async Task<Customer> GetAuthenticatedCustomerAsync()
    {
        if (!IsApiRequest())
            return await base.GetAuthenticatedCustomerAsync();

        if (_cachedCustomer != null)
            return _cachedCustomer;

        //unlike the cookie authentication, guests are authenticated by their token too
        var httpContext = _httpContextAccessor.HttpContext;
        var authenticateResult = await httpContext.AuthenticateAsync(HeadlessApiDefaults.AuthenticationScheme);
        if (!authenticateResult.Succeeded)
            return null;

        _cachedCustomer = httpContext.Items[HeadlessApiDefaults.AuthenticatedCustomerItemKey] as Customer;

        return _cachedCustomer;
    }

    #endregion
}

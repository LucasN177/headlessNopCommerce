using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Events;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Auth;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Authentication;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the endpoints to get API tokens and to log in and out
/// </summary>
/// <remarks>
/// A frontend first requests a guest token and sends it with every request (Authorization: Bearer {token}).
/// On login the guest's shopping cart is migrated to the registered customer and a new token is issued.
/// </remarks>
[HeadlessApiRoute("auth")]
public class AuthApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CustomerSettings _customerSettings;
    protected readonly HeadlessApiTokenService _headlessApiTokenService;
    protected readonly IAuthenticationService _authenticationService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ICustomerRegistrationService _customerRegistrationService;
    protected readonly ICustomerService _customerService;
    protected readonly IEventPublisher _eventPublisher;
    protected readonly ILocalizationService _localizationService;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;

    #endregion

    #region Ctor

    public AuthApiController(CustomerSettings customerSettings,
        HeadlessApiTokenService headlessApiTokenService,
        IAuthenticationService authenticationService,
        ICustomerActivityService customerActivityService,
        ICustomerRegistrationService customerRegistrationService,
        ICustomerService customerService,
        IEventPublisher eventPublisher,
        ILocalizationService localizationService,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService)
    {
        _customerSettings = customerSettings;
        _headlessApiTokenService = headlessApiTokenService;
        _authenticationService = authenticationService;
        _customerActivityService = customerActivityService;
        _customerRegistrationService = customerRegistrationService;
        _customerService = customerService;
        _eventPublisher = eventPublisher;
        _localizationService = localizationService;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Create a problem result for a failed login
    /// </summary>
    /// <param name="loginResult">Login result</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result</returns>
    protected virtual async Task<IActionResult> LoginProblemAsync(CustomerLoginResults loginResult)
    {
        var (statusCode, resourceKey) = loginResult switch
        {
            CustomerLoginResults.Deleted => (StatusCodes.Status401Unauthorized, "Account.Login.WrongCredentials.Deleted"),
            CustomerLoginResults.NotActive => (StatusCodes.Status401Unauthorized, "Account.Login.WrongCredentials.NotActive"),
            CustomerLoginResults.LockedOut => (StatusCodes.Status401Unauthorized, "Account.Login.WrongCredentials.LockedOut"),
            CustomerLoginResults.MultiFactorAuthenticationRequired => (StatusCodes.Status403Forbidden, "Plugins.Misc.HeadlessApi.Auth.MultiFactorAuthenticationNotSupported"),
            _ => (StatusCodes.Status401Unauthorized, "Account.Login.WrongCredentials")
        };

        return Problem(statusCode: statusCode, detail: await _localizationService.GetResourceAsync(resourceKey));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Creates a new guest customer and issues a token for it
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the token</returns>
    [AllowAnonymous]
    [HttpPost("guest")]
    [ProducesResponseType(typeof(TokenModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Guest()
    {
        var customer = await _customerService.InsertGuestCustomerAsync();

        return Ok(await _headlessApiTokenService.PrepareTokenModelAsync(customer));
    }

    /// <summary>
    /// Issues a new token for the current customer, e.g. before the current one expires
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the token</returns>
    [HttpPost("refresh")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(TokenModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public virtual async Task<IActionResult> Refresh()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        return Ok(await _headlessApiTokenService.PrepareTokenModelAsync(customer));
    }

    /// <summary>
    /// Logs in a customer; the cart of the current (guest) customer is migrated to the logged in customer
    /// </summary>
    /// <param name="request">Login request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the token of the logged in customer</returns>
    [HttpPost("login")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(TokenModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Login(LoginRequest request)
    {
        var usernameOrEmail = _customerSettings.UsernamesEnabled ? request.Username : request.Email;
        if (string.IsNullOrWhiteSpace(usernameOrEmail) || string.IsNullOrEmpty(request.Password))
        {
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Account.Login.WrongCredentials"));
            return ValidationProblem(ModelState);
        }

        usernameOrEmail = usernameOrEmail.Trim();
        var loginResult = await _customerRegistrationService.ValidateCustomerAsync(usernameOrEmail, request.Password);
        var customer = _customerSettings.UsernamesEnabled
            ? await _customerService.GetCustomerByUsernameAsync(usernameOrEmail)
            : await _customerService.GetCustomerByEmailAsync(usernameOrEmail);

        if (loginResult != CustomerLoginResults.Successful)
        {
            if (loginResult == CustomerLoginResults.WrongPassword && _customerSettings.NotifyFailedLoginAttempt && customer != null)
                await _workflowMessageService.SendCustomerFailedLoginAttemptNotificationAsync(customer, customer.LanguageId ?? 0);

            return await LoginProblemAsync(loginResult);
        }

        //migrate the shopping cart, raise the event and log the activity like the storefront does
        //(the returned redirect result is not relevant for the API)
        await _customerRegistrationService.SignInCustomerAsync(customer, null);

        return Ok(await _headlessApiTokenService.PrepareTokenModelAsync(customer));
    }

    /// <summary>
    /// Logs out the current customer and revokes all its tokens
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    [HttpPost("logout")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public virtual async Task<IActionResult> Logout()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return NoContent();

        //activity log
        await _customerActivityService.InsertActivityAsync(customer, "PublicStore.Logout",
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.Logout"), customer);

        await _authenticationService.SignOutAsync();
        await _headlessApiTokenService.RevokeTokensAsync(customer);

        //raise logged out event
        await _eventPublisher.PublishAsync(new CustomerLoggedOutEvent(customer));

        return NoContent();
    }

    #endregion
}

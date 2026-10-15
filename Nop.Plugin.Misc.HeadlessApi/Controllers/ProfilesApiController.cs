using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessApi.Models.Profiles;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Factories;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the public customer profile endpoints (only if "Allow viewing profiles" is enabled)
/// </summary>
[HeadlessApiRoute("profiles")]
public class ProfilesApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CustomerSettings _customerSettings;
    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IProfileModelFactory _profileModelFactory;

    #endregion

    #region Ctor

    public ProfilesApiController(CustomerSettings customerSettings,
        ICustomerService customerService,
        ILocalizationService localizationService,
        IProfileModelFactory profileModelFactory)
    {
        _customerSettings = customerSettings;
        _customerService = customerService;
        _localizationService = localizationService;
        _profileModelFactory = profileModelFactory;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the public profile of a customer
    /// </summary>
    /// <param name="customerId">Customer identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the profile</returns>
    [HttpGet("{customerId:int}")]
    [ProducesResponseType(typeof(CustomerProfileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Get(int customerId)
    {
        if (!_customerSettings.AllowViewingProfiles)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer == null || await _customerService.IsGuestAsync(customer))
            return NotFoundProblem();

        return Ok(new CustomerProfileModel
        {
            Profile = await _profileModelFactory.PrepareProfileIndexModelAsync(customer),
            Info = await _profileModelFactory.PrepareProfileInfoModelAsync(customer)
        });
    }

    #endregion
}

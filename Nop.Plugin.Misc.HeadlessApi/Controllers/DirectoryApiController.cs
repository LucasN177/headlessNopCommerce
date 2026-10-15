using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Directory;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Web.Factories;
using Nop.Web.Models.Directory;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the directory endpoints (countries, states/provinces) for the address forms
/// </summary>
/// <remarks>
/// Available even when the store is closed or navigation is not allowed, since they are needed by the registration form
/// </remarks>
[HeadlessApiRoute("countries")]
public class DirectoryApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly ICountryModelFactory _countryModelFactory;
    protected readonly ICountryService _countryService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public DirectoryApiController(ICountryModelFactory countryModelFactory,
        ICountryService countryService,
        ILocalizationService localizationService,
        IWorkContext workContext)
    {
        _countryModelFactory = countryModelFactory;
        _countryService = countryService;
        _localizationService = localizationService;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the published countries of the current store
    /// </summary>
    /// <param name="type">Address type: All, Billing (countries allowing billing) or Shipping (countries allowing shipping)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the countries</returns>
    [HttpGet]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(IList<CountryModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Countries([FromQuery] CountryAddressType type = CountryAddressType.All)
    {
        var languageId = (await _workContext.GetWorkingLanguageAsync()).Id;
        var countries = type switch
        {
            CountryAddressType.Billing => await _countryService.GetAllCountriesForBillingAsync(languageId),
            CountryAddressType.Shipping => await _countryService.GetAllCountriesForShippingAsync(languageId),
            _ => await _countryService.GetAllCountriesAsync(languageId)
        };

        var model = new List<CountryModel>();
        foreach (var country in countries)
        {
            model.Add(new CountryModel
            {
                Id = country.Id,
                Name = await _localizationService.GetLocalizedAsync(country, entity => entity.Name, languageId),
                TwoLetterIsoCode = country.TwoLetterIsoCode,
                ThreeLetterIsoCode = country.ThreeLetterIsoCode,
                AllowsBilling = country.AllowsBilling,
                AllowsShipping = country.AllowsShipping,
                SubjectToVat = country.SubjectToVat
            });
        }

        return Ok(model);
    }

    /// <summary>
    /// Gets the states/provinces of a country (like GetStatesByCountryId of the storefront)
    /// </summary>
    /// <param name="countryId">Country identifier</param>
    /// <param name="addSelectStateItem">Whether to add the "Select state" item (id 0) if the country has states;
    /// if the country has no states, the result always contains the "Other" item (id 0)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the states/provinces (properties "id" and "name")</returns>
    [HttpGet("{countryId:int}/states")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(IList<StateProvinceModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> States(int countryId, [FromQuery] bool addSelectStateItem = false)
    {
        return Ok(await _countryModelFactory.GetStatesByCountryIdAsync(countryId, addSelectStateItem));
    }

    #endregion
}

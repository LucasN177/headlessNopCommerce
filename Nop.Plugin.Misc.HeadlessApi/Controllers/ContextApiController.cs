using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Tax;
using Nop.Plugin.Misc.HeadlessApi.Models.Context;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the endpoints to get and change the context of the current customer (language, currency, tax display type)
/// </summary>
[HeadlessApiRoute("context")]
public class ContextApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly ICurrencyService _currencyService;
    protected readonly ICustomerService _customerService;
    protected readonly ILanguageService _languageService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly TaxSettings _taxSettings;

    #endregion

    #region Ctor

    public ContextApiController(ICurrencyService currencyService,
        ICustomerService customerService,
        ILanguageService languageService,
        ILocalizationService localizationService,
        IStoreContext storeContext,
        IWorkContext workContext,
        TaxSettings taxSettings)
    {
        _currencyService = currencyService;
        _customerService = customerService;
        _languageService = languageService;
        _localizationService = localizationService;
        _storeContext = storeContext;
        _workContext = workContext;
        _taxSettings = taxSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Prepare the context model of the current request
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the context model</returns>
    protected virtual async Task<ContextModel> PrepareContextModelAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var workingLanguage = await _workContext.GetWorkingLanguageAsync();
        var workingCurrency = await _workContext.GetWorkingCurrencyAsync();

        var model = new ContextModel
        {
            Customer = new CustomerInfoModel
            {
                CustomerGuid = customer.CustomerGuid,
                IsRegistered = await _customerService.IsRegisteredAsync(customer),
                Email = customer.Email,
                Username = customer.Username,
                FullName = await _customerService.GetCustomerFullNameAsync(customer)
            },
            StoreId = store.Id,
            StoreName = store.Name,
            WorkingLanguage = PrepareLanguageModel(workingLanguage),
            WorkingCurrency = PrepareCurrencyModel(workingCurrency),
            TaxDisplayType = await _workContext.GetTaxDisplayTypeAsync(),
            AllowCustomersToSelectTaxDisplayType = _taxSettings.AllowCustomersToSelectTaxDisplayType
        };

        foreach (var language in await _languageService.GetAllLanguagesAsync(storeId: store.Id))
            model.AvailableLanguages.Add(PrepareLanguageModel(language));

        foreach (var currency in await _currencyService.GetAllCurrenciesAsync(storeId: store.Id))
            model.AvailableCurrencies.Add(PrepareCurrencyModel(currency));

        return model;
    }

    /// <summary>
    /// Prepare the language model
    /// </summary>
    /// <param name="language">Language</param>
    /// <returns>Language model</returns>
    protected virtual LanguageModel PrepareLanguageModel(Language language)
    {
        return new LanguageModel
        {
            Id = language.Id,
            Name = language.Name,
            LanguageCulture = language.LanguageCulture,
            UniqueSeoCode = language.UniqueSeoCode,
            Rtl = language.Rtl
        };
    }

    /// <summary>
    /// Prepare the currency model
    /// </summary>
    /// <param name="currency">Currency</param>
    /// <returns>Currency model</returns>
    protected virtual CurrencyModel PrepareCurrencyModel(Currency currency)
    {
        return new CurrencyModel
        {
            Id = currency.Id,
            Name = currency.Name,
            CurrencyCode = currency.CurrencyCode
        };
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the context of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the context</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ContextModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Get()
    {
        return Ok(await PrepareContextModelAsync());
    }

    /// <summary>
    /// Changes the working language of the current customer
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated context</returns>
    [HttpPut("language")]
    [ProducesResponseType(typeof(ContextModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetLanguage(SetLanguageRequest request)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var language = (await _languageService.GetAllLanguagesAsync(storeId: store.Id))
            .FirstOrDefault(language => language.Id == request.LanguageId);
        if (language == null)
        {
            ModelState.AddModelError(nameof(request.LanguageId), await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Context.LanguageNotAvailable"));
            return ValidationProblem(ModelState);
        }

        await _workContext.SetWorkingLanguageAsync(language);

        return Ok(await PrepareContextModelAsync());
    }

    /// <summary>
    /// Changes the working currency of the current customer
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated context</returns>
    [HttpPut("currency")]
    [ProducesResponseType(typeof(ContextModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetCurrency(SetCurrencyRequest request)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var currency = (await _currencyService.GetAllCurrenciesAsync(storeId: store.Id))
            .FirstOrDefault(currency => currency.Id == request.CurrencyId);
        if (currency == null)
        {
            ModelState.AddModelError(nameof(request.CurrencyId), await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Context.CurrencyNotAvailable"));
            return ValidationProblem(ModelState);
        }

        await _workContext.SetWorkingCurrencyAsync(currency);

        return Ok(await PrepareContextModelAsync());
    }

    /// <summary>
    /// Changes the tax display type (prices including or excluding tax) of the current customer
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated context</returns>
    [HttpPut("tax-type")]
    [ProducesResponseType(typeof(ContextModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> SetTaxType(SetTaxTypeRequest request)
    {
        if (!_taxSettings.AllowCustomersToSelectTaxDisplayType)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Context.TaxTypeSelectionNotAllowed"));

        if (!Enum.IsDefined(request.TaxDisplayType))
        {
            ModelState.AddModelError(nameof(request.TaxDisplayType), await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Context.TaxTypeNotAvailable"));
            return ValidationProblem(ModelState);
        }

        await _workContext.SetTaxDisplayTypeAsync(request.TaxDisplayType);

        return Ok(await PrepareContextModelAsync());
    }

    #endregion
}

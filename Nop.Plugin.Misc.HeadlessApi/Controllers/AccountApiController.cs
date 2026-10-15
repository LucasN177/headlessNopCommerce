using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Gdpr;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tax;
using Nop.Core.Events;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Account;
using Nop.Plugin.Misc.HeadlessApi.Models.Auth;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Attributes;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Gdpr;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Tax;
using Nop.Web.Factories;
using Nop.Web.Framework.Validators;
using Nop.Web.Models.Common;
using Nop.Web.Models.Customer;
using Nop.Web.Models.Order;
using PhoneNumbers;
using ILogger = Nop.Services.Logging.ILogger;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the customer account endpoints (registration, account activation, password recovery, customer info, password, addresses, orders)
/// </summary>
/// <remarks>
/// Request bodies use the storefront models of nopCommerce (RegisterModel, CustomerInfoModel, ChangePasswordModel, AddressModel),
/// so they are validated by the same validators (required fields, password rules, etc.).
/// Custom customer attributes and GDPR consents are passed in the "attributes" dictionary using the keys of the storefront form.
/// CAPTCHA is not supported by the API.
/// </remarks>
[HeadlessApiRoute("account")]
public class AccountApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly AddressSettings _addressSettings;
    protected readonly CustomerSettings _customerSettings;
    protected readonly DateTimeSettings _dateTimeSettings;
    protected readonly GdprSettings _gdprSettings;
    protected readonly HeadlessApiTokenService _headlessApiTokenService;
    protected readonly IAddressService _addressService;
    protected readonly IAttributeParser<AddressAttribute, AddressAttributeValue> _addressAttributeParser;
    protected readonly IAttributeParser<CustomerAttribute, CustomerAttributeValue> _customerAttributeParser;
    protected readonly ICountryService _countryService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ICustomerModelFactory _customerModelFactory;
    protected readonly ICustomerRegistrationService _customerRegistrationService;
    protected readonly ICustomerService _customerService;
    protected readonly IEventPublisher _eventPublisher;
    protected readonly IGdprService _gdprService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly ILogger _logger;
    protected readonly INewsLetterSubscriptionService _newsLetterSubscriptionService;
    protected readonly IOrderModelFactory _orderModelFactory;
    protected readonly IOrderService _orderService;
    protected readonly IStateProvinceService _stateProvinceService;
    protected readonly IStoreContext _storeContext;
    protected readonly ITaxService _taxService;
    protected readonly IValidator<CustomerInfoModel> _customerInfoValidator;
    protected readonly IValidator<PasswordRecoveryConfirmModel> _passwordRecoveryConfirmValidator;
    protected readonly IValidator<RegisterModel> _registerValidator;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly LocalizationSettings _localizationSettings;
    protected readonly OtpSettings _otpSettings;
    protected readonly TaxSettings _taxSettings;

    #endregion

    #region Ctor

    public AccountApiController(AddressSettings addressSettings,
        CustomerSettings customerSettings,
        DateTimeSettings dateTimeSettings,
        GdprSettings gdprSettings,
        HeadlessApiTokenService headlessApiTokenService,
        IAddressService addressService,
        IAttributeParser<AddressAttribute, AddressAttributeValue> addressAttributeParser,
        IAttributeParser<CustomerAttribute, CustomerAttributeValue> customerAttributeParser,
        ICountryService countryService,
        ICustomerActivityService customerActivityService,
        ICustomerModelFactory customerModelFactory,
        ICustomerRegistrationService customerRegistrationService,
        ICustomerService customerService,
        IEventPublisher eventPublisher,
        IGdprService gdprService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        ILogger logger,
        INewsLetterSubscriptionService newsLetterSubscriptionService,
        IOrderModelFactory orderModelFactory,
        IOrderService orderService,
        IStateProvinceService stateProvinceService,
        IStoreContext storeContext,
        ITaxService taxService,
        IValidator<CustomerInfoModel> customerInfoValidator,
        IValidator<PasswordRecoveryConfirmModel> passwordRecoveryConfirmValidator,
        IValidator<RegisterModel> registerValidator,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings,
        OtpSettings otpSettings,
        TaxSettings taxSettings)
    {
        _addressSettings = addressSettings;
        _customerSettings = customerSettings;
        _dateTimeSettings = dateTimeSettings;
        _gdprSettings = gdprSettings;
        _headlessApiTokenService = headlessApiTokenService;
        _addressService = addressService;
        _addressAttributeParser = addressAttributeParser;
        _customerAttributeParser = customerAttributeParser;
        _countryService = countryService;
        _customerActivityService = customerActivityService;
        _customerModelFactory = customerModelFactory;
        _customerRegistrationService = customerRegistrationService;
        _customerService = customerService;
        _eventPublisher = eventPublisher;
        _gdprService = gdprService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _logger = logger;
        _newsLetterSubscriptionService = newsLetterSubscriptionService;
        _orderModelFactory = orderModelFactory;
        _orderService = orderService;
        _stateProvinceService = stateProvinceService;
        _storeContext = storeContext;
        _taxService = taxService;
        _customerInfoValidator = customerInfoValidator;
        _passwordRecoveryConfirmValidator = passwordRecoveryConfirmValidator;
        _registerValidator = registerValidator;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
        _otpSettings = otpSettings;
        _taxSettings = taxSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Validate the model by the passed validator and add the errors to the model state
    /// </summary>
    /// <typeparam name="TModel">Model type</typeparam>
    /// <param name="validator">Validator</param>
    /// <param name="model">Model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    /// <remarks>
    /// The request models extend the storefront models, so the storefront validators aren't applied automatically
    /// </remarks>
    protected virtual async Task ValidateAsync<TModel>(IValidator<TModel> validator, TModel model)
    {
        var validationResult = await validator.ValidateAsync(model, HttpContext.RequestAborted);
        foreach (var error in validationResult.Errors)
            ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
    }

    /// <summary>
    /// Convert the custom form fields of the request to a form collection, so the storefront parsers of nopCommerce can be used
    /// </summary>
    /// <param name="attributes">Custom form fields (keys of the storefront form)</param>
    /// <param name="gdprConsents">GDPR consents of the request model; accepted consents are added to the form</param>
    /// <returns>Form collection</returns>
    protected virtual IFormCollection PrepareForm(IDictionary<string, string> attributes, IList<GdprConsentModel> gdprConsents)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
        {
            //the storefront form posts "on" for a checked consent, accept boolean values as well
            if (key.StartsWith("consent", StringComparison.InvariantCultureIgnoreCase) && bool.TryParse(value, out var accepted))
                values[key] = accepted ? "on" : string.Empty;
            else
                values[key] = value;
        }

        foreach (var acceptedConsent in gdprConsents?.Where(item => item.Accepted) ?? Enumerable.Empty<GdprConsentModel>())
            values.TryAdd($"consent{acceptedConsent.Id}", "on");

        return new FormCollection(values);
    }

    /// <summary>
    /// Parse the custom customer attributes from the form
    /// </summary>
    /// <param name="form">Form</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the attributes in XML format</returns>
    protected virtual async Task<string> ParseCustomCustomerAttributesAsync(IFormCollection form)
    {
        return await _customerAttributeParser.ParseCustomAttributesAsync(form, $"{NopCustomerServicesDefaults.CustomerAttributePrefix}{{0}}");
    }

    /// <summary>
    /// Check whether the consent is accepted in the form
    /// </summary>
    /// <param name="form">Form</param>
    /// <param name="consent">GDPR consent</param>
    /// <returns>True if the consent is accepted; otherwise false</returns>
    protected virtual bool IsConsentAccepted(IFormCollection form, GdprConsent consent)
    {
        var cbConsent = form[$"consent{consent.Id}"];

        return !StringValues.IsNullOrEmpty(cbConsent) && cbConsent.ToString().Equals("on");
    }

    /// <summary>
    /// Validate that all required consents are accepted
    /// </summary>
    /// <param name="consents">Required consents</param>
    /// <param name="form">Form</param>
    protected virtual void ValidateRequiredConsents(List<GdprConsent> consents, IFormCollection form)
    {
        foreach (var consent in consents.Where(item => !IsConsentAccepted(form, item)))
            ModelState.AddModelError(string.Empty, consent.RequiredMessage);
    }

    /// <summary>
    /// Format the phone number to the E.164 format
    /// </summary>
    /// <param name="phone">Phone number</param>
    /// <returns>Formatted phone number; empty if the number is not valid</returns>
    protected virtual string FormatPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone))
            return phone;

        try
        {
            var phoneNumberUtil = PhoneNumberUtil.GetInstance();
            var regionCode = phoneNumberUtil.GetRegionCodeForNumber(phoneNumberUtil.Parse(phone, null));

            var phoneNumber = phoneNumberUtil.Parse(phone, regionCode);
            return phoneNumberUtil.Format(phoneNumber, PhoneNumberFormat.E164);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Validate that the phone number is not used by another customer (login by phone)
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="phoneNumber">Formatted phone number</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task ValidatePhoneNumberAsync(Customer customer, string phoneNumber)
    {
        if (_otpSettings.LoginByPhoneEnabled && !string.IsNullOrEmpty(phoneNumber) && await _customerService.IsAlreadyExistsVerifiedPhoneNumberAsync(customer, phoneNumber))
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Account.IsAlreadyExistsVerifiedPhoneNumber"));
    }

    /// <summary>
    /// Copy the registration form fields enabled in the customer settings to the customer
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="model">Registration model</param>
    /// <param name="phoneNumber">Formatted phone number</param>
    protected virtual void SetCustomerFields(Customer customer, RegisterModel model, string phoneNumber)
    {
        if (_customerSettings.GenderEnabled)
            customer.Gender = model.Gender;
        if (_customerSettings.FirstNameEnabled)
            customer.FirstName = model.FirstName;
        if (_customerSettings.LastNameEnabled)
            customer.LastName = model.LastName;
        if (_customerSettings.DateOfBirthEnabled)
            customer.DateOfBirth = model.ParseDateOfBirth();
        if (_customerSettings.CompanyEnabled)
            customer.Company = model.Company;
        if (_customerSettings.StreetAddressEnabled)
            customer.StreetAddress = model.StreetAddress;
        if (_customerSettings.StreetAddress2Enabled)
            customer.StreetAddress2 = model.StreetAddress2;
        if (_customerSettings.ZipPostalCodeEnabled)
            customer.ZipPostalCode = model.ZipPostalCode;
        if (_customerSettings.CityEnabled)
            customer.City = model.City;
        if (_customerSettings.CountyEnabled)
            customer.County = model.County;
        if (_customerSettings.CountryEnabled)
            customer.CountryId = model.CountryId;
        if (_customerSettings.CountryEnabled && _customerSettings.StateProvinceEnabled)
            customer.StateProvinceId = model.StateProvinceId;
        if (_customerSettings.PhoneEnabled)
            customer.Phone = phoneNumber;
        if (_customerSettings.FaxEnabled)
            customer.Fax = model.Fax;
    }

    /// <summary>
    /// Insert the default (billing and shipping) address of the registered customer, if the entered data is a valid address
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task InsertDefaultAddressAsync(Customer customer)
    {
        var defaultAddress = new Address
        {
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            Email = customer.Email,
            Company = customer.Company,
            CountryId = customer.CountryId > 0 ? customer.CountryId : null,
            StateProvinceId = customer.StateProvinceId > 0 ? customer.StateProvinceId : null,
            County = customer.County,
            City = customer.City,
            Address1 = customer.StreetAddress,
            Address2 = customer.StreetAddress2,
            ZipPostalCode = customer.ZipPostalCode,
            PhoneNumber = customer.Phone,
            FaxNumber = customer.Fax,
            CreatedOnUtc = customer.CreatedOnUtc
        };
        if (!await _addressService.IsAddressValidAsync(defaultAddress))
            return;

        await _addressService.InsertAddressAsync(defaultAddress);
        await _customerService.InsertCustomerAddressAsync(customer, defaultAddress);

        customer.BillingAddressId = defaultAddress.Id;
        customer.ShippingAddressId = defaultAddress.Id;
        await _customerService.UpdateCustomerAsync(customer);
    }

    /// <summary>
    /// Save the newsletter subscriptions selected during the registration
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="model">Registration model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task SaveRegistrationNewsletterSubscriptionsAsync(Customer customer, RegisterModel model)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();

        var anyNewSubscriptions = false;
        var isNewsletterActive = _customerSettings.UserRegistrationType != UserRegistrationType.EmailValidation;
        var activeSubscriptions = model.NewsLetterSubscriptions?.Where(subscriptionModel => subscriptionModel.IsActive).ToList() ?? new List<NewsLetterSubscriptionModel>();
        var currentSubscriptions = await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByEmailAsync(customer.Email, storeId: store.Id);
        var subscriptionGuid = currentSubscriptions.FirstOrDefault()?.NewsLetterSubscriptionGuid ?? Guid.NewGuid();

        foreach (var activeSubscription in activeSubscriptions)
        {
            var existingSubscription = currentSubscriptions.FirstOrDefault(subscription => subscription.TypeId == activeSubscription.TypeId);
            if (existingSubscription is not null)
            {
                if (!existingSubscription.Active && isNewsletterActive)
                {
                    existingSubscription.Active = true;
                    existingSubscription.LanguageId = customer.LanguageId ?? language.Id;
                    await _newsLetterSubscriptionService.UpdateNewsLetterSubscriptionAsync(existingSubscription);
                }

                continue;
            }

            await _newsLetterSubscriptionService.InsertNewsLetterSubscriptionAsync(new NewsLetterSubscription
            {
                NewsLetterSubscriptionGuid = subscriptionGuid,
                Email = customer.Email,
                Active = isNewsletterActive,
                TypeId = activeSubscription.TypeId,
                StoreId = store.Id,
                LanguageId = customer.LanguageId ?? language.Id,
                CreatedOnUtc = DateTime.UtcNow
            });
            anyNewSubscriptions = true;
        }

        //GDPR
        if (anyNewSubscriptions && _gdprSettings.GdprEnabled && _gdprSettings.LogNewsletterConsent)
            await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ConsentAgree, await _localizationService.GetResourceAsync("Gdpr.Consent.Newsletter"));
    }

    /// <summary>
    /// Save the newsletter subscriptions selected on the customer info page
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="model">Customer info model</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task SaveCustomerInfoNewsletterSubscriptionsAsync(Customer customer, CustomerInfoModel model)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();
        var subscriptionModels = model.NewsLetterSubscriptions ?? new List<NewsLetterSubscriptionModel>();

        var currentSubscriptions = await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByEmailAsync(customer.Email, storeId: store.Id);
        var subscriptionGuid = currentSubscriptions.FirstOrDefault()?.NewsLetterSubscriptionGuid ?? Guid.NewGuid();

        foreach (var subscriptionModel in subscriptionModels)
        {
            var existingSubscription = currentSubscriptions.FirstOrDefault(subscription => subscription.TypeId == subscriptionModel.TypeId);
            if (existingSubscription is not null && existingSubscription.Active != subscriptionModel.IsActive)
            {
                existingSubscription.Active = subscriptionModel.IsActive;
                await _newsLetterSubscriptionService.UpdateNewsLetterSubscriptionAsync(existingSubscription);
            }

            if (existingSubscription is null && subscriptionModel.IsActive)
            {
                await _newsLetterSubscriptionService.InsertNewsLetterSubscriptionAsync(new NewsLetterSubscription
                {
                    NewsLetterSubscriptionGuid = subscriptionGuid,
                    Email = customer.Email,
                    Active = true,
                    TypeId = subscriptionModel.TypeId,
                    StoreId = store.Id,
                    LanguageId = customer.LanguageId ?? language.Id,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>
    /// Log the GDPR consents and profile changes of the customer info page
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="oldCustomerInfoModel">Customer info before the changes</param>
    /// <param name="newCustomerInfoModel">Changed customer info</param>
    /// <param name="form">Form</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task LogGdprAsync(Customer customer, CustomerInfoModel oldCustomerInfoModel, CustomerInfoModel newCustomerInfoModel, IFormCollection form)
    {
        try
        {
            //consents
            var consents = (await _gdprService.GetAllConsentsAsync()).Where(consent => consent.DisplayOnCustomerInfoPage).ToList();
            foreach (var consent in consents)
            {
                var previousConsentValue = await _gdprService.IsConsentAcceptedAsync(consent.Id, customer.Id);
                if (IsConsentAccepted(form, consent))
                {
                    //agree
                    if (!previousConsentValue.HasValue || !previousConsentValue.Value)
                        await _gdprService.InsertLogAsync(customer, consent.Id, GdprRequestType.ConsentAgree, consent.Message);
                }
                else
                {
                    //disagree
                    if (!previousConsentValue.HasValue || previousConsentValue.Value)
                        await _gdprService.InsertLogAsync(customer, consent.Id, GdprRequestType.ConsentDisagree, consent.Message);
                }
            }

            //newsletter subscriptions
            if (_gdprSettings.LogNewsletterConsent)
            {
                var oldNewsletter = oldCustomerInfoModel.NewsLetterSubscriptions?.Any(subscriptionModel => subscriptionModel.IsActive) ?? false;
                var newNewsletter = newCustomerInfoModel.NewsLetterSubscriptions?.Any(subscriptionModel => subscriptionModel.IsActive) ?? false;
                if (oldNewsletter && !newNewsletter)
                    await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ConsentDisagree, await _localizationService.GetResourceAsync("Gdpr.Consent.Newsletter"));
                if (!oldNewsletter && newNewsletter)
                    await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ConsentAgree, await _localizationService.GetResourceAsync("Gdpr.Consent.Newsletter"));
            }

            //user profile changes
            if (!_gdprSettings.LogUserProfileChanges)
                return;

            async Task logChangeAsync(string resourceKey, object value) =>
                await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ProfileChanged, $"{await _localizationService.GetResourceAsync(resourceKey)} = {value}");

            if (oldCustomerInfoModel.Gender != newCustomerInfoModel.Gender)
                await logChangeAsync("Account.Fields.Gender", newCustomerInfoModel.Gender);

            if (oldCustomerInfoModel.FirstName != newCustomerInfoModel.FirstName)
                await logChangeAsync("Account.Fields.FirstName", newCustomerInfoModel.FirstName);

            if (oldCustomerInfoModel.LastName != newCustomerInfoModel.LastName)
                await logChangeAsync("Account.Fields.LastName", newCustomerInfoModel.LastName);

            if (oldCustomerInfoModel.ParseDateOfBirth() != newCustomerInfoModel.ParseDateOfBirth())
                await logChangeAsync("Account.Fields.DateOfBirth", newCustomerInfoModel.ParseDateOfBirth());

            if (oldCustomerInfoModel.Email != newCustomerInfoModel.Email)
                await logChangeAsync("Account.Fields.Email", newCustomerInfoModel.Email);

            if (oldCustomerInfoModel.Company != newCustomerInfoModel.Company)
                await logChangeAsync("Account.Fields.Company", newCustomerInfoModel.Company);

            if (oldCustomerInfoModel.StreetAddress != newCustomerInfoModel.StreetAddress)
                await logChangeAsync("Account.Fields.StreetAddress", newCustomerInfoModel.StreetAddress);

            if (oldCustomerInfoModel.StreetAddress2 != newCustomerInfoModel.StreetAddress2)
                await logChangeAsync("Account.Fields.StreetAddress2", newCustomerInfoModel.StreetAddress2);

            if (oldCustomerInfoModel.ZipPostalCode != newCustomerInfoModel.ZipPostalCode)
                await logChangeAsync("Account.Fields.ZipPostalCode", newCustomerInfoModel.ZipPostalCode);

            if (oldCustomerInfoModel.City != newCustomerInfoModel.City)
                await logChangeAsync("Account.Fields.City", newCustomerInfoModel.City);

            if (oldCustomerInfoModel.County != newCustomerInfoModel.County)
                await logChangeAsync("Account.Fields.County", newCustomerInfoModel.County);

            if (oldCustomerInfoModel.CountryId != newCustomerInfoModel.CountryId)
                await logChangeAsync("Account.Fields.Country", (await _countryService.GetCountryByIdAsync(newCustomerInfoModel.CountryId))?.Name);

            if (oldCustomerInfoModel.StateProvinceId != newCustomerInfoModel.StateProvinceId)
                await logChangeAsync("Account.Fields.StateProvince", (await _stateProvinceService.GetStateProvinceByIdAsync(newCustomerInfoModel.StateProvinceId))?.Name);
        }
        catch (Exception exception)
        {
            await _logger.ErrorAsync(exception.Message, exception, customer);
        }
    }

    /// <summary>
    /// Gets the customer of an email link; for backward compatibility links of previous versions contain the email instead of the GUID
    /// </summary>
    /// <param name="email">Email</param>
    /// <param name="guid">Customer GUID</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the customer or null if not found</returns>
    protected virtual async Task<Customer> GetCustomerByLinkAsync(string email, Guid guid)
    {
        return (string.IsNullOrWhiteSpace(email) ? null : await _customerService.GetCustomerByEmailAsync(email))
            ?? await _customerService.GetCustomerByGuidAsync(guid);
    }

    /// <summary>
    /// Validate the custom address attributes; custom attributes are not supported by the API yet, so only required ones cause errors
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task ValidateCustomAddressAttributesAsync()
    {
        foreach (var error in await _addressAttributeParser.GetAttributeWarningsAsync(string.Empty))
            ModelState.AddModelError(string.Empty, error);
    }

    #endregion

    #region Registration

    /// <summary>
    /// Gets the registration form (enabled and required fields, available countries, custom attributes, GDPR consents, newsletters, etc.)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the registration form</returns>
    [HttpGet("register")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(RegisterModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> RegisterForm()
    {
        if (_customerSettings.UserRegistrationType == UserRegistrationType.Disabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Account.Register.Result.Disabled"));

        return Ok(await _customerModelFactory.PrepareRegisterModelAsync(new RegisterModel(), false, setDefaultValues: true));
    }

    /// <summary>
    /// Registers the current guest customer; the shopping cart and wishlist are kept
    /// </summary>
    /// <param name="model">Registration data, custom customer attributes and GDPR consents</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the registration result</returns>
    [HttpPost("register")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(RegistrationResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Register(RegisterRequest model)
    {
        if (_customerSettings.UserRegistrationType == UserRegistrationType.Disabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Account.Register.Result.Disabled"));

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsRegisteredAsync(customer))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.AlreadyRegistered")]);

        await ValidateAsync<RegisterModel>(_registerValidator, model);

        //custom customer attributes
        var form = PrepareForm(model.Attributes, model.GdprConsents);
        var customerAttributesXml = await ParseCustomCustomerAttributesAsync(form);
        foreach (var error in await _customerAttributeParser.GetAttributeWarningsAsync(customerAttributesXml))
            ModelState.AddModelError(string.Empty, error);

        //check is verified phone number
        var phoneNumber = FormatPhoneNumber(model.Phone);
        await ValidatePhoneNumberAsync(customer, phoneNumber);

        //GDPR
        if (_gdprSettings.GdprEnabled)
        {
            var requiredConsents = (await _gdprService.GetAllConsentsAsync())
                .Where(consent => consent.DisplayDuringRegistration && consent.IsRequired)
                .ToList();
            ValidateRequiredConsents(requiredConsents, form);
        }

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var store = await _storeContext.GetCurrentStoreAsync();
        var language = await _workContext.GetWorkingLanguageAsync();
        customer.RegisteredInStoreId = store.Id;

        var registrationRequest = new CustomerRegistrationRequest(customer,
            model.Email,
            _customerSettings.UsernamesEnabled ? model.Username : model.Email,
            model.Password,
            _customerSettings.DefaultPasswordFormat,
            store.Id,
            _customerSettings.UserRegistrationType == UserRegistrationType.Standard);
        var registrationResult = await _customerRegistrationService.RegisterCustomerAsync(registrationRequest);
        if (!registrationResult.Success)
            return WarningsProblem(registrationResult.Errors);

        //properties
        if (_dateTimeSettings.AllowCustomersToSetTimeZone)
            customer.TimeZoneId = model.TimeZoneId;

        //VAT number
        if (_taxSettings.EuVatEnabled)
        {
            customer.VatNumber = model.VatNumber;

            var (vatNumberStatus, _, vatAddress) = await _taxService.GetVatNumberStatusAsync(model.VatNumber);
            customer.VatNumberStatusId = (int)vatNumberStatus;

            //send VAT number admin notification
            if (!string.IsNullOrEmpty(model.VatNumber) && _taxSettings.EuVatEmailAdminWhenNewVatSubmitted)
                await _workflowMessageService.SendNewVatSubmittedStoreOwnerNotificationAsync(customer, model.VatNumber, vatAddress, _localizationSettings.DefaultAdminLanguageId);
        }

        //form fields and custom attributes
        SetCustomerFields(customer, model, phoneNumber);
        customer.CustomCustomerAttributesXML = customerAttributesXml;
        await _customerService.UpdateCustomerAsync(customer);

        //newsletter subscriptions
        if (_customerSettings.NewsletterEnabled)
            await SaveRegistrationNewsletterSubscriptionsAsync(customer, model);

        //GDPR
        if (_customerSettings.AcceptPrivacyPolicyEnabled && _gdprSettings.GdprEnabled && _gdprSettings.LogPrivacyPolicyConsent)
        {
            await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ConsentAgree,
                await _localizationService.GetResourceAsync("Gdpr.Consent.PrivacyPolicy"));
        }

        if (_gdprSettings.GdprEnabled)
        {
            var consents = (await _gdprService.GetAllConsentsAsync()).Where(consent => consent.DisplayDuringRegistration).ToList();
            foreach (var consent in consents)
            {
                var requestType = IsConsentAccepted(form, consent) ? GdprRequestType.ConsentAgree : GdprRequestType.ConsentDisagree;
                await _gdprService.InsertLogAsync(customer, consent.Id, requestType, consent.Message);
            }
        }

        await InsertDefaultAddressAsync(customer);

        //notifications
        if (_customerSettings.NotifyNewCustomerRegistration)
            await _workflowMessageService.SendCustomerRegisteredStoreOwnerNotificationMessageAsync(customer, _localizationSettings.DefaultAdminLanguageId);

        //raise event
        await _eventPublisher.PublishAsync(new CustomerRegisteredEvent(customer));

        var result = new RegistrationResultModel { RegistrationType = _customerSettings.UserRegistrationType.ToString() };
        switch (_customerSettings.UserRegistrationType)
        {
            case UserRegistrationType.EmailValidation:
                //email validation message
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.AccountActivationTokenAttribute, Guid.NewGuid().ToString());
                await _workflowMessageService.SendCustomerEmailValidationMessageAsync(customer, language.Id);
                result.Message = await _localizationService.GetResourceAsync("Account.Register.Result.EmailValidation");
                break;

            case UserRegistrationType.AdminApproval:
                result.Message = await _localizationService.GetResourceAsync("Account.Register.Result.AdminApproval");
                break;

            case UserRegistrationType.Standard:
                //send customer welcome message
                await _workflowMessageService.SendCustomerWelcomeMessageAsync(customer, language.Id);

                //raise event
                await _eventPublisher.PublishAsync(new CustomerActivatedEvent(customer));

                await _customerRegistrationService.SignInCustomerAsync(customer, null, true);
                result.Message = await _localizationService.GetResourceAsync("Account.Register.Result.Standard");
                result.Token = await _headlessApiTokenService.PrepareTokenModelAsync(customer);
                break;
        }

        return Ok(result);
    }

    /// <summary>
    /// Checks whether a username is available
    /// </summary>
    /// <param name="username">Username</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the availability and a status text</returns>
    [HttpGet("check-username")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(UsernameAvailabilityModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> CheckUsernameAvailability([FromQuery] string username)
    {
        var result = new UsernameAvailabilityModel
        {
            Text = await _localizationService.GetResourceAsync("Account.CheckUsernameAvailability.NotAvailable")
        };

        if (!UsernamePropertyValidator<string, string>.IsValid(username, _customerSettings))
            result.Text = await _localizationService.GetResourceAsync("Account.Fields.Username.NotValid");
        else if (_customerSettings.UsernamesEnabled && !string.IsNullOrWhiteSpace(username))
        {
            var currentCustomer = await _workContext.GetCurrentCustomerAsync();
            if (currentCustomer?.Username?.Equals(username, StringComparison.InvariantCultureIgnoreCase) == true)
                result.Text = await _localizationService.GetResourceAsync("Account.CheckUsernameAvailability.CurrentUsername");
            else if (await _customerService.GetCustomerByUsernameAsync(username) == null)
            {
                result.Text = await _localizationService.GetResourceAsync("Account.CheckUsernameAvailability.Available");
                result.Available = true;
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Activates an account (registration type "Email validation"); the frontend page of the activation email link calls this endpoint
    /// </summary>
    /// <param name="request">Values of the activation link</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the message and the token of the activated (and logged in) customer</returns>
    [HttpPost("activation")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(AccountResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> AccountActivation(AccountTokenRequest request)
    {
        var customer = await GetCustomerByLinkAsync(request.Email, request.Guid);
        if (customer == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.InvalidLink")]);

        var cToken = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.AccountActivationTokenAttribute);
        if (string.IsNullOrEmpty(cToken))
            return Ok(new AccountResultModel { Message = await _localizationService.GetResourceAsync("Account.AccountActivation.AlreadyActivated") });

        if (!cToken.Equals(request.Token, StringComparison.InvariantCultureIgnoreCase))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.InvalidLink")]);

        //activate user account
        customer.Active = true;
        await _customerService.UpdateCustomerAsync(customer);
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.AccountActivationTokenAttribute, string.Empty);

        //send welcome message
        await _workflowMessageService.SendCustomerWelcomeMessageAsync(customer, (await _workContext.GetWorkingLanguageAsync()).Id);

        //raise event
        await _eventPublisher.PublishAsync(new CustomerActivatedEvent(customer));

        //authenticate customer after activation
        await _customerRegistrationService.SignInCustomerAsync(customer, null, true);

        //activating newsletter subscriptions
        var store = await _storeContext.GetCurrentStoreAsync();
        var subscriptions = await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByEmailAsync(customer.Email, storeId: store.Id, isActive: false);
        foreach (var subscription in subscriptions)
        {
            subscription.Active = true;
            await _newsLetterSubscriptionService.UpdateNewsLetterSubscriptionAsync(subscription);
        }

        return Ok(new AccountResultModel
        {
            Message = await _localizationService.GetResourceAsync("Account.AccountActivation.Activated"),
            Token = await _headlessApiTokenService.PrepareTokenModelAsync(customer)
        });
    }

    #endregion

    #region Password recovery

    /// <summary>
    /// Sends the password recovery email; always succeeds, so it can't be used to find out whether an account exists
    /// </summary>
    /// <param name="model">Password recovery data</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    [HttpPost("password-recovery")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> PasswordRecovery(PasswordRecoveryModel model)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var customer = await _customerService.GetCustomerByEmailAsync(model.Email);
        if (customer is { Active: true, Deleted: false })
        {
            //save token and current date
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.PasswordRecoveryTokenAttribute, Guid.NewGuid().ToString());
            await _genericAttributeService.SaveAttributeAsync<DateTime?>(customer, NopCustomerDefaults.PasswordRecoveryTokenDateGeneratedAttribute, DateTime.UtcNow);

            //send email
            await _workflowMessageService.SendCustomerPasswordRecoveryMessageAsync(customer, (await _workContext.GetWorkingLanguageAsync()).Id);
        }

        return NoContent();
    }

    /// <summary>
    /// Checks the values of a password recovery link before the new password form is displayed
    /// </summary>
    /// <param name="request">Values of the password recovery link</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form state ("disablePasswordChanging" and "result" are set if the link can't be used)</returns>
    [HttpPost("password-recovery/check")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(PasswordRecoveryConfirmModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> PasswordRecoveryCheck(AccountTokenRequest request)
    {
        var customer = await GetCustomerByLinkAsync(request.Email, request.Guid);
        if (customer == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Account.PasswordRecovery.WrongToken")]);

        var model = new PasswordRecoveryConfirmModel();
        if (string.IsNullOrEmpty(await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.PasswordRecoveryTokenAttribute)))
        {
            model.DisablePasswordChanging = true;
            model.Result = await _localizationService.GetResourceAsync("Account.PasswordRecovery.PasswordAlreadyHasBeenChanged");
        }
        else if (!await _customerService.IsPasswordRecoveryTokenValidAsync(customer, request.Token))
        {
            model.DisablePasswordChanging = true;
            model.Result = await _localizationService.GetResourceAsync("Account.PasswordRecovery.WrongToken");
        }
        else if (await _customerService.IsPasswordRecoveryLinkExpiredAsync(customer))
        {
            model.DisablePasswordChanging = true;
            model.Result = await _localizationService.GetResourceAsync("Account.PasswordRecovery.LinkExpired");
        }

        return Ok(model);
    }

    /// <summary>
    /// Sets a new password from the password recovery link; the customer is logged in and all its other tokens are revoked
    /// </summary>
    /// <param name="model">Values of the password recovery link and the new password</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the message and the token of the logged in customer</returns>
    [HttpPost("password-recovery/confirm")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(AccountResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> PasswordRecoveryConfirm(PasswordRecoveryConfirmRequest model)
    {
        var customer = await GetCustomerByLinkAsync(model.Email, model.Guid);
        if (customer == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Account.PasswordRecovery.WrongToken")]);

        //validate token
        if (!await _customerService.IsPasswordRecoveryTokenValidAsync(customer, model.Token))
            return WarningsProblem([await _localizationService.GetResourceAsync("Account.PasswordRecovery.WrongToken")]);

        //validate token expiration date
        if (await _customerService.IsPasswordRecoveryLinkExpiredAsync(customer))
            return WarningsProblem([await _localizationService.GetResourceAsync("Account.PasswordRecovery.LinkExpired")]);

        await ValidateAsync<PasswordRecoveryConfirmModel>(_passwordRecoveryConfirmValidator, model);
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var response = await _customerRegistrationService
            .ChangePasswordAsync(new ChangePasswordRequest(customer.Email, false, _customerSettings.DefaultPasswordFormat, model.NewPassword));
        if (!response.Success)
            return WarningsProblem(response.Errors);

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.PasswordRecoveryTokenAttribute, string.Empty);

        await _customerActivityService.InsertActivityAsync(customer, "PublicStore.PasswordChanged",
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.PasswordChanged"));

        //log out other devices and authenticate customer after changing password
        await _headlessApiTokenService.RevokeTokensAsync(customer);
        await _customerRegistrationService.SignInCustomerAsync(customer, null, true);

        return Ok(new AccountResultModel
        {
            Message = await _localizationService.GetResourceAsync("Account.PasswordRecovery.PasswordHasBeenChanged"),
            Token = await _headlessApiTokenService.PrepareTokenModelAsync(customer)
        });
    }

    #endregion

    #region Customer info and password

    /// <summary>
    /// Gets the account navigation (available tabs of "My account" depending on the settings)
    /// </summary>
    /// <param name="selectedTabId">Identifier of the selected tab</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the navigation model</returns>
    [HttpGet("navigation")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerNavigationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Navigation([FromQuery] int selectedTabId = 0)
    {
        return Ok(await _customerModelFactory.PrepareCustomerNavigationModelAsync(selectedTabId));
    }

    /// <summary>
    /// Gets the customer info of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the customer info</returns>
    [HttpGet("info")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Info()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();

        return Ok(await _customerModelFactory.PrepareCustomerInfoModelAsync(new CustomerInfoModel(), customer, false));
    }

    /// <summary>
    /// Updates the customer info of the current customer
    /// </summary>
    /// <param name="model">Customer info, custom customer attributes and GDPR consents</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated customer info</returns>
    /// <remarks>
    /// If the email is changed and the registration type is "Email validation", the new email has to be confirmed first
    /// (see "emailToRevalidate"); the email link calls POST account/email-revalidation.
    /// If login by phone is enabled and the phone number has changed, "phoneSmsVerified" is reset.
    /// </remarks>
    [HttpPut("info")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> UpdateInfo(CustomerInfoRequest model)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var oldCustomerModel = new CustomerInfoModel();

        //get customer info model before changes for gdpr log
        if (_gdprSettings.GdprEnabled && _gdprSettings.LogUserProfileChanges)
            oldCustomerModel = await _customerModelFactory.PrepareCustomerInfoModelAsync(oldCustomerModel, customer, false);

        await ValidateAsync<CustomerInfoModel>(_customerInfoValidator, model);

        //custom customer attributes
        var form = PrepareForm(model.Attributes, model.GdprConsents);
        var customerAttributesXml = await ParseCustomCustomerAttributesAsync(form);
        foreach (var error in await _customerAttributeParser.GetAttributeWarningsAsync(customerAttributesXml))
            ModelState.AddModelError(string.Empty, error);

        //GDPR
        if (_gdprSettings.GdprEnabled)
        {
            var requiredConsents = (await _gdprService.GetAllConsentsAsync())
                .Where(consent => consent.DisplayOnCustomerInfoPage && consent.IsRequired)
                .ToList();
            ValidateRequiredConsents(requiredConsents, form);
        }

        //check is verified phone number
        var phoneNumber = FormatPhoneNumber(model.Phone);
        await ValidatePhoneNumberAsync(customer, phoneNumber);

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        try
        {
            //username
            if (_customerSettings.UsernamesEnabled && _customerSettings.AllowUsersToChangeUsernames
                && !string.Equals(customer.Username, model.Username, StringComparison.InvariantCultureIgnoreCase))
            {
                await _customerRegistrationService.SetUsernameAsync(customer, model.Username);
            }

            //email
            if (!string.Equals(customer.Email, model.Email, StringComparison.InvariantCultureIgnoreCase))
            {
                var requireValidation = _customerSettings.UserRegistrationType == UserRegistrationType.EmailValidation;
                await _customerRegistrationService.SetEmailAsync(customer, model.Email, requireValidation);
            }

            //properties
            if (_dateTimeSettings.AllowCustomersToSetTimeZone)
                customer.TimeZoneId = model.TimeZoneId;

            //VAT number
            if (_taxSettings.EuVatEnabled)
            {
                var prevVatNumber = customer.VatNumber;
                customer.VatNumber = model.VatNumber;

                if (prevVatNumber != model.VatNumber)
                {
                    var (vatNumberStatus, _, vatAddress) = await _taxService.GetVatNumberStatusAsync(model.VatNumber);
                    customer.VatNumberStatusId = (int)vatNumberStatus;

                    //send VAT number admin notification
                    if (!string.IsNullOrEmpty(model.VatNumber) && _taxSettings.EuVatEmailAdminWhenNewVatSubmitted)
                        await _workflowMessageService.SendNewVatSubmittedStoreOwnerNotificationAsync(customer, model.VatNumber, vatAddress, _localizationSettings.DefaultAdminLanguageId);
                }
            }

            //form fields
            if (_customerSettings.GenderEnabled)
                customer.Gender = model.Gender;
            if (_customerSettings.FirstNameEnabled)
                customer.FirstName = model.FirstName;
            if (_customerSettings.LastNameEnabled)
                customer.LastName = model.LastName;
            if (_customerSettings.DateOfBirthEnabled)
                customer.DateOfBirth = model.ParseDateOfBirth();
            if (_customerSettings.CompanyEnabled)
                customer.Company = model.Company;
            if (_customerSettings.StreetAddressEnabled)
                customer.StreetAddress = model.StreetAddress;
            if (_customerSettings.StreetAddress2Enabled)
                customer.StreetAddress2 = model.StreetAddress2;
            if (_customerSettings.ZipPostalCodeEnabled)
                customer.ZipPostalCode = model.ZipPostalCode;
            if (_customerSettings.CityEnabled)
                customer.City = model.City;
            if (_customerSettings.CountyEnabled)
                customer.County = model.County;
            if (_customerSettings.CountryEnabled)
                customer.CountryId = model.CountryId;
            if (_customerSettings.CountryEnabled && _customerSettings.StateProvinceEnabled)
                customer.StateProvinceId = model.StateProvinceId;
            if (_customerSettings.PhoneEnabled)
            {
                if (_otpSettings.LoginByPhoneEnabled)
                    customer.PhoneSmsVerified = customer.PhoneSmsVerified && customer.Phone == phoneNumber;
                customer.Phone = phoneNumber;
            }
            if (_customerSettings.FaxEnabled)
                customer.Fax = model.Fax;

            customer.CustomCustomerAttributesXML = customerAttributesXml;
            await _customerService.UpdateCustomerAsync(customer);

            //newsletter subscriptions
            if (_customerSettings.NewsletterEnabled)
                await SaveCustomerInfoNewsletterSubscriptionsAsync(customer, model);

            //GDPR
            if (_gdprSettings.GdprEnabled)
                await LogGdprAsync(customer, oldCustomerModel, model, form);
        }
        catch (Exception exception)
        {
            //e.g. the email or username already exists
            return WarningsProblem([exception.Message]);
        }

        return Ok(await _customerModelFactory.PrepareCustomerInfoModelAsync(new CustomerInfoModel(), customer, false));
    }

    /// <summary>
    /// Confirms a changed email address (registration type "Email validation"); the frontend page of the email revalidation link calls this endpoint
    /// </summary>
    /// <param name="request">Values of the email revalidation link</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the message and the token of the (logged in) customer</returns>
    [HttpPost("email-revalidation")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(AccountResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> EmailRevalidation(AccountTokenRequest request)
    {
        var customer = await GetCustomerByLinkAsync(request.Email, request.Guid);
        if (customer == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.InvalidLink")]);

        var cToken = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.EmailRevalidationTokenAttribute);
        if (string.IsNullOrEmpty(cToken))
            return Ok(new AccountResultModel { Message = await _localizationService.GetResourceAsync("Account.EmailRevalidation.AlreadyChanged") });

        if (!cToken.Equals(request.Token, StringComparison.InvariantCultureIgnoreCase)
            || string.IsNullOrEmpty(customer.EmailToRevalidate)
            || _customerSettings.UserRegistrationType != UserRegistrationType.EmailValidation)
        {
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.InvalidLink")]);
        }

        //change email
        try
        {
            await _customerRegistrationService.SetEmailAsync(customer, customer.EmailToRevalidate, false);
        }
        catch (Exception exception)
        {
            return WarningsProblem([await _localizationService.GetResourceAsync(exception.Message)]);
        }

        customer.EmailToRevalidate = null;
        await _customerService.UpdateCustomerAsync(customer);
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.EmailRevalidationTokenAttribute, string.Empty);

        //authenticate customer after changing email
        await _customerRegistrationService.SignInCustomerAsync(customer, null, true);

        return Ok(new AccountResultModel
        {
            Message = await _localizationService.GetResourceAsync("Account.EmailRevalidation.Changed"),
            Token = await _headlessApiTokenService.PrepareTokenModelAsync(customer)
        });
    }

    /// <summary>
    /// Changes the password of the current customer; all other tokens of the customer are revoked
    /// </summary>
    /// <param name="model">Old and new password</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains a new token</returns>
    [HttpPost("change-password")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(TokenModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ChangePassword(ChangePasswordModel model)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var changePasswordRequest = new ChangePasswordRequest(customer.Email, true, _customerSettings.DefaultPasswordFormat, model.NewPassword, model.OldPassword);
        var changePasswordResult = await _customerRegistrationService.ChangePasswordAsync(changePasswordRequest);
        if (!changePasswordResult.Success)
            return WarningsProblem(changePasswordResult.Errors);

        await _customerActivityService.InsertActivityAsync(customer, "PublicStore.PasswordChanged",
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.PasswordChanged"));

        //log out other devices and issue a new token for the current one
        await _headlessApiTokenService.RevokeTokensAsync(customer);
        await _customerRegistrationService.SignInCustomerAsync(customer, null, true);

        return Ok(await _headlessApiTokenService.PrepareTokenModelAsync(customer));
    }

    #endregion

    #region Addresses

    /// <summary>
    /// Gets the addresses of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the addresses</returns>
    [HttpGet("addresses")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAddressListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Addresses()
    {
        return Ok(await _customerModelFactory.PrepareCustomerAddressListModelAsync());
    }

    /// <summary>
    /// Adds an address to the current customer
    /// </summary>
    /// <param name="model">Address</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated addresses</returns>
    [HttpPost("addresses")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAddressListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> AddAddress(AddressModel model)
    {
        await ValidateCustomAddressAttributesAsync();
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var address = model.ToEntity();
        address.Id = 0;
        address.CreatedOnUtc = DateTime.UtcNow;
        if (address.CountryId == 0)
            address.CountryId = null;
        if (address.StateProvinceId == 0)
            address.StateProvinceId = null;

        await _addressService.InsertAddressAsync(address);
        await _customerService.InsertCustomerAddressAsync(await _workContext.GetCurrentCustomerAsync(), address);

        return Ok(await _customerModelFactory.PrepareCustomerAddressListModelAsync());
    }

    /// <summary>
    /// Updates an address of the current customer
    /// </summary>
    /// <param name="addressId">Address identifier</param>
    /// <param name="model">Address</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated addresses</returns>
    [HttpPut("addresses/{addressId:int}")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAddressListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UpdateAddress(int addressId, AddressModel model)
    {
        //ensure that the address belongs to the current customer
        var customer = await _workContext.GetCurrentCustomerAsync();
        var address = await _customerService.GetCustomerAddressAsync(customer.Id, addressId);
        if (address == null)
            return NotFoundProblem();

        await ValidateCustomAddressAttributesAsync();
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        model.Id = address.Id;
        address = model.ToEntity(address);
        if (address.CountryId == 0)
            address.CountryId = null;
        if (address.StateProvinceId == 0)
            address.StateProvinceId = null;
        await _addressService.UpdateAddressAsync(address);

        return Ok(await _customerModelFactory.PrepareCustomerAddressListModelAsync());
    }

    /// <summary>
    /// Deletes an address of the current customer
    /// </summary>
    /// <param name="addressId">Address identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated addresses</returns>
    [HttpDelete("addresses/{addressId:int}")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAddressListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> DeleteAddress(int addressId)
    {
        //ensure that the address belongs to the current customer
        var customer = await _workContext.GetCurrentCustomerAsync();
        var address = await _customerService.GetCustomerAddressAsync(customer.Id, addressId);
        if (address == null)
            return NotFoundProblem();

        await _customerService.RemoveCustomerAddressAsync(customer, address);
        await _customerService.UpdateCustomerAsync(customer);
        await _addressService.DeleteAddressAsync(address);

        return Ok(await _customerModelFactory.PrepareCustomerAddressListModelAsync());
    }

    #endregion

    #region Orders

    /// <summary>
    /// Gets the orders of the current customer
    /// </summary>
    /// <param name="page">Page number</param>
    /// <param name="period">Period (All, Day, Week, Month, HalfYear, Year)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the orders</returns>
    [HttpGet("orders")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerOrderListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Orders([FromQuery] int? page = null, [FromQuery] OrderHistoryPeriods period = OrderHistoryPeriods.All)
    {
        return Ok(await _orderModelFactory.PrepareCustomerOrderListModelAsync(page, period));
    }

    /// <summary>
    /// Gets the details of an order of the current customer
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the order details</returns>
    [HttpGet("orders/{orderId:int}")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(OrderDetailsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> OrderDetails(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || order.Deleted || order.CustomerId != customer.Id)
            return NotFoundProblem();

        return Ok(await _orderModelFactory.PrepareOrderDetailsModelAsync(order));
    }

    #endregion
}

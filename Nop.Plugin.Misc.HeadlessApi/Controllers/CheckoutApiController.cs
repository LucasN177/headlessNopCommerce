using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Plugin.Misc.HeadlessApi.Models.Checkout;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Shipping;
using Nop.Services.Tax;
using Nop.Web.Factories;
using Nop.Web.Models.Checkout;
using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the checkout endpoints
/// </summary>
/// <remarks>
/// The flow corresponds to the one page checkout of the storefront:
/// POST checkout/start → PUT billing-address → (PUT shipping-address) → (PUT shipping-method) → (PUT payment-method) → (PUT payment-info) → POST confirm.
/// Each step returns the next step and its model; steps that are not required are skipped.
/// The checkout data is stored at the customer, so the steps can be called in separate requests.
/// </remarks>
[HeadlessApiRoute("checkout")]
public class CheckoutApiController : BaseHeadlessApiController
{
    #region Constants

    private const string STEP_BILLING_ADDRESS = "billing_address";
    private const string STEP_SHIPPING_ADDRESS = "shipping_address";
    private const string STEP_SHIPPING_METHOD = "shipping_method";
    private const string STEP_PAYMENT_METHOD = "payment_method";
    private const string STEP_PAYMENT_INFO = "payment_info";
    private const string STEP_CONFIRM = "confirm";

    private static readonly string[] _separator = ["___"];

    #endregion

    #region Fields

    protected readonly AddressSettings _addressSettings;
    protected readonly CustomerSettings _customerSettings;
    protected readonly HeadlessApiFrontendUrlService _headlessApiFrontendUrlService;
    protected readonly IAddressService _addressService;
    protected readonly IAttributeParser<AddressAttribute, AddressAttributeValue> _addressAttributeParser;
    protected readonly ICheckoutModelFactory _checkoutModelFactory;
    protected readonly ICountryService _countryService;
    protected readonly ICustomerService _customerService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly ILogger _logger;
    protected readonly IOrderProcessingService _orderProcessingService;
    protected readonly IOrderService _orderService;
    protected readonly IPaymentPluginManager _paymentPluginManager;
    protected readonly IPaymentService _paymentService;
    protected readonly IProductService _productService;
    protected readonly IShippingService _shippingService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly ITaxService _taxService;
    protected readonly IValidator<AddressModel> _addressValidator;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;
    protected readonly OrderSettings _orderSettings;
    protected readonly PaymentSettings _paymentSettings;
    protected readonly RewardPointsSettings _rewardPointsSettings;
    protected readonly ShippingSettings _shippingSettings;
    protected readonly TaxSettings _taxSettings;

    #endregion

    #region Ctor

    public CheckoutApiController(AddressSettings addressSettings,
        CustomerSettings customerSettings,
        HeadlessApiFrontendUrlService headlessApiFrontendUrlService,
        IAddressService addressService,
        IAttributeParser<AddressAttribute, AddressAttributeValue> addressAttributeParser,
        ICheckoutModelFactory checkoutModelFactory,
        ICountryService countryService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        ILogger logger,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IPaymentPluginManager paymentPluginManager,
        IPaymentService paymentService,
        IProductService productService,
        IShippingService shippingService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        ITaxService taxService,
        IValidator<AddressModel> addressValidator,
        IWebHelper webHelper,
        IWorkContext workContext,
        OrderSettings orderSettings,
        PaymentSettings paymentSettings,
        RewardPointsSettings rewardPointsSettings,
        ShippingSettings shippingSettings,
        TaxSettings taxSettings)
    {
        _addressSettings = addressSettings;
        _customerSettings = customerSettings;
        _headlessApiFrontendUrlService = headlessApiFrontendUrlService;
        _addressService = addressService;
        _addressAttributeParser = addressAttributeParser;
        _checkoutModelFactory = checkoutModelFactory;
        _countryService = countryService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _logger = logger;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _paymentPluginManager = paymentPluginManager;
        _paymentService = paymentService;
        _productService = productService;
        _shippingService = shippingService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _taxService = taxService;
        _addressValidator = addressValidator;
        _webHelper = webHelper;
        _workContext = workContext;
        _orderSettings = orderSettings;
        _paymentSettings = paymentSettings;
        _rewardPointsSettings = rewardPointsSettings;
        _shippingSettings = shippingSettings;
        _taxSettings = taxSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Load the checkout data and check whether the current customer can check out
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the customer, store, cart and the problem result (null if the checkout is possible)</returns>
    protected virtual async Task<(Customer Customer, int StoreId, IList<ShoppingCartItem> Cart, IActionResult Problem)> PrepareCheckoutAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);

        if (_orderSettings.CheckoutDisabled)
            return (customer, store.Id, cart, ForbiddenProblem(await _localizationService.GetResourceAsync("Checkout.Disabled")));

        if (!cart.Any())
            return (customer, store.Id, cart, WarningsProblem([await _localizationService.GetResourceAsync("ShoppingCart.CartIsEmpty")]));

        if (await _customerService.IsGuestAsync(customer))
        {
            var downloadableProductsRequireRegistration = _customerSettings.RequireRegistrationForDownloadableProducts
                && await _productService.HasAnyDownloadableProductAsync(cart.Select(item => item.ProductId).ToArray());
            if (!_orderSettings.AnonymousCheckoutAllowed || downloadableProductsRequireRegistration)
                return (customer, store.Id, cart, ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.RegisteredCustomerRequired")));
        }

        return (customer, store.Id, cart, null);
    }

    /// <summary>
    /// Convert a dictionary to a form collection, so the storefront parsers and payment plugins can be used
    /// </summary>
    /// <param name="values">Values</param>
    /// <returns>Form collection</returns>
    protected virtual IFormCollection PrepareForm(IDictionary<string, string> values)
    {
        return new FormCollection((values ?? new Dictionary<string, string>())
            .ToDictionary(pair => pair.Key, pair => new StringValues(pair.Value), StringComparer.InvariantCultureIgnoreCase));
    }

    /// <summary>
    /// Get the existing address or save the new address of the request
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the address or null if the request is not valid (see ModelState)</returns>
    protected virtual async Task<Address> SaveAddressAsync(Customer customer, CheckoutAddressRequest request)
    {
        //existing address
        if (request.AddressId > 0)
        {
            var existingAddress = await _customerService.GetCustomerAddressAsync(customer.Id, request.AddressId);
            if (existingAddress == null)
                ModelState.AddModelError(nameof(request.AddressId), await _localizationService.GetResourceAsync("Checkout.Address.NotFound"));

            return existingAddress;
        }

        //new address
        var newAddress = request.NewAddress;
        if (newAddress == null)
        {
            ModelState.AddModelError(nameof(request.NewAddress), await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.AddressRequired"));
            return null;
        }

        var validationResult = await _addressValidator.ValidateAsync(newAddress);
        foreach (var error in validationResult.Errors)
            ModelState.AddModelError($"{nameof(request.NewAddress)}.{error.PropertyName}", error.ErrorMessage);

        //custom address attributes
        var customAttributes = await _addressAttributeParser.ParseCustomAttributesAsync(PrepareForm(request.Attributes), NopCommonDefaults.AddressAttributeControlName);
        foreach (var error in await _addressAttributeParser.GetAttributeWarningsAsync(customAttributes))
            ModelState.AddModelError(string.Empty, error);

        if (!ModelState.IsValid)
            return null;

        //try to find an address with the same values (don't duplicate records)
        var address = _addressService.FindAddress((await _customerService.GetAddressesByCustomerIdAsync(customer.Id)).ToList(),
            newAddress.FirstName, newAddress.LastName, newAddress.PhoneNumber,
            newAddress.Email, newAddress.FaxNumber, newAddress.Company,
            newAddress.Address1, newAddress.Address2, newAddress.City,
            newAddress.County, newAddress.StateProvinceId, newAddress.ZipPostalCode,
            newAddress.CountryId, customAttributes);
        if (address != null)
            return address;

        newAddress.Id = 0;
        address = newAddress.ToEntity();
        address.CustomAttributes = customAttributes;
        address.CreatedOnUtc = DateTime.UtcNow;
        if (address.CountryId == 0)
            address.CountryId = null;
        if (address.StateProvinceId == 0)
            address.StateProvinceId = null;

        await _addressService.InsertAddressAsync(address);
        await _customerService.InsertCustomerAddressAsync(customer, address);

        return address;
    }

    /// <summary>
    /// Save the selected pickup point as shipping option
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="storeId">Store identifier</param>
    /// <param name="cart">Cart</param>
    /// <param name="pickupPointValue">Pickup point as "{id}___{providerSystemName}"</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the pickup point has been saved</returns>
    protected virtual async Task<bool> SavePickupPointAsync(Customer customer, int storeId, IList<ShoppingCartItem> cart, string pickupPointValue)
    {
        var pickupPointParts = (pickupPointValue ?? string.Empty).Split(_separator, StringSplitOptions.None);
        if (pickupPointParts.Length != 2)
            return false;

        var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
        var pickupPoint = (await _shippingService.GetPickupPointsAsync(cart, billingAddress, customer, pickupPointParts[1], storeId))
            .PickupPoints.FirstOrDefault(point => point.Id.Equals(pickupPointParts[0]));
        if (pickupPoint == null)
            return false;

        var name = !string.IsNullOrEmpty(pickupPoint.Name)
            ? string.Format(await _localizationService.GetResourceAsync("Checkout.PickupPoints.Name"), pickupPoint.Name)
            : await _localizationService.GetResourceAsync("Checkout.PickupPoints.NullName");
        var pickupInStoreShippingOption = new ShippingOption
        {
            Name = name,
            Rate = pickupPoint.PickupFee,
            Description = pickupPoint.Description,
            ShippingRateComputationMethodSystemName = pickupPoint.ProviderSystemName,
            IsPickupInStore = true
        };

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, pickupInStoreShippingOption, storeId);
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPickupPointAttribute, pickupPoint, storeId);

        return true;
    }

    /// <summary>
    /// Prepare the next step after the shipping address has been saved
    /// </summary>
    protected virtual async Task<CheckoutStepModel> PrepareStepAfterShippingAddressAsync(Customer customer, int storeId, IList<ShoppingCartItem> cart)
    {
        var shippingMethodModel = await _checkoutModelFactory.PrepareShippingMethodModelAsync(cart, await _customerService.GetCustomerShippingAddressAsync(customer));
        if (_shippingSettings.BypassShippingMethodSelectionIfOnlyOne && shippingMethodModel.ShippingMethods.Count == 1)
        {
            //if we have only one shipping method, then a customer doesn't have to choose a shipping method
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute,
                shippingMethodModel.ShippingMethods.First().ShippingOption, storeId);

            return await PrepareStepAfterShippingMethodAsync(customer, storeId, cart);
        }

        return new CheckoutStepModel { NextStep = STEP_SHIPPING_METHOD, Model = shippingMethodModel };
    }

    /// <summary>
    /// Prepare the next step after the shipping method has been saved (or shipping is not required)
    /// </summary>
    protected virtual async Task<CheckoutStepModel> PrepareStepAfterShippingMethodAsync(Customer customer, int storeId, IList<ShoppingCartItem> cart)
    {
        //we ignore reward points during cart total calculation
        if (!await _orderProcessingService.IsPaymentWorkflowRequiredAsync(cart, false))
        {
            await _genericAttributeService.SaveAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, null, storeId);

            return new CheckoutStepModel { NextStep = STEP_CONFIRM, Model = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart) };
        }

        var filterByCountryId = _addressSettings.CountryEnabled
            ? (await _customerService.GetCustomerBillingAddressAsync(customer))?.CountryId ?? 0
            : 0;
        var paymentMethodModel = await _checkoutModelFactory.PreparePaymentMethodModelAsync(cart, filterByCountryId);

        if (_paymentSettings.BypassPaymentMethodSelectionIfOnlyOne && paymentMethodModel.PaymentMethods.Count == 1 && !paymentMethodModel.DisplayRewardPoints)
        {
            //if we have only one payment method and reward points are disabled or the current customer doesn't have any reward points
            var systemName = paymentMethodModel.PaymentMethods[0].PaymentMethodSystemName;
            var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(systemName, customer, storeId);
            if (_paymentPluginManager.IsPluginActive(paymentMethod))
            {
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, systemName, storeId);
                return await PrepareStepAfterPaymentMethodAsync(paymentMethod, cart);
            }
        }

        return new CheckoutStepModel { NextStep = STEP_PAYMENT_METHOD, Model = paymentMethodModel };
    }

    /// <summary>
    /// Prepare the next step after the payment method has been saved
    /// </summary>
    protected virtual async Task<CheckoutStepModel> PrepareStepAfterPaymentMethodAsync(IPaymentMethod paymentMethod, IList<ShoppingCartItem> cart)
    {
        if (paymentMethod.SkipPaymentInfo ||
            (paymentMethod.PaymentMethodType == PaymentMethodType.Redirection && _paymentSettings.SkipPaymentInfoStepForRedirectionPaymentMethods))
        {
            //skip payment info step
            await _orderProcessingService.SetProcessPaymentRequestAsync(new ProcessPaymentRequest());

            return new CheckoutStepModel { NextStep = STEP_CONFIRM, Model = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart) };
        }

        return new CheckoutStepModel { NextStep = STEP_PAYMENT_INFO, Model = await PreparePaymentInfoModelAsync(paymentMethod) };
    }

    /// <summary>
    /// Prepare the payment info model
    /// </summary>
    /// <param name="paymentMethod">Payment method</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the payment info model</returns>
    protected virtual async Task<PaymentInfoModel> PreparePaymentInfoModelAsync(IPaymentMethod paymentMethod)
    {
        var language = await _workContext.GetWorkingLanguageAsync();

        return new PaymentInfoModel
        {
            PaymentMethodSystemName = paymentMethod.PluginDescriptor.SystemName,
            PaymentMethodName = await _localizationService.GetLocalizedFriendlyNameAsync(paymentMethod, language.Id),
            PaymentMethodType = paymentMethod.PaymentMethodType.ToString(),
            SkipPaymentInfo = paymentMethod.SkipPaymentInfo,
            PaymentMethodDescription = await paymentMethod.GetPaymentMethodDescriptionAsync(),
            DisplayOrderTotals = _orderSettings.OnePageCheckoutDisplayOrderTotalsOnPaymentInfoTab
        };
    }

    /// <summary>
    /// Get the selected payment method
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <param name="storeId">Store identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the payment method or null if not selected</returns>
    protected virtual async Task<IPaymentMethod> GetSelectedPaymentMethodAsync(Customer customer, int storeId)
    {
        var systemName = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, storeId);
        if (string.IsNullOrEmpty(systemName))
            return null;

        var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(systemName, customer, storeId);

        return _paymentPluginManager.IsPluginActive(paymentMethod) ? paymentMethod : null;
    }

    /// <summary>
    /// Execute the post-processing of the payment (e.g. redirect to the payment provider) and capture the redirect URL
    /// </summary>
    /// <param name="order">Order</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the redirect URL or null if there is no redirect</returns>
    protected virtual async Task<string> PostProcessPaymentAsync(Order order)
    {
        await _paymentService.PostProcessPaymentAsync(new PostProcessPaymentRequest { Order = order });

        if (!_webHelper.IsRequestBeingRedirected)
            return null;

        //payment plugins redirect the current response to the payment provider, return the URL to the frontend instead
        var redirectUrl = Response.Headers.Location.ToString();
        Response.Headers.Remove("Location");
        Response.StatusCode = StatusCodes.Status200OK;

        return redirectUrl;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Starts the checkout: validates the cart, resets the checkout data and returns the first step
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the checkout overview with the billing address step</returns>
    [HttpPost("start")]
    [ProducesResponseType(typeof(OnePageCheckoutModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Start()
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        //"button" payment methods only (displayed on the shopping cart page) don't allow the standard checkout
        var paymentMethods = await (await _paymentPluginManager.LoadActivePluginsAsync(customer, storeId))
            .WhereAwait(async paymentMethod => !await paymentMethod.HidePaymentMethodAsync(cart))
            .ToListAsync();
        if (paymentMethods.All(paymentMethod => paymentMethod.PaymentMethodType == PaymentMethodType.Button) && paymentMethods.Any())
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ButtonPaymentMethodsOnly")]);

        //reset checkout data
        await _shoppingCartService.ResetCheckoutDataAsync(customer, storeId);

        //validation (cart and each cart item)
        var checkoutAttributesXml = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, storeId);
        var warnings = (await _shoppingCartService.GetShoppingCartWarningsAsync(cart, checkoutAttributesXml, true)).ToList();
        foreach (var item in cart)
        {
            var product = await _productService.GetProductByIdAsync(item.ProductId);
            warnings.AddRange(await _shoppingCartService.GetShoppingCartItemWarningsAsync(customer, item.ShoppingCartType, product, item.StoreId,
                item.AttributesXml, item.CustomerEnteredPrice, item.RentalStartDateUtc, item.RentalEndDateUtc, item.Quantity, false, item.Id));
        }
        if (warnings.Any())
            return WarningsProblem(warnings.Distinct());

        return Ok(await _checkoutModelFactory.PrepareOnePageCheckoutModelAsync(cart));
    }

    /// <summary>
    /// Gets the billing address step (existing addresses and the form of a new address)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the billing address step</returns>
    [HttpGet("billing-address")]
    [ProducesResponseType(typeof(CheckoutBillingAddressModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> BillingAddress()
    {
        var (_, _, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        var model = new CheckoutBillingAddressModel();
        await _checkoutModelFactory.PrepareBillingAddressModelAsync(model, cart, prePopulateNewAddressWithCustomerFields: true);

        return Ok(model);
    }

    /// <summary>
    /// Sets the billing address (an existing one or a new one)
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the next step</returns>
    [HttpPut("billing-address")]
    [ProducesResponseType(typeof(CheckoutStepModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetBillingAddress(CheckoutAddressRequest request)
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        var result = new CheckoutStepModel();

        //VAT number of guests
        if (request.AddressId == 0 && await _customerService.IsGuestAsync(customer) && _taxSettings.EuVatEnabled && _taxSettings.EuVatEnabledForGuests)
        {
            var (vatNumberStatus, _, _) = await _taxService.GetVatNumberStatusAsync(request.VatNumber);
            customer.VatNumberStatus = vatNumberStatus;
            customer.VatNumber = request.VatNumber;
            await _customerService.UpdateCustomerAsync(customer);

            if (vatNumberStatus != VatNumberStatus.Valid && !string.IsNullOrEmpty(request.VatNumber))
            {
                result.Warnings.Add(string.Format(await _localizationService.GetResourceAsync("Checkout.VatNumber.Warning"),
                    await _localizationService.GetLocalizedEnumAsync(vatNumberStatus)));
            }
        }

        var address = await SaveAddressAsync(customer, request);
        if (address == null)
            return ValidationProblem(ModelState);

        customer.BillingAddressId = address.Id;
        await _customerService.UpdateCustomerAsync(customer);

        CheckoutStepModel nextStep;
        if (await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
        {
            //by default shipping is available if the country is not specified
            var shippingAllowed = !_addressSettings.CountryEnabled || ((await _countryService.GetCountryByAddressAsync(address))?.AllowsShipping ?? false);
            if (_shippingSettings.ShipToSameAddress && request.ShipToSameAddress && shippingAllowed)
            {
                customer.ShippingAddressId = address.Id;
                await _customerService.UpdateCustomerAsync(customer);

                //reset selected shipping method (in case if "pick up in store" was selected)
                await _genericAttributeService.SaveAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, storeId);
                await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, storeId);

                nextStep = await PrepareStepAfterShippingAddressAsync(customer, storeId, cart);
            }
            else
            {
                var shippingAddressModel = new CheckoutShippingAddressModel();
                await _checkoutModelFactory.PrepareShippingAddressModelAsync(shippingAddressModel, cart, prePopulateNewAddressWithCustomerFields: true);
                nextStep = new CheckoutStepModel { NextStep = STEP_SHIPPING_ADDRESS, Model = shippingAddressModel };
            }
        }
        else
        {
            //shipping is not required
            await _genericAttributeService.SaveAttributeAsync<ShippingOption>(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, null, storeId);
            nextStep = await PrepareStepAfterShippingMethodAsync(customer, storeId, cart);
        }

        return Ok(nextStep with { Warnings = result.Warnings.Concat(nextStep.Warnings).ToList() });
    }

    /// <summary>
    /// Gets the shipping address step (existing addresses, the form of a new address and the pickup points)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipping address step</returns>
    [HttpGet("shipping-address")]
    [ProducesResponseType(typeof(CheckoutShippingAddressModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> ShippingAddress()
    {
        var (_, _, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingNotRequired")]);

        var model = new CheckoutShippingAddressModel();
        await _checkoutModelFactory.PrepareShippingAddressModelAsync(model, cart, prePopulateNewAddressWithCustomerFields: true);

        return Ok(model);
    }

    /// <summary>
    /// Sets the shipping address (an existing one, a new one or a pickup point)
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the next step</returns>
    [HttpPut("shipping-address")]
    [ProducesResponseType(typeof(CheckoutStepModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetShippingAddress(CheckoutAddressRequest request)
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingNotRequired")]);

        //pickup point
        if (_shippingSettings.AllowPickupInStore && !_orderSettings.DisplayPickupInStoreOnShippingMethodPage)
        {
            if (request.PickupInStore)
            {
                if (!await SavePickupPointAsync(customer, storeId, cart, request.PickupPoint))
                    return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PickupPointNotAvailable")]);

                return Ok(await PrepareStepAfterShippingMethodAsync(customer, storeId, cart));
            }

            //set value indicating that "pick up in store" option has not been chosen
            await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, storeId);
        }

        var address = await SaveAddressAsync(customer, request);
        if (address == null)
            return ValidationProblem(ModelState);

        customer.ShippingAddressId = address.Id;
        await _customerService.UpdateCustomerAsync(customer);

        return Ok(await PrepareStepAfterShippingAddressAsync(customer, storeId, cart));
    }

    /// <summary>
    /// Gets the available shipping methods
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipping methods</returns>
    [HttpGet("shipping-methods")]
    [ProducesResponseType(typeof(CheckoutShippingMethodModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> ShippingMethods()
    {
        var (customer, _, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingNotRequired")]);

        return Ok(await _checkoutModelFactory.PrepareShippingMethodModelAsync(cart, await _customerService.GetCustomerShippingAddressAsync(customer)));
    }

    /// <summary>
    /// Selects the shipping method (or a pickup point)
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the next step</returns>
    [HttpPut("shipping-method")]
    [ProducesResponseType(typeof(CheckoutStepModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetShippingMethod(ShippingMethodRequest request)
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        if (!await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingNotRequired")]);

        //pickup point
        if (_shippingSettings.AllowPickupInStore && _orderSettings.DisplayPickupInStoreOnShippingMethodPage)
        {
            if (request.PickupInStore)
            {
                if (!await SavePickupPointAsync(customer, storeId, cart, request.PickupPoint))
                    return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PickupPointNotAvailable")]);

                return Ok(await PrepareStepAfterShippingMethodAsync(customer, storeId, cart));
            }

            await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, storeId);
        }

        //parse selected method
        var optionParts = (request.ShippingOption ?? string.Empty).Split(_separator, StringSplitOptions.RemoveEmptyEntries);
        if (optionParts.Length != 2)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingMethodNotAvailable")]);
        var (selectedName, systemName) = (optionParts[0], optionParts[1]);

        //performance optimization, try the cached options first
        var shippingOptions = await _genericAttributeService.GetAttributeAsync<List<ShippingOption>>(customer, NopCustomerDefaults.OfferedShippingOptionsAttribute, storeId);
        if (shippingOptions == null || !shippingOptions.Any())
        {
            shippingOptions = (await _shippingService.GetShippingOptionsAsync(cart, await _customerService.GetCustomerShippingAddressAsync(customer),
                customer, systemName, storeId)).ShippingOptions.ToList();
        }
        else
        {
            shippingOptions = shippingOptions
                .Where(option => option.ShippingRateComputationMethodSystemName.Equals(systemName, StringComparison.InvariantCultureIgnoreCase))
                .ToList();
        }

        var shippingOption = shippingOptions.Find(option => !string.IsNullOrEmpty(option.Name) && option.Name.Equals(selectedName, StringComparison.InvariantCultureIgnoreCase));
        if (shippingOption == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.ShippingMethodNotAvailable")]);

        if (_shippingSettings.AllowCustomerToChooseDeliveryDate && request.DesiredDeliveryDate.HasValue)
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.DesiredDeliveryDate, request.DesiredDeliveryDate, storeId);

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, shippingOption, storeId);

        return Ok(await PrepareStepAfterShippingMethodAsync(customer, storeId, cart));
    }

    /// <summary>
    /// Gets the available payment methods
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the payment methods</returns>
    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(CheckoutPaymentMethodModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> PaymentMethods()
    {
        var (customer, _, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        var filterByCountryId = _addressSettings.CountryEnabled
            ? (await _customerService.GetCustomerBillingAddressAsync(customer))?.CountryId ?? 0
            : 0;

        return Ok(await _checkoutModelFactory.PreparePaymentMethodModelAsync(cart, filterByCountryId));
    }

    /// <summary>
    /// Selects the payment method
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the next step</returns>
    [HttpPut("payment-method")]
    [ProducesResponseType(typeof(CheckoutStepModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetPaymentMethod(PaymentMethodRequest request)
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        //reward points
        if (_rewardPointsSettings.Enabled)
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.UseRewardPointsDuringCheckoutAttribute, request.UseRewardPoints, storeId);

        //whether payment workflow is required
        if (!await _orderProcessingService.IsPaymentWorkflowRequiredAsync(cart))
        {
            await _genericAttributeService.SaveAttributeAsync<string>(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, null, storeId);

            return Ok(new CheckoutStepModel { NextStep = STEP_CONFIRM, Model = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart) });
        }

        var paymentMethod = string.IsNullOrEmpty(request.PaymentMethodSystemName)
            ? null
            : await _paymentPluginManager.LoadPluginBySystemNameAsync(request.PaymentMethodSystemName, customer, storeId);
        if (paymentMethod == null || !_paymentPluginManager.IsPluginActive(paymentMethod) || paymentMethod.PaymentMethodType == PaymentMethodType.Button)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PaymentMethodNotAvailable")]);

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedPaymentMethodAttribute, request.PaymentMethodSystemName, storeId);

        return Ok(await PrepareStepAfterPaymentMethodAsync(paymentMethod, cart));
    }

    /// <summary>
    /// Gets the payment info step of the selected payment method
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the payment info step</returns>
    [HttpGet("payment-info")]
    [ProducesResponseType(typeof(PaymentInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> PaymentInfo()
    {
        var (customer, storeId, _, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        var paymentMethod = await GetSelectedPaymentMethodAsync(customer, storeId);
        if (paymentMethod == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PaymentMethodNotSelected")]);

        return Ok(await PreparePaymentInfoModelAsync(paymentMethod));
    }

    /// <summary>
    /// Enters the payment information; the values are validated by the payment plugin
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the next step (confirm)</returns>
    [HttpPut("payment-info")]
    [ProducesResponseType(typeof(CheckoutStepModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> SetPaymentInfo(PaymentInfoRequest request)
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        var paymentMethod = await GetSelectedPaymentMethodAsync(customer, storeId);
        if (paymentMethod == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PaymentMethodNotSelected")]);

        var form = PrepareForm(request.Values);
        var warnings = await paymentMethod.ValidatePaymentFormAsync(form);
        if (warnings.Any())
            return WarningsProblem(warnings);

        await _orderProcessingService.SetProcessPaymentRequestAsync(await paymentMethod.GetPaymentInfoAsync(form));

        return Ok(new CheckoutStepModel { NextStep = STEP_CONFIRM, Model = await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart) });
    }

    /// <summary>
    /// Gets the confirm step (terms of service, minimum order totals, warnings)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the confirm step</returns>
    [HttpGet("confirm")]
    [ProducesResponseType(typeof(CheckoutConfirmModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Confirm()
    {
        var (_, _, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        return Ok(await _checkoutModelFactory.PrepareConfirmOrderModelAsync(cart));
    }

    /// <summary>
    /// Places the order
    /// </summary>
    /// <remarks>
    /// For redirection payment methods (e.g. PayPal) the result contains the URL of the payment provider; the frontend has to redirect the customer there.
    /// CAPTCHA for guests is not supported by the API.
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation; the task result contains the placed order</returns>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(PlaceOrderResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> PlaceOrder()
    {
        var (customer, storeId, cart, problem) = await PrepareCheckoutAsync();
        if (problem != null)
            return problem;

        //prevent 2 orders being placed within an X seconds time frame
        if (_orderSettings.MinimumOrderPlacementInterval > 0)
        {
            var lastOrder = (await _orderService.SearchOrdersAsync(storeId: storeId, customerId: customer.Id, pageSize: 1)).FirstOrDefault();
            if (lastOrder != null && (DateTime.UtcNow - lastOrder.CreatedOnUtc).TotalMinutes <= _orderSettings.MinimumOrderPlacementInterval)
                return WarningsProblem([await _localizationService.GetResourceAsync("Checkout.MinOrderPlacementInterval")]);
        }

        //place order
        var processPaymentRequest = await _orderProcessingService.GetProcessPaymentRequestAsync();
        if (processPaymentRequest == null)
        {
            if (await _orderProcessingService.IsPaymentWorkflowRequiredAsync(cart))
                return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.PaymentInfoRequired")]);

            processPaymentRequest = new ProcessPaymentRequest();
        }

        processPaymentRequest.StoreId = storeId;
        processPaymentRequest.CustomerId = customer.Id;
        processPaymentRequest.PaymentMethodSystemName = await _genericAttributeService.GetAttributeAsync<string>(customer,
            NopCustomerDefaults.SelectedPaymentMethodAttribute, storeId);
        await _orderProcessingService.SetProcessPaymentRequestAsync(processPaymentRequest);

        var placeOrderResult = await _orderProcessingService.PlaceOrderAsync(processPaymentRequest);
        if (!placeOrderResult.Success)
            return WarningsProblem(placeOrderResult.Errors);

        await _orderProcessingService.SetProcessPaymentRequestAsync(null);

        var order = placeOrderResult.PlacedOrder;
        var result = new PlaceOrderResultModel
        {
            OrderId = order.Id,
            CustomOrderNumber = order.CustomOrderNumber,
            CompletedUrl = _headlessApiFrontendUrlService.GetCheckoutCompletedUrl(order.Id)
        };

        //payment method could be null if order total is 0
        var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(order.PaymentMethodSystemName, customer, storeId);
        if (paymentMethod != null)
        {
            try
            {
                result.PaymentRedirectUrl = await PostProcessPaymentAsync(order);
            }
            catch (Exception exception)
            {
                //the order has been placed anyway, the customer can retry the payment from the order details
                await _logger.WarningAsync(exception.Message, exception, customer);
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets the "checkout completed" data of an order
    /// </summary>
    /// <param name="orderId">Order identifier; empty to get the last order of the customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the checkout completed data</returns>
    [HttpGet("completed/{orderId:int?}")]
    [ProducesResponseType(typeof(CheckoutCompletedModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Completed(int? orderId = null)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        var order = orderId.HasValue
            ? await _orderService.GetOrderByIdAsync(orderId.Value)
            : (await _orderService.SearchOrdersAsync(storeId: store.Id, customerId: customer.Id, pageSize: 1)).FirstOrDefault();
        if (order == null || order.Deleted || order.CustomerId != customer.Id)
            return NotFoundProblem();

        return Ok(await _checkoutModelFactory.PrepareCheckoutCompletedModelAsync(order));
    }

    /// <summary>
    /// Retries the payment of an order (e.g. after the customer canceled the payment at the payment provider)
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the URL of the payment provider</returns>
    [HttpPost("orders/{orderId:int}/repost-payment")]
    [ProducesResponseType(typeof(PlaceOrderResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> RePostPayment(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || order.Deleted || order.CustomerId != customer.Id)
            return NotFoundProblem();

        if (!await _paymentService.CanRePostProcessPaymentAsync(order))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Checkout.RePostPaymentNotPossible")]);

        return Ok(new PlaceOrderResultModel
        {
            OrderId = order.Id,
            CustomOrderNumber = order.CustomOrderNumber,
            PaymentRedirectUrl = await PostProcessPaymentAsync(order),
            CompletedUrl = _headlessApiFrontendUrlService.GetOrderDetailsUrl(order.Id)
        });
    }

    #endregion
}

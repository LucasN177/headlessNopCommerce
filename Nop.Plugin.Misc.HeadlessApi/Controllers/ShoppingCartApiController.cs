using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Http;
using Nop.Core.Http.Extensions;
using Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Discounts;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Shipping;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the shopping cart endpoints
/// </summary>
/// <remarks>
/// All changing endpoints return the updated cart, so the frontend doesn't need to reload it
/// </remarks>
[HeadlessApiRoute("cart")]
public class ShoppingCartApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly HeadlessApiCheckoutAttributeService _headlessApiCheckoutAttributeService;
    protected readonly HeadlessApiShoppingCartService _headlessApiShoppingCartService;
    protected readonly ICustomerService _customerService;
    protected readonly IDiscountService _discountService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IGiftCardService _giftCardService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPermissionService _permissionService;
    protected readonly IProductService _productService;
    protected readonly IShippingService _shippingService;
    protected readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly ShippingSettings _shippingSettings;
    protected readonly ShoppingCartSettings _shoppingCartSettings;

    #endregion

    #region Ctor

    public ShoppingCartApiController(HeadlessApiCheckoutAttributeService headlessApiCheckoutAttributeService,
        HeadlessApiShoppingCartService headlessApiShoppingCartService,
        ICustomerService customerService,
        IDiscountService discountService,
        IGenericAttributeService genericAttributeService,
        IGiftCardService giftCardService,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        IProductService productService,
        IShippingService shippingService,
        IShoppingCartModelFactory shoppingCartModelFactory,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWorkContext workContext,
        ShippingSettings shippingSettings,
        ShoppingCartSettings shoppingCartSettings)
    {
        _headlessApiCheckoutAttributeService = headlessApiCheckoutAttributeService;
        _headlessApiShoppingCartService = headlessApiShoppingCartService;
        _customerService = customerService;
        _discountService = discountService;
        _genericAttributeService = genericAttributeService;
        _giftCardService = giftCardService;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _productService = productService;
        _shippingService = shippingService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _workContext = workContext;
        _shippingSettings = shippingSettings;
        _shoppingCartSettings = shoppingCartSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Prepare the shopping cart model of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shopping cart model</returns>
    protected virtual async Task<ShoppingCartModel> PrepareCartModelAsync()
    {
        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);

        return await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(new ShoppingCartModel(), cart);
    }

    /// <summary>
    /// Check whether the shopping cart is enabled for the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the cart is enabled</returns>
    protected virtual async Task<IActionResult> CheckCartEnabledAsync()
    {
        if (await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART))
            return null;

        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.Disabled"));
    }

    /// <summary>
    /// Get the validation error of a gift card code
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <param name="giftCardCode">Gift card code</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the error or empty string if the code is valid</returns>
    protected virtual async Task<string> GetGiftCardValidationErrorAsync(IList<ShoppingCartItem> cart, string giftCardCode)
    {
        if (string.IsNullOrWhiteSpace(giftCardCode))
            return await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");

        if (await _shoppingCartService.ShoppingCartIsRecurringAsync(cart))
            return await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.DontWorkWithAutoshipProducts");

        var giftCard = (await _giftCardService.GetAllGiftCardsAsync(giftCardCouponCode: giftCardCode)).FirstOrDefault();
        if (giftCard == null || !await _giftCardService.IsGiftCardValidAsync(giftCard))
            return await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.WrongGiftCard");

        if (await _productService.HasAnyGiftCardProductAsync(cart.Select(item => item.ProductId).ToArray()))
            return await _localizationService.GetResourceAsync("ShoppingCart.GiftCardCouponCode.DontWorkWithGiftCards");

        return string.Empty;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the shopping cart of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shopping cart</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Get()
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Gets the totals of the shopping cart (subtotal, shipping, tax, discounts, total)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the totals</returns>
    [HttpGet("totals")]
    [ProducesResponseType(typeof(OrderTotalsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Totals()
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);

        return Ok(await _shoppingCartModelFactory.PrepareOrderTotalsModelAsync(cart, true));
    }

    /// <summary>
    /// Adds a product to the shopping cart
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> AddItem(AddToCartRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var warnings = await _headlessApiShoppingCartService.AddToCartAsync(ShoppingCartType.ShoppingCart, request);
        if (warnings.Any())
            return WarningsProblem(warnings);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Changes the quantity of a shopping cart item; the item is removed if the quantity is 0
    /// </summary>
    /// <param name="itemId">Shopping cart item identifier</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpPut("items/{itemId:int}")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UpdateItem(int itemId, UpdateCartItemRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var item = await _headlessApiShoppingCartService.GetCartItemAsync(ShoppingCartType.ShoppingCart, itemId);
        if (item == null)
            return NotFoundProblem();

        var warnings = await _headlessApiShoppingCartService.UpdateCartItemAsync(item, request.Quantity);
        if (warnings.Any())
            return WarningsProblem(warnings);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Removes an item from the shopping cart
    /// </summary>
    /// <param name="itemId">Shopping cart item identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpDelete("items/{itemId:int}")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> DeleteItem(int itemId)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var item = await _headlessApiShoppingCartService.GetCartItemAsync(ShoppingCartType.ShoppingCart, itemId);
        if (item == null)
            return NotFoundProblem();

        await _headlessApiShoppingCartService.DeleteCartItemAsync(item);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Applies a discount coupon code
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpPost("discount-coupons")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> ApplyDiscountCoupon(CouponCodeRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var couponCode = request.Code?.Trim();
        if (string.IsNullOrEmpty(couponCode))
            return WarningsProblem([await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.Empty")]);

        //we find even hidden records here. this way we can display a user-friendly message if it's expired
        var discounts = (await _discountService.GetAllDiscountsAsync(couponCode: couponCode, showHidden: true))
            .Where(discount => discount.RequiresCouponCode)
            .ToList();
        if (!discounts.Any())
            return WarningsProblem([await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.CannotBeFound")]);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var errors = new List<string>();
        var anyValidDiscount = await discounts.AnyAwaitAsync(async discount =>
        {
            var validationResult = await _discountService.ValidateDiscountAsync(discount, customer, [couponCode]);
            errors.AddRange(validationResult.Errors);

            return validationResult.IsValid;
        });

        if (!anyValidDiscount)
        {
            return WarningsProblem(errors.Any()
                ? errors
                : [await _localizationService.GetResourceAsync("ShoppingCart.DiscountCouponCode.WrongDiscount")]);
        }

        await _customerService.ApplyDiscountCouponCodeAsync(customer, couponCode);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Removes an applied discount coupon code
    /// </summary>
    /// <param name="discountId">Discount identifier (see DiscountBox.AppliedDiscountsWithCodes of the cart)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpDelete("discount-coupons/{discountId:int}")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> RemoveDiscountCoupon(int discountId)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var discount = await _discountService.GetDiscountByIdAsync(discountId);
        if (discount != null)
            await _customerService.RemoveDiscountCouponCodeAsync(await _workContext.GetCurrentCustomerAsync(), discount.CouponCode);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Applies a gift card code
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpPost("gift-cards")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> ApplyGiftCard(CouponCodeRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var giftCardCode = request.Code?.Trim();
        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);
        var validationError = await GetGiftCardValidationErrorAsync(cart, giftCardCode);
        if (!string.IsNullOrEmpty(validationError))
            return WarningsProblem([validationError]);

        await _customerService.ApplyGiftCardCouponCodeAsync(await _workContext.GetCurrentCustomerAsync(), giftCardCode);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Removes an applied gift card code
    /// </summary>
    /// <param name="giftCardId">Gift card identifier (see the applied gift cards in the cart totals)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpDelete("gift-cards/{giftCardId:int}")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> RemoveGiftCard(int giftCardId)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var giftCard = await _giftCardService.GetGiftCardByIdAsync(giftCardId);
        if (giftCard != null)
            await _customerService.RemoveGiftCardCouponCodeAsync(await _workContext.GetCurrentCustomerAsync(), giftCard.GiftCardCouponCode);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Clears the shopping cart (items set aside by the vendor selection are moved back to the cart)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpDelete]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Clear()
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        await _shoppingCartService.ClearShoppingCartAsync(customer, store.Id);

        //reset checkout data like the deletion of an item does
        await _shoppingCartService.ResetCheckoutDataAsync(customer, store.Id, clearCheckoutAttributes: true);
        if (_shoppingCartSettings.VendorEnabled)
            await _shoppingCartService.SetShoppingCartVendorAsync(customer, null, store.Id);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Gets the mini shopping cart (flyout cart)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the mini shopping cart</returns>
    [HttpGet("mini")]
    [ProducesResponseType(typeof(MiniShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> MiniCart()
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        if (!_shoppingCartSettings.MiniShoppingCartEnabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.MiniCartDisabled"));

        return Ok(await _shoppingCartModelFactory.PrepareMiniShoppingCartModelAsync());
    }

    /// <summary>
    /// Selects the vendor whose products are checked out; the items of other vendors are set aside until another vendor (or all vendors) is selected
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart</returns>
    [HttpPut("vendor")]
    [ProducesResponseType(typeof(ShoppingCartModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> SetVendor(SetCartVendorRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        if (!_shoppingCartSettings.VendorEnabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.VendorCartDisabled"));

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        await _shoppingCartService.SetShoppingCartVendorAsync(customer, request.VendorId, store.Id);

        return Ok(await PrepareCartModelAsync());
    }

    /// <summary>
    /// Gets the shipping estimation form (enabled flag, countries, states, default address of the customer)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the estimation form</returns>
    [HttpGet("estimate-shipping")]
    [ProducesResponseType(typeof(EstimateShippingModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> EstimateShippingForm()
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);

        return Ok(await _shoppingCartModelFactory.PrepareEstimateShippingModelAsync(cart));
    }

    /// <summary>
    /// Estimates the shipping of the shopping cart; the offered options are cached, so one of them can be selected afterwards
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipping options (or the errors of the shipping providers)</returns>
    [HttpPost("estimate-shipping")]
    [ProducesResponseType(typeof(EstimateShippingResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> EstimateShipping(EstimateShippingRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var errors = new List<string>();
        if (!_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(request.ZipPostalCode))
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.ZipPostalCode.Required"));

        if (_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(request.City))
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.City.Required"));

        if (request.CountryId is null or 0)
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.Country.Required"));

        if (errors.Any())
            return WarningsProblem(errors);

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);

        //save checkout attributes (only if passed, the storefront always posts the whole cart form)
        if (request.CheckoutAttributes != null)
            await _headlessApiCheckoutAttributeService.SaveCheckoutAttributesAsync(cart, request.CheckoutAttributes);

        var model = new EstimateShippingModel
        {
            CountryId = request.CountryId,
            StateProvinceId = request.StateProvinceId,
            ZipPostalCode = request.ZipPostalCode,
            City = request.City,
            UseCity = _shippingSettings.EstimateShippingCityNameEnabled
        };

        return Ok(await _shoppingCartModelFactory.PrepareEstimateShippingResultModelAsync(cart, model, true));
    }

    /// <summary>
    /// Selects a shipping option offered by the shipping estimation
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated totals of the shopping cart</returns>
    [HttpPost("select-shipping-option")]
    [ProducesResponseType(typeof(OrderTotalsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> SelectShippingOption(SelectShippingOptionRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var errors = new List<string>();
        if (string.IsNullOrEmpty(request.ZipPostalCode) && !_shippingSettings.EstimateShippingCityNameEnabled)
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.ZipPostalCode.Required"));

        if (request.CountryId is null or 0)
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.Country.Required"));

        if (errors.Any())
            return WarningsProblem(errors);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);

        //save checkout attributes (only if passed)
        if (request.CheckoutAttributes != null)
            await _headlessApiCheckoutAttributeService.SaveCheckoutAttributesAsync(cart, request.CheckoutAttributes);

        var shippingOptions = new List<ShippingOption>();
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            //performance optimization. try cache first
            shippingOptions = await _genericAttributeService.GetAttributeAsync<List<ShippingOption>>(customer,
                NopCustomerDefaults.OfferedShippingOptionsAttribute, store.Id);

            if (shippingOptions == null || !shippingOptions.Any())
            {
                var address = new Address
                {
                    CountryId = request.CountryId,
                    StateProvinceId = request.StateProvinceId,
                    ZipPostalCode = request.ZipPostalCode,
                    City = request.City
                };

                //not found? let's load them using shipping service
                var getShippingOptionResponse = await _shippingService.GetShippingOptionsAsync(cart, address, customer, storeId: store.Id);
                if (getShippingOptionResponse.Success)
                    shippingOptions = getShippingOptionResponse.ShippingOptions.ToList();
                else
                    errors.AddRange(getShippingOptionResponse.Errors);
            }
        }

        var selectedShippingOption = shippingOptions?.Find(option => !string.IsNullOrEmpty(option.Name)
            && option.Name.Equals(request.Name, StringComparison.InvariantCultureIgnoreCase));
        if (selectedShippingOption == null)
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShippingPopUp.ShippingOption.IsNotFound"));

        if (errors.Any())
            return WarningsProblem(errors);

        //reset pickup point
        await _genericAttributeService.SaveAttributeAsync<PickupPoint>(customer, NopCustomerDefaults.SelectedPickupPointAttribute, null, store.Id);

        //cache shipping option
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SelectedShippingOptionAttribute, selectedShippingOption, store.Id);

        return Ok(await _shoppingCartModelFactory.PrepareOrderTotalsModelAsync(cart, true));
    }

    /// <summary>
    /// Saves the checkout attributes of the shopping cart (the available attributes are part of the shopping cart model)
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated shopping cart, the state of the conditional attributes and the warnings</returns>
    [HttpPut("checkout-attributes")]
    [ProducesResponseType(typeof(CheckoutAttributesResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> SaveCheckoutAttributes(CheckoutAttributesRequest request)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);
        await _headlessApiCheckoutAttributeService.SaveCheckoutAttributesAsync(cart, request.Attributes);

        var conditions = await _headlessApiCheckoutAttributeService.GetAttributeConditionsAsync(cart);

        return Ok(new CheckoutAttributesResultModel
        {
            Cart = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(new ShoppingCartModel(), cart),
            EnabledAttributeIds = conditions.EnabledAttributeIds,
            DisabledAttributeIds = conditions.DisabledAttributeIds,
            Warnings = await _headlessApiCheckoutAttributeService.GetCheckoutAttributeWarningsAsync(cart)
        });
    }

    /// <summary>
    /// Uploads a file for a checkout attribute of the "File upload" type (multipart/form-data, first file);
    /// the returned download GUID is then passed as the attribute value
    /// </summary>
    /// <param name="attributeId">Checkout attribute identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the upload result</returns>
    [HttpPost("checkout-attributes/{attributeId:int}/upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileUploadResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> UploadCheckoutAttributeFile(int attributeId)
    {
        if (await CheckCartEnabledAsync() is IActionResult problem)
            return problem;

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.ShoppingCart);
        var file = await Request.GetFirstOrDefaultFileAsync();
        var (download, error) = await _headlessApiCheckoutAttributeService.UploadFileAsync(cart, attributeId, file);
        if (download == null)
            return WarningsProblem([error]);

        return Ok(new FileUploadResultModel
        {
            DownloadGuid = download.DownloadGuid,
            DownloadUrl = Url.RouteUrl(NopRouteNames.Standard.DOWNLOAD_GET_FILE_UPLOAD, new { downloadId = download.DownloadGuid }),
            Message = await _localizationService.GetResourceAsync("ShoppingCart.FileUploaded")
        });
    }

    #endregion
}

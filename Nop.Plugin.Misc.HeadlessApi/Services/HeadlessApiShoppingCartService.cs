using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to manage the shopping cart and wishlist of the current customer
/// </summary>
public class HeadlessApiShoppingCartService
{
    #region Fields

    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IProductAttributeParser _productAttributeParser;
    protected readonly IProductService _productService;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly ShoppingCartSettings _shoppingCartSettings;

    #endregion

    #region Ctor

    public HeadlessApiShoppingCartService(HeadlessApiCatalogService headlessApiCatalogService,
        ICustomerActivityService customerActivityService,
        ILocalizationService localizationService,
        IProductAttributeParser productAttributeParser,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWorkContext workContext,
        ShoppingCartSettings shoppingCartSettings)
    {
        _headlessApiCatalogService = headlessApiCatalogService;
        _customerActivityService = customerActivityService;
        _localizationService = localizationService;
        _productAttributeParser = productAttributeParser;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _workContext = workContext;
        _shoppingCartSettings = shoppingCartSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Convert the request to a form collection, so the storefront parsers of nopCommerce can be used
    /// </summary>
    /// <param name="product">Product</param>
    /// <param name="request">Request</param>
    /// <returns>Form collection</returns>
    protected virtual IFormCollection PrepareForm(Product product, AddToCartRequest request)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var (key, value) in request.Attributes ?? new Dictionary<string, string>())
            values[key] = value;

        values[$"addtocart_{product.Id}.EnteredQuantity"] = request.Quantity.ToString(CultureInfo.InvariantCulture);

        //the storefront parser expects the price formatted with the current culture
        if (request.CustomerEnteredPrice.HasValue)
            values[$"addtocart_{product.Id}.CustomerEnteredPrice"] = request.CustomerEnteredPrice.Value.ToString(CultureInfo.CurrentCulture);

        return new FormCollection(values);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the shopping cart or wishlist of the current customer
    /// </summary>
    /// <param name="cartType">Shopping cart type</param>
    /// <param name="customWishlistId">Custom wishlist identifier; null for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the cart items</returns>
    public virtual async Task<IList<ShoppingCartItem>> GetCartAsync(ShoppingCartType cartType, int? customWishlistId = null)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        return await _shoppingCartService.GetShoppingCartAsync(customer, cartType, store.Id, customWishlistId: customWishlistId);
    }

    /// <summary>
    /// Gets an item of the shopping cart or wishlist of the current customer
    /// </summary>
    /// <param name="cartType">Shopping cart type</param>
    /// <param name="shoppingCartItemId">Shopping cart item identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the item or null if it doesn't belong to the current customer</returns>
    public virtual async Task<ShoppingCartItem> GetCartItemAsync(ShoppingCartType cartType, int shoppingCartItemId)
    {
        var cart = await GetCartAsync(cartType);

        return cart.FirstOrDefault(item => item.Id == shoppingCartItemId);
    }

    /// <summary>
    /// Adds a product to the shopping cart or wishlist of the current customer
    /// </summary>
    /// <param name="cartType">Shopping cart type</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the warnings (empty if the product has been added)</returns>
    public virtual async Task<IList<string>> AddToCartAsync(ShoppingCartType cartType, AddToCartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var product = await _productService.GetProductByIdAsync(request.ProductId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product))
            return [await _localizationService.GetResourceAsync("ShoppingCart.ProductUnpublished")];

        //we can add only simple products
        if (product.ProductType != ProductType.SimpleProduct)
            return [await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.OnlySimpleProducts")];

        var form = PrepareForm(product, request);
        var warnings = new List<string>();

        var customerEnteredPriceConverted = await _productAttributeParser.ParseCustomerEnteredPriceAsync(product, form);
        var quantity = _productAttributeParser.ParseEnteredQuantity(product, form);
        var attributesXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, warnings);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        warnings.AddRange(await _shoppingCartService.AddToCartAsync(customer, product, cartType, store.Id,
            attributesXml, customerEnteredPriceConverted,
            request.RentalStartDate?.Date, request.RentalEndDate?.Date, quantity, true));

        if (warnings.Any())
            return warnings;

        //activity log
        if (cartType == ShoppingCartType.Wishlist)
        {
            await _customerActivityService.InsertActivityAsync("PublicStore.AddToWishlist",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddToWishlist"), product.Name), product);
        }
        else
        {
            await _customerActivityService.InsertActivityAsync("PublicStore.AddToShoppingCart",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddToShoppingCart"), product.Name), product);
        }

        return warnings;
    }

    /// <summary>
    /// Changes the quantity of a shopping cart or wishlist item; the item is removed if the quantity is 0
    /// </summary>
    /// <param name="item">Shopping cart item</param>
    /// <param name="quantity">New quantity</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the warnings (empty if the item has been updated)</returns>
    public virtual async Task<IList<string>> UpdateCartItemAsync(ShoppingCartItem item, int quantity)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (quantity <= 0)
        {
            await DeleteCartItemAsync(item);
            return [];
        }

        var customer = await _workContext.GetCurrentCustomerAsync();

        return await _shoppingCartService.UpdateShoppingCartItemAsync(customer, item.Id, item.AttributesXml, item.CustomerEnteredPrice,
            item.RentalStartDateUtc, item.RentalEndDateUtc, quantity, true);
    }

    /// <summary>
    /// Adds a wishlist item to the shopping cart; the item is removed from the wishlist if configured ("Move items from wishlist to cart")
    /// </summary>
    /// <param name="wishlistItem">Wishlist item</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the warnings (empty if the item has been added)</returns>
    public virtual async Task<IList<string>> MoveToCartAsync(ShoppingCartItem wishlistItem)
    {
        ArgumentNullException.ThrowIfNull(wishlistItem);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var product = await _productService.GetProductByIdAsync(wishlistItem.ProductId);

        var warnings = await _shoppingCartService.AddToCartAsync(customer, product, ShoppingCartType.ShoppingCart, store.Id,
            wishlistItem.AttributesXml, wishlistItem.CustomerEnteredPrice,
            wishlistItem.RentalStartDateUtc, wishlistItem.RentalEndDateUtc, wishlistItem.Quantity, true);

        if (!warnings.Any() && _shoppingCartSettings.MoveItemsFromWishlistToCart)
            await _shoppingCartService.DeleteShoppingCartItemAsync(wishlistItem);

        return warnings;
    }

    /// <summary>
    /// Removes an item from the shopping cart or wishlist
    /// </summary>
    /// <param name="item">Shopping cart item</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task DeleteCartItemAsync(ShoppingCartItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        await _shoppingCartService.DeleteShoppingCartItemAsync(item, ensureOnlyActiveCheckoutAttributes: true);
    }

    #endregion
}

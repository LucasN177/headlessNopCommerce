using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Security;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Helpers;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the recently viewed products service that stores the list of API requests in a generic attribute of the current customer
/// </summary>
/// <remarks>
/// The default implementation stores the list in a cookie, which isn't available for a headless frontend;
/// all other requests (storefront) keep using the cookie
/// </remarks>
public class HeadlessApiRecentlyViewedProductsService : RecentlyViewedProductsService
{
    #region Constants

    /// <summary>
    /// Gets the name of the generic attribute that stores the identifiers of the recently viewed products (comma separated)
    /// </summary>
    public const string RecentlyViewedProductIdsAttribute = "HeadlessApi.RecentlyViewedProductIds";

    #endregion

    #region Fields

    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public HeadlessApiRecentlyViewedProductsService(CatalogSettings catalogSettings,
        CookieSettings cookieSettings,
        IGenericAttributeService genericAttributeService,
        IHttpContextAccessor httpContextAccessor,
        IProductService productService,
        IWebHelper webHelper,
        IWorkContext workContext) : base(catalogSettings, cookieSettings, httpContextAccessor, productService, webHelper)
    {
        _genericAttributeService = genericAttributeService;
        _workContext = workContext;
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

    /// <summary>
    /// Gets the identifiers of the recently viewed products of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product identifiers</returns>
    protected virtual async Task<List<int>> GetCustomerRecentlyViewedProductIdsAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var value = await _genericAttributeService.GetAttributeAsync<string>(customer, RecentlyViewedProductIdsAttribute);
        if (string.IsNullOrEmpty(value))
            return new List<int>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => int.TryParse(id, out var productId) ? productId : 0)
            .Where(productId => productId > 0)
            .Distinct()
            .ToList();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a "recently viewed products" list
    /// </summary>
    /// <param name="number">Number of products to load</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the "recently viewed products" list</returns>
    public override async Task<IList<Product>> GetRecentlyViewedProductsAsync(int number)
    {
        if (!IsApiRequest())
            return await base.GetRecentlyViewedProductsAsync(number);

        var productIds = (await GetCustomerRecentlyViewedProductIdsAsync()).Take(number).ToList();
        var products = await _productService.GetProductsByIdsAsync(productIds.ToArray());

        //keep the order of the list (most recently viewed first)
        return productIds
            .Select(productId => products.FirstOrDefault(product => product.Id == productId))
            .Where(product => product is { Published: true, Deleted: false })
            .ToList();
    }

    /// <summary>
    /// Adds a product to a recently viewed products list
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task AddProductToRecentlyViewedListAsync(int productId)
    {
        if (!IsApiRequest())
        {
            await base.AddProductToRecentlyViewedListAsync(productId);
            return;
        }

        //whether recently viewed products is enabled
        if (!_catalogSettings.RecentlyViewedProductsEnabled)
            return;

        var productIds = await GetCustomerRecentlyViewedProductIdsAsync();

        //move the product to the top of the list
        productIds.Remove(productId);
        productIds.Insert(0, productId);

        //limit list based on the allowed number of the recently viewed products
        var customer = await _workContext.GetCurrentCustomerAsync();
        await _genericAttributeService.SaveAttributeAsync(customer, RecentlyViewedProductIdsAttribute,
            string.Join(",", productIds.Take(_catalogSettings.RecentlyViewedProductsNumber)));
    }

    #endregion
}

using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Security;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Helpers;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the compare products service that stores the list of API requests in a generic attribute of the current customer
/// </summary>
/// <remarks>
/// The default implementation stores the list in a cookie, which isn't available for a headless frontend;
/// all other requests (storefront) keep using the cookie
/// </remarks>
public class HeadlessApiCompareProductsService : CompareProductsService
{
    #region Constants

    /// <summary>
    /// Gets the name of the generic attribute that stores the identifiers of the compared products (comma separated)
    /// </summary>
    public const string ComparedProductIdsAttribute = "HeadlessApi.ComparedProductIds";

    #endregion

    #region Fields

    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public HeadlessApiCompareProductsService(CatalogSettings catalogSettings,
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
    /// Gets the identifiers of the compared products of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product identifiers</returns>
    protected virtual async Task<List<int>> GetCustomerComparedProductIdsAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var value = await _genericAttributeService.GetAttributeAsync<string>(customer, ComparedProductIdsAttribute);
        if (string.IsNullOrEmpty(value))
            return new List<int>();

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => int.TryParse(id, out var productId) ? productId : 0)
            .Where(productId => productId > 0)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Saves the identifiers of the compared products of the current customer
    /// </summary>
    /// <param name="productIds">Product identifiers</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task SaveCustomerComparedProductIdsAsync(IEnumerable<int> productIds)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var value = string.Join(",", productIds ?? []);

        //an empty value removes the attribute
        await _genericAttributeService.SaveAttributeAsync(customer, ComparedProductIdsAttribute, value);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Clears a "compare products" list
    /// </summary>
    /// <remarks>
    /// The interface method is synchronous, so API clients should prefer <see cref="ClearCompareProductsAsync"/>
    /// </remarks>
    public override void ClearCompareProducts()
    {
        if (!IsApiRequest())
        {
            base.ClearCompareProducts();
            return;
        }

        ClearCompareProductsAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Clears a "compare products" list
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task ClearCompareProductsAsync()
    {
        if (!IsApiRequest())
        {
            base.ClearCompareProducts();
            return;
        }

        await SaveCustomerComparedProductIdsAsync([]);
    }

    /// <summary>
    /// Gets a "compare products" list
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the "Compare products" list</returns>
    public override async Task<IList<Product>> GetComparedProductsAsync()
    {
        if (!IsApiRequest())
            return await base.GetComparedProductsAsync();

        var productIds = await GetCustomerComparedProductIdsAsync();
        var products = await _productService.GetProductsByIdsAsync(productIds.ToArray());

        //keep the order of the list (most recently added first)
        return productIds
            .Select(productId => products.FirstOrDefault(product => product.Id == productId))
            .Where(product => product is { Published: true, Deleted: false })
            .ToList();
    }

    /// <summary>
    /// Removes a product from a "compare products" list
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task RemoveProductFromCompareListAsync(int productId)
    {
        if (!IsApiRequest())
        {
            await base.RemoveProductFromCompareListAsync(productId);
            return;
        }

        var productIds = await GetCustomerComparedProductIdsAsync();
        if (!productIds.Remove(productId))
            return;

        await SaveCustomerComparedProductIdsAsync(productIds);
    }

    /// <summary>
    /// Adds a product to a "compare products" list
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task AddProductToCompareListAsync(int productId)
    {
        if (!IsApiRequest())
        {
            await base.AddProductToCompareListAsync(productId);
            return;
        }

        var productIds = await GetCustomerComparedProductIdsAsync();
        if (!productIds.Contains(productId))
            productIds.Insert(0, productId);

        //limit list based on the allowed number of products to be compared
        await SaveCustomerComparedProductIdsAsync(productIds.Take(_catalogSettings.CompareProductsNumber));
    }

    #endregion
}

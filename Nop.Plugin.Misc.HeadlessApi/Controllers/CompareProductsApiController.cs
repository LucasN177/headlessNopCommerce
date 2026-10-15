using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the compare products endpoints
/// </summary>
/// <remarks>
/// Unlike the storefront (cookie), the list is stored per customer (see HeadlessApiCompareProductsService).
/// All endpoints return the updated list, so the frontend doesn't need to reload it
/// </remarks>
[HeadlessApiRoute("compare")]
public class CompareProductsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly ICompareProductsService _compareProductsService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IProductModelFactory _productModelFactory;
    protected readonly IProductService _productService;

    #endregion

    #region Ctor

    public CompareProductsApiController(CatalogSettings catalogSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        ICompareProductsService compareProductsService,
        ICustomerActivityService customerActivityService,
        ILocalizationService localizationService,
        IProductModelFactory productModelFactory,
        IProductService productService)
    {
        _catalogSettings = catalogSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _compareProductsService = compareProductsService;
        _customerActivityService = customerActivityService;
        _localizationService = localizationService;
        _productModelFactory = productModelFactory;
        _productService = productService;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Prepare the compare products model of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the compare products model</returns>
    protected virtual async Task<CompareProductsModel> PrepareCompareProductsModelAsync()
    {
        var model = new CompareProductsModel
        {
            IncludeShortDescriptionInCompareProducts = _catalogSettings.IncludeShortDescriptionInCompareProducts,
            IncludeFullDescriptionInCompareProducts = _catalogSettings.IncludeFullDescriptionInCompareProducts
        };

        //ACL, store mapping and availability dates
        var products = await (await _compareProductsService.GetComparedProductsAsync())
            .WhereAwait(async product => await _headlessApiCatalogService.IsProductAvailableAsync(product))
            .ToListAsync();

        foreach (var productModel in await _productModelFactory.PrepareProductOverviewModelsAsync(products, prepareSpecificationAttributes: true))
            model.Products.Add(productModel);

        return model;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the compared products of the current customer (including their specification attributes)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the compared products</returns>
    [HttpGet]
    [ProducesResponseType(typeof(CompareProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Get()
    {
        if (!_catalogSettings.CompareProductsEnabled)
            return NotFoundProblem();

        return Ok(await PrepareCompareProductsModelAsync());
    }

    /// <summary>
    /// Adds a product to the compare list
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated compared products</returns>
    [HttpPost("{productId:int}")]
    [ProducesResponseType(typeof(CompareProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Add(int productId)
    {
        if (!_catalogSettings.CompareProductsEnabled)
            return NotFoundProblem();

        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product) || !product.Published)
            return NotFoundProblem();

        await _compareProductsService.AddProductToCompareListAsync(product.Id);

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.AddToCompareList",
            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddToCompareList"), product.Name), product);

        return Ok(await PrepareCompareProductsModelAsync());
    }

    /// <summary>
    /// Removes a product from the compare list
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated compared products</returns>
    [HttpDelete("{productId:int}")]
    [ProducesResponseType(typeof(CompareProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Remove(int productId)
    {
        if (!_catalogSettings.CompareProductsEnabled)
            return NotFoundProblem();

        await _compareProductsService.RemoveProductFromCompareListAsync(productId);

        return Ok(await PrepareCompareProductsModelAsync());
    }

    /// <summary>
    /// Clears the compare list
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the (empty) compared products</returns>
    [HttpDelete]
    [ProducesResponseType(typeof(CompareProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Clear()
    {
        if (!_catalogSettings.CompareProductsEnabled)
            return NotFoundProblem();

        if (_compareProductsService is HeadlessApiCompareProductsService headlessApiCompareProductsService)
            await headlessApiCompareProductsService.ClearCompareProductsAsync();
        else
            _compareProductsService.ClearCompareProducts();

        return Ok(await PrepareCompareProductsModelAsync());
    }

    #endregion
}

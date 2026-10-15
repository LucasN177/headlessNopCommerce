using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.FilterLevels;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.HeadlessApi.Models.Catalog;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.FilterLevels;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the catalog endpoints (categories, manufacturers, vendors, product tags, search)
/// </summary>
/// <remarks>
/// Product lists support the same query parameters as the storefront (CatalogProductsCommand):
/// pageNumber, pageSize, orderBy, viewMode, price (e.g. "10-50"), specs (specification attribute option ids), ms (manufacturer ids)
/// </remarks>
[HeadlessApiRoute("catalog")]
public class CatalogApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly FilterLevelSettings _filterLevelSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly IAclService _aclService;
    protected readonly ICatalogModelFactory _catalogModelFactory;
    protected readonly ICategoryService _categoryService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly IFilterLevelValueModelFactory _filterLevelValueModelFactory;
    protected readonly IFilterLevelValueService _filterLevelValueService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IManufacturerService _manufacturerService;
    protected readonly IProductModelFactory _productModelFactory;
    protected readonly IProductService _productService;
    protected readonly IProductTagService _productTagService;
    protected readonly IStoreContext _storeContext;
    protected readonly IStoreMappingService _storeMappingService;
    protected readonly IVendorService _vendorService;
    protected readonly IWorkContext _workContext;
    protected readonly MediaSettings _mediaSettings;
    protected readonly VendorSettings _vendorSettings;

    #endregion

    #region Ctor

    public CatalogApiController(CatalogSettings catalogSettings,
        FilterLevelSettings filterLevelSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        IAclService aclService,
        ICatalogModelFactory catalogModelFactory,
        ICategoryService categoryService,
        ICustomerActivityService customerActivityService,
        IFilterLevelValueModelFactory filterLevelValueModelFactory,
        IFilterLevelValueService filterLevelValueService,
        ILocalizationService localizationService,
        IManufacturerService manufacturerService,
        IProductModelFactory productModelFactory,
        IProductService productService,
        IProductTagService productTagService,
        IStoreContext storeContext,
        IStoreMappingService storeMappingService,
        IVendorService vendorService,
        IWorkContext workContext,
        MediaSettings mediaSettings,
        VendorSettings vendorSettings)
    {
        _catalogSettings = catalogSettings;
        _filterLevelSettings = filterLevelSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _aclService = aclService;
        _catalogModelFactory = catalogModelFactory;
        _categoryService = categoryService;
        _customerActivityService = customerActivityService;
        _filterLevelValueModelFactory = filterLevelValueModelFactory;
        _filterLevelValueService = filterLevelValueService;
        _localizationService = localizationService;
        _manufacturerService = manufacturerService;
        _productModelFactory = productModelFactory;
        _productService = productService;
        _productTagService = productTagService;
        _storeContext = storeContext;
        _storeMappingService = storeMappingService;
        _vendorService = vendorService;
        _workContext = workContext;
        _mediaSettings = mediaSettings;
        _vendorSettings = vendorSettings;
    }

    #endregion

    #region Homepage

    /// <summary>
    /// Gets the categories displayed on the homepage
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the categories</returns>
    [HttpGet("homepage/categories")]
    [ProducesResponseType(typeof(List<CategoryModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> HomepageCategories()
    {
        return Ok(await _catalogModelFactory.PrepareHomepageCategoryModelsAsync());
    }

    /// <summary>
    /// Gets the products displayed on the homepage (featured products)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("homepage/products")]
    [ProducesResponseType(typeof(List<ProductOverviewModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> HomepageProducts()
    {
        var products = await (await _productService.GetAllProductsDisplayedOnHomepageAsync())
            .WhereAwait(async product => await _aclService.AuthorizeAsync(product) && await _storeMappingService.AuthorizeAsync(product))
            .Where(product => _productService.ProductIsAvailable(product) && product.VisibleIndividually)
            .ToListAsync();

        return Ok(await _productModelFactory.PrepareProductOverviewModelsAsync(products));
    }

    #endregion

    #region Categories

    /// <summary>
    /// Gets a category including its first page of products, subcategories and filters
    /// </summary>
    /// <param name="categoryId">Category identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the category</returns>
    [HttpGet("categories/{categoryId:int}")]
    [ProducesResponseType(typeof(CategoryModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Category(int categoryId, [FromQuery] CatalogProductsCommand command)
    {
        var category = await _categoryService.GetCategoryByIdAsync(categoryId);
        if (!await _headlessApiCatalogService.IsCategoryAvailableAsync(category))
            return NotFoundProblem();

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.ViewCategory",
            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.ViewCategory"), category.Name), category);

        return Ok(await _catalogModelFactory.PrepareCategoryModelAsync(category, command));
    }

    /// <summary>
    /// Gets the products of a category (e.g. for paging or filtering without reloading the category)
    /// </summary>
    /// <param name="categoryId">Category identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("categories/{categoryId:int}/products")]
    [ProducesResponseType(typeof(CatalogProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> CategoryProducts(int categoryId, [FromQuery] CatalogProductsCommand command)
    {
        var category = await _categoryService.GetCategoryByIdAsync(categoryId);
        if (!await _headlessApiCatalogService.IsCategoryAvailableAsync(category))
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareCategoryProductsModelAsync(category, command));
    }

    /// <summary>
    /// Gets the category navigation (category tree of the side column)
    /// </summary>
    /// <param name="currentCategoryId">Identifier of the currently displayed category; 0 if no category is displayed</param>
    /// <param name="currentProductId">Identifier of the currently displayed product (its category is highlighted); 0 if no product is displayed</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the category navigation</returns>
    [HttpGet("categories/navigation")]
    [ProducesResponseType(typeof(CategoryNavigationModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> CategoryNavigation([FromQuery] int currentCategoryId = 0, [FromQuery] int currentProductId = 0)
    {
        return Ok(await _catalogModelFactory.PrepareCategoryNavigationModelAsync(currentCategoryId, currentProductId));
    }

    #endregion

    #region Manufacturers

    /// <summary>
    /// Gets all manufacturers
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the manufacturers</returns>
    [HttpGet("manufacturers")]
    [ProducesResponseType(typeof(List<ManufacturerModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Manufacturers()
    {
        return Ok(await _catalogModelFactory.PrepareManufacturerAllModelsAsync());
    }

    /// <summary>
    /// Gets a manufacturer including its first page of products and filters
    /// </summary>
    /// <param name="manufacturerId">Manufacturer identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the manufacturer</returns>
    [HttpGet("manufacturers/{manufacturerId:int}")]
    [ProducesResponseType(typeof(ManufacturerModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Manufacturer(int manufacturerId, [FromQuery] CatalogProductsCommand command)
    {
        var manufacturer = await _manufacturerService.GetManufacturerByIdAsync(manufacturerId);
        if (!await _headlessApiCatalogService.IsManufacturerAvailableAsync(manufacturer))
            return NotFoundProblem();

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.ViewManufacturer",
            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.ViewManufacturer"), manufacturer.Name), manufacturer);

        return Ok(await _catalogModelFactory.PrepareManufacturerModelAsync(manufacturer, command));
    }

    /// <summary>
    /// Gets the products of a manufacturer
    /// </summary>
    /// <param name="manufacturerId">Manufacturer identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("manufacturers/{manufacturerId:int}/products")]
    [ProducesResponseType(typeof(CatalogProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ManufacturerProducts(int manufacturerId, [FromQuery] CatalogProductsCommand command)
    {
        var manufacturer = await _manufacturerService.GetManufacturerByIdAsync(manufacturerId);
        if (!await _headlessApiCatalogService.IsManufacturerAvailableAsync(manufacturer))
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareManufacturerProductsModelAsync(manufacturer, command));
    }

    /// <summary>
    /// Gets the manufacturer navigation (manufacturers block of the side column)
    /// </summary>
    /// <param name="currentManufacturerId">Identifier of the currently displayed manufacturer; 0 if no manufacturer is displayed</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the manufacturer navigation</returns>
    [HttpGet("manufacturers/navigation")]
    [ProducesResponseType(typeof(ManufacturerNavigationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ManufacturerNavigation([FromQuery] int currentManufacturerId = 0)
    {
        //the block is hidden
        if (_catalogSettings.ManufacturersBlockItemsToDisplay == 0)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareManufacturerNavigationModelAsync(currentManufacturerId));
    }

    #endregion

    #region Vendors

    /// <summary>
    /// Gets a vendor including its first page of products
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the vendor</returns>
    [HttpGet("vendors/{vendorId:int}")]
    [ProducesResponseType(typeof(VendorModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Vendor(int vendorId, [FromQuery] CatalogProductsCommand command)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (!_headlessApiCatalogService.IsVendorAvailable(vendor))
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareVendorModelAsync(vendor, command));
    }

    /// <summary>
    /// Gets the products of a vendor
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("vendors/{vendorId:int}/products")]
    [ProducesResponseType(typeof(CatalogProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> VendorProducts(int vendorId, [FromQuery] CatalogProductsCommand command)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (!_headlessApiCatalogService.IsVendorAvailable(vendor))
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareVendorProductsModelAsync(vendor, command));
    }

    /// <summary>
    /// Gets the reviews of the products of a vendor
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <param name="pageNumber">Page number (starting from 1)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product reviews</returns>
    [HttpGet("vendors/{vendorId:int}/reviews")]
    [ProducesResponseType(typeof(VendorProductReviewsListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> VendorReviews(int vendorId, [FromQuery] int pageNumber = 1)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (!_headlessApiCatalogService.IsVendorAvailable(vendor))
            return NotFoundProblem();

        //the page size is taken from the catalog settings
        var pagingModel = new VendorReviewsPagingFilteringModel { PageNumber = pageNumber };

        return Ok(await _catalogModelFactory.PrepareVendorProductReviewsModelAsync(vendor, pagingModel));
    }

    /// <summary>
    /// Gets all vendors
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the vendors</returns>
    [HttpGet("vendors")]
    [ProducesResponseType(typeof(List<VendorModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Vendors()
    {
        //we don't allow viewing of vendors if "vendors" block is hidden
        if (_vendorSettings.VendorsBlockItemsToDisplay == 0)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareVendorAllModelsAsync());
    }

    /// <summary>
    /// Gets the vendor navigation (vendors block of the side column)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the vendor navigation</returns>
    [HttpGet("vendors/navigation")]
    [ProducesResponseType(typeof(VendorNavigationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> VendorNavigation()
    {
        //the block is hidden
        if (_vendorSettings.VendorsBlockItemsToDisplay == 0)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareVendorNavigationModelAsync());
    }

    #endregion

    #region Product tags and new products

    /// <summary>
    /// Gets the popular product tags
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product tags</returns>
    [HttpGet("tags")]
    [ProducesResponseType(typeof(PopularProductTagsModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> ProductTags()
    {
        return Ok(await _catalogModelFactory.PreparePopularProductTagsModelAsync());
    }

    /// <summary>
    /// Gets the products tagged with a product tag
    /// </summary>
    /// <param name="productTagId">Product tag identifier</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("tags/{productTagId:int}")]
    [ProducesResponseType(typeof(ProductsByTagModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ProductsByTag(int productTagId, [FromQuery] CatalogProductsCommand command)
    {
        var productTag = await _productTagService.GetProductTagByIdAsync(productTagId);
        if (productTag == null)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareProductsByTagModelAsync(productTag, command));
    }

    /// <summary>
    /// Gets the recently added products
    /// </summary>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("new-products")]
    [ProducesResponseType(typeof(CatalogProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> NewProducts([FromQuery] CatalogProductsCommand command)
    {
        if (!_catalogSettings.NewProductsEnabled)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareNewProductsModelAsync(command));
    }

    #endregion

    #region Search

    /// <summary>
    /// Searches products
    /// </summary>
    /// <remarks>
    /// Query parameters as in the storefront: q (keywords), advs (advanced search), cid (category), isc (include subcategories),
    /// mid (manufacturer), vid (vendor), sid (search in descriptions), plus paging, sorting and filtering
    /// </remarks>
    /// <param name="model">Search parameters</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the search result</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Search([FromQuery] SearchModel model, [FromQuery] CatalogProductsCommand command)
    {
        return Ok(await _catalogModelFactory.PrepareSearchModelAsync(model ?? new SearchModel(), command));
    }

    /// <summary>
    /// Gets the products matching a search term (e.g. for a search box with suggestions)
    /// </summary>
    /// <param name="term">Search term</param>
    /// <param name="categoryId">Category identifier to search in (including subcategories); 0 to search in all categories</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the matching products</returns>
    [HttpGet("search/autocomplete")]
    [ProducesResponseType(typeof(List<ProductOverviewModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> SearchAutoComplete([FromQuery] string term, [FromQuery] int categoryId = 0)
    {
        term = term?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < _catalogSettings.ProductSearchTermMinimumLength)
            return Ok(new List<ProductOverviewModel>());

        var productNumber = _catalogSettings.ProductSearchAutoCompleteNumberOfProducts > 0
            ? _catalogSettings.ProductSearchAutoCompleteNumberOfProducts
            : 10;
        var store = await _storeContext.GetCurrentStoreAsync();

        var categoryIds = new List<int>();
        if (categoryId > 0)
            categoryIds.AddRange([categoryId, .. await _categoryService.GetChildCategoryIdsAsync(categoryId, store.Id)]);

        var products = await _productService.SearchProductsAsync(0,
            categoryIds: categoryIds,
            storeId: store.Id,
            keywords: term,
            languageId: (await _workContext.GetWorkingLanguageAsync()).Id,
            visibleIndividuallyOnly: true,
            pageSize: productNumber);

        return Ok(await _productModelFactory.PrepareProductOverviewModelsAsync(products,
            preparePriceModel: true,
            preparePictureModel: _catalogSettings.ShowProductImagesInSearchAutoComplete,
            productThumbPictureSize: _mediaSettings.AutoCompleteSearchThumbPictureSize));
    }

    #endregion

    #region Filter levels

    /// <summary>
    /// Gets the filter level values for a cascading selection (e.g. make, then model, then year)
    /// </summary>
    /// <remarks>
    /// Without parameters the level 1 values are returned; pass the selected level 1 value to get the level 2 values and so on
    /// </remarks>
    /// <param name="filterLevel1Value">Selected level 1 value</param>
    /// <param name="filterLevel2Value">Selected level 2 value</param>
    /// <param name="filterLevel3Value">Selected level 3 value</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the filter level values</returns>
    [HttpGet("filter-levels/values")]
    [ProducesResponseType(typeof(FilterLevelValuesModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> FilterLevelValues([FromQuery] string filterLevel1Value = "",
        [FromQuery] string filterLevel2Value = "", [FromQuery] string filterLevel3Value = "")
    {
        if (!_filterLevelSettings.FilterLevelEnabled)
            return NotFoundProblem();

        var values = await _filterLevelValueService.GetAllFilterLevelValuesAsync(filterLevel1Value, filterLevel2Value, filterLevel3Value);

        //the same cascade as in the storefront: return the values of the next level to select
        IEnumerable<(string Level1, string Level2, string Level3)> items;
        if (string.IsNullOrEmpty(filterLevel1Value))
            items = values.Select(value => (value.FilterLevel1Value, (string)null, (string)null));
        else if (string.IsNullOrEmpty(filterLevel2Value))
        {
            items = values
                .Where(value => value.FilterLevel1Value == filterLevel1Value)
                .Select(value => (value.FilterLevel1Value, value.FilterLevel2Value, (string)null));
        }
        else
        {
            items = values
                .Where(value => value.FilterLevel1Value == filterLevel1Value && value.FilterLevel2Value == filterLevel2Value)
                .Where(value => string.IsNullOrEmpty(filterLevel3Value) || value.FilterLevel3Value == filterLevel3Value)
                .Select(value => (value.FilterLevel1Value, value.FilterLevel2Value, value.FilterLevel3Value));
        }

        return Ok(new FilterLevelValuesModel
        {
            DefaultItemText = await _localizationService.GetResourceAsync("Admin.Common.Select"),
            Values = items.Distinct()
                .Select(item => new FilterLevelValueItemModel
                {
                    FilterLevel1Value = item.Level1,
                    FilterLevel2Value = item.Level2,
                    FilterLevel3Value = item.Level3
                })
                .ToList()
        });
    }

    /// <summary>
    /// Gets the filter level search form (e.g. the "find parts for your vehicle" block of the home page)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the search form model with the available level 1 values</returns>
    [HttpGet("filter-levels/search-form")]
    [ProducesResponseType(typeof(FilterLevelValueSearchModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> FilterLevelSearchForm()
    {
        if (!_filterLevelSettings.FilterLevelEnabled)
            return NotFoundProblem();

        return Ok(await _filterLevelValueModelFactory.PrepareFilterLevelValueSearchModelAsync(new FilterLevelValueSearchModel()));
    }

    /// <summary>
    /// Searches products by filter level values, including the available values and the first page of products
    /// </summary>
    /// <remarks>
    /// Query parameters as in the storefront: fl1id, fl2id, fl3id (selected filter level values), plus paging, sorting and filtering
    /// </remarks>
    /// <param name="model">Search parameters</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the search result</returns>
    [HttpGet("filter-levels/search")]
    [ProducesResponseType(typeof(SearchFilterLevelValueModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SearchByFilterLevelValues([FromQuery] SearchFilterLevelValueModel model, [FromQuery] CatalogProductsCommand command)
    {
        if (!_filterLevelSettings.FilterLevelEnabled)
            return NotFoundProblem();

        return Ok(await _filterLevelValueModelFactory.PrepareSearchFilterLevelValueModelAsync(model ?? new SearchFilterLevelValueModel(), command));
    }

    /// <summary>
    /// Gets the products matching the filter level values (e.g. for paging without reloading the search)
    /// </summary>
    /// <param name="model">Search parameters (fl1id, fl2id, fl3id)</param>
    /// <param name="command">Paging, sorting and filtering</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("filter-levels/search/products")]
    [ProducesResponseType(typeof(CatalogProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SearchProductsByFilterLevelValues([FromQuery] SearchFilterLevelValueModel model, [FromQuery] CatalogProductsCommand command)
    {
        if (!_filterLevelSettings.FilterLevelEnabled)
            return NotFoundProblem();

        return Ok(await _catalogModelFactory.PrepareSearchProductsByFilterLevelValuesModelAsync(model ?? new SearchFilterLevelValueModel(), command));
    }

    #endregion
}

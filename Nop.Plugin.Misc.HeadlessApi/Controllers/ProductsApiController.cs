using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.FilterLevels;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Shipping;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessApi.Models.Products;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.FilterLevels;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Factories;
using Nop.Web.Infrastructure.Cache;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the product endpoints
/// </summary>
[HeadlessApiRoute("products")]
public class ProductsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly FilterLevelSettings _filterLevelSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly HeadlessApiProductAttributeService _headlessApiProductAttributeService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ICustomerService _customerService;
    protected readonly IDownloadService _downloadService;
    protected readonly IFilterLevelValueModelFactory _filterLevelValueModelFactory;
    protected readonly IFilterLevelValueService _filterLevelValueService;
    protected readonly IHtmlFormatter _htmlFormatter;
    protected readonly ILocalizationService _localizationService;
    protected readonly INopFileProvider _fileProvider;
    protected readonly IOrderReportService _orderReportService;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly IProductModelFactory _productModelFactory;
    protected readonly IProductService _productService;
    protected readonly IRecentlyViewedProductsService _recentlyViewedProductsService;
    protected readonly IStaticCacheManager _staticCacheManager;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly ShippingSettings _shippingSettings;

    #endregion

    #region Ctor

    public ProductsApiController(CatalogSettings catalogSettings,
        FilterLevelSettings filterLevelSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        HeadlessApiProductAttributeService headlessApiProductAttributeService,
        ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IDownloadService downloadService,
        IFilterLevelValueModelFactory filterLevelValueModelFactory,
        IFilterLevelValueService filterLevelValueService,
        IHtmlFormatter htmlFormatter,
        ILocalizationService localizationService,
        INopFileProvider fileProvider,
        IOrderReportService orderReportService,
        IProductAttributeService productAttributeService,
        IProductModelFactory productModelFactory,
        IProductService productService,
        IRecentlyViewedProductsService recentlyViewedProductsService,
        IStaticCacheManager staticCacheManager,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        ShippingSettings shippingSettings)
    {
        _catalogSettings = catalogSettings;
        _filterLevelSettings = filterLevelSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _headlessApiProductAttributeService = headlessApiProductAttributeService;
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _downloadService = downloadService;
        _filterLevelValueModelFactory = filterLevelValueModelFactory;
        _filterLevelValueService = filterLevelValueService;
        _htmlFormatter = htmlFormatter;
        _localizationService = localizationService;
        _fileProvider = fileProvider;
        _orderReportService = orderReportService;
        _productAttributeService = productAttributeService;
        _productModelFactory = productModelFactory;
        _productService = productService;
        _recentlyViewedProductsService = recentlyViewedProductsService;
        _staticCacheManager = staticCacheManager;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _shippingSettings = shippingSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets the product if it's available for the current customer
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product or null if it isn't available</returns>
    protected virtual async Task<Product> GetAvailableProductAsync(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);

        return await _headlessApiCatalogService.IsProductAvailableAsync(product) ? product : null;
    }

    /// <summary>
    /// Filter the products available for the current customer
    /// </summary>
    /// <param name="products">Products</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the available products</returns>
    protected virtual async Task<List<Product>> FilterAvailableProductsAsync(IEnumerable<Product> products)
    {
        return await products
            .WhereAwait(async product => await _headlessApiCatalogService.IsProductAvailableAsync(product))
            .ToListAsync();
    }

    #endregion

    #region Product details

    /// <summary>
    /// Gets the product details
    /// </summary>
    /// <remarks>
    /// Associated products of a grouped product are not visible individually, they are included in the details of the grouped product.
    /// The product is added to the recently viewed products of the current customer.
    /// </remarks>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product details</returns>
    [HttpGet("{productId:int}")]
    [ProducesResponseType(typeof(ProductDetailsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Details(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product) || !product.VisibleIndividually)
            return NotFoundProblem();

        //save as recently viewed
        await _recentlyViewedProductsService.AddProductToRecentlyViewedListAsync(product.Id);

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.ViewProduct",
            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.ViewProduct"), product.Name), product);

        return Ok(await _productModelFactory.PrepareProductDetailsModelAsync(product));
    }

    /// <summary>
    /// Recalculates the product details (price, sku/mpn/gtin, stock availability, conditional attributes, pictures)
    /// after the customer changed the selected attributes
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the recalculated product details</returns>
    [HttpPost("{productId:int}/attributes/change")]
    [ProducesResponseType(typeof(ProductAttributeChangeModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> AttributeChange(int productId, ProductAttributeChangeRequest request)
    {
        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem();

        return Ok(await _headlessApiProductAttributeService.PrepareAttributeChangeModelAsync(product, request ?? new ProductAttributeChangeRequest()));
    }

    /// <summary>
    /// Gets the attribute combinations of the product (e.g. to disable out of stock combinations)
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the attribute combinations</returns>
    [HttpGet("{productId:int}/combinations")]
    [ProducesResponseType(typeof(IList<ProductCombinationModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Combinations(int productId)
    {
        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem();

        return Ok(await _productModelFactory.PrepareProductCombinationModelsAsync(product));
    }

    /// <summary>
    /// Estimates the shipping of the product with the selected attributes
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipping options (and errors of the shipping providers)</returns>
    [HttpPost("{productId:int}/estimate-shipping")]
    [ProducesResponseType(typeof(EstimateShippingResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> EstimateShipping(int productId, ProductEstimateShippingRequest request)
    {
        request ??= new ProductEstimateShippingRequest();

        var errors = new List<string>();

        if (!_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(request.ZipPostalCode))
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.ZipPostalCode.Required"));

        if (_shippingSettings.EstimateShippingCityNameEnabled && string.IsNullOrEmpty(request.City))
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.City.Required"));

        if (request.CountryId is null or 0)
            errors.Add(await _localizationService.GetResourceAsync("Shipping.EstimateShipping.Country.Required"));

        if (errors.Any())
            return WarningsProblem(errors);

        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem(await _localizationService.GetResourceAsync("Shipping.EstimateShippingPopUp.Product.IsNotFound"));

        return Ok(await _headlessApiProductAttributeService.EstimateShippingAsync(product, request));
    }

    /// <summary>
    /// Uploads a file for a "file upload" product attribute
    /// </summary>
    /// <remarks>
    /// Send the file as multipart/form-data; the returned download GUID has to be passed as value of the attribute
    /// (e.g. "product_attribute_5": "{downloadGuid}") when adding the product to the cart
    /// </remarks>
    /// <param name="productId">Product identifier</param>
    /// <param name="attributeId">Product attribute mapping identifier</param>
    /// <param name="file">File</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the download GUID</returns>
    [HttpPost("{productId:int}/attributes/{attributeId:int}/upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(UploadedFileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UploadAttributeFile(int productId, int attributeId, IFormFile file)
    {
        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem();

        var attribute = await _productAttributeService.GetProductAttributeMappingByIdAsync(attributeId);
        if (attribute == null || attribute.ProductId != product.Id || attribute.AttributeControlType != AttributeControlType.FileUpload)
            return NotFoundProblem();

        file ??= Request.HasFormContentType ? Request.Form.Files.FirstOrDefault() : null;
        if (file == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Upload.NoFile")]);

        var fileBinary = await _downloadService.GetDownloadBitsAsync(file);

        //remove path (passed in IE)
        var fileName = _fileProvider.GetFileName(file.FileName);

        var fileExtension = _fileProvider.GetFileExtension(fileName);
        if (!string.IsNullOrEmpty(fileExtension))
            fileExtension = fileExtension.ToLowerInvariant();

        //compare in bytes
        if (attribute.ValidationFileMaximumSize.HasValue && fileBinary.Length > attribute.ValidationFileMaximumSize.Value * 1024)
        {
            return WarningsProblem([string.Format(await _localizationService.GetResourceAsync("ShoppingCart.MaximumUploadedFileSize"),
                attribute.ValidationFileMaximumSize.Value)]);
        }

        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadUrl = string.Empty,
            DownloadBinary = fileBinary,
            ContentType = file.ContentType,
            //we store filename without extension for downloads
            Filename = _fileProvider.GetFileNameWithoutExtension(fileName),
            Extension = fileExtension,
            IsNew = true
        };
        await _downloadService.InsertDownloadAsync(download);

        return Ok(new UploadedFileModel
        {
            DownloadGuid = download.DownloadGuid,
            Message = await _localizationService.GetResourceAsync("ShoppingCart.FileUploaded")
        });
    }

    /// <summary>
    /// Gets the filter level values (e.g. make/model/year) the product is compatible with
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the filter level values</returns>
    [HttpGet("{productId:int}/filter-level-values")]
    [ProducesResponseType(typeof(FilterLevelValueOverviewModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> FilterLevelValues(int productId)
    {
        if (!_filterLevelSettings.FilterLevelEnabled || !_filterLevelSettings.DisplayOnProductDetailsPage)
            return NotFoundProblem();

        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem();

        var filterLevelValues = await _filterLevelValueService.GetFilterLevelValuesByProductIdAsync(product.Id);

        return Ok(_filterLevelValueModelFactory.PrepareFilterLevelValueOverviewModel(filterLevelValues));
    }

    #endregion

    #region Reviews and related products

    /// <summary>
    /// Gets the product reviews
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product reviews</returns>
    [HttpGet("{productId:int}/reviews")]
    [ProducesResponseType(typeof(ProductReviewsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Reviews(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product) || !product.AllowCustomerReviews)
            return NotFoundProblem();

        return Ok(await _productModelFactory.PrepareProductReviewsModelAsync(product));
    }

    /// <summary>
    /// Gets the related products
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the related products</returns>
    [HttpGet("{productId:int}/related")]
    [ProducesResponseType(typeof(List<ProductOverviewModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Related(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product))
            return NotFoundProblem();

        var productIds = (await _productService.GetRelatedProductsByProductId1Async(product.Id))
            .Select(relatedProduct => relatedProduct.ProductId2)
            .ToArray();
        var products = await (await _productService.GetProductsByIdsAsync(productIds))
            .WhereAwait(async relatedProduct => await _headlessApiCatalogService.IsProductAvailableAsync(relatedProduct))
            .Where(relatedProduct => relatedProduct.VisibleIndividually)
            .ToListAsync();

        return Ok(await _productModelFactory.PrepareProductOverviewModelsAsync(products));
    }

    /// <summary>
    /// Gets the products that customers who bought this product also bought
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="productThumbPictureSize">Product thumb picture size (longest side); null to use the default value of the media settings</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("{productId:int}/also-purchased")]
    [ProducesResponseType(typeof(List<ProductOverviewModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> AlsoPurchased(int productId, [FromQuery] int? productThumbPictureSize = null)
    {
        if (!_catalogSettings.ProductsAlsoPurchasedEnabled)
            return NotFoundProblem();

        var product = await GetAvailableProductAsync(productId);
        if (product == null)
            return NotFoundProblem();

        //load and cache report
        var store = await _storeContext.GetCurrentStoreAsync();
        var productIds = await _staticCacheManager.GetAsync(_staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.ProductsAlsoPurchasedIdsKey, product.Id, store),
            async () => await _orderReportService.GetAlsoPurchasedProductsIdsAsync(store.Id, product.Id, _catalogSettings.ProductsAlsoPurchasedNumber));

        var products = await FilterAvailableProductsAsync(await _productService.GetProductsByIdsAsync(productIds));

        return Ok(await _productModelFactory.PrepareProductOverviewModelsAsync(products, true, true, productThumbPictureSize));
    }

    /// <summary>
    /// Gets the products recently viewed by the current customer
    /// </summary>
    /// <param name="productThumbPictureSize">Product thumb picture size (longest side); null to use the default value of the media settings</param>
    /// <param name="preparePriceModel">Whether to prepare the prices</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the products</returns>
    [HttpGet("recently-viewed")]
    [ProducesResponseType(typeof(List<ProductOverviewModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> RecentlyViewed([FromQuery] int? productThumbPictureSize = null, [FromQuery] bool preparePriceModel = true)
    {
        if (!_catalogSettings.RecentlyViewedProductsEnabled)
            return NotFoundProblem();

        var products = await FilterAvailableProductsAsync(
            await _recentlyViewedProductsService.GetRecentlyViewedProductsAsync(_catalogSettings.RecentlyViewedProductsNumber));

        return Ok(await _productModelFactory.PrepareProductOverviewModelsAsync(products, preparePriceModel, true, productThumbPictureSize));
    }

    #endregion

    #region Email a friend

    /// <summary>
    /// Gets the "email a friend" form of the product (e.g. with the prefilled email address of the customer)
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form model</returns>
    [HttpGet("{productId:int}/email-a-friend")]
    [ProducesResponseType(typeof(ProductEmailAFriendModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> EmailAFriend(int productId)
    {
        var product = await GetAvailableProductAsync(productId);
        if (product is not { Published: true } || !_catalogSettings.EmailAFriendEnabled)
            return NotFoundProblem();

        return Ok(await _productModelFactory.PrepareProductEmailAFriendModelAsync(new ProductEmailAFriendModel(), product, false));
    }

    /// <summary>
    /// Sends the product to a friend by email
    /// </summary>
    /// <remarks>
    /// CAPTCHA is not supported by the API, so the CAPTCHA setting "Show on email product to friend page" is ignored
    /// </remarks>
    /// <param name="productId">Product identifier</param>
    /// <param name="model">Form model (friendEmail, yourEmailAddress, personalMessage)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form model with the result</returns>
    [HttpPost("{productId:int}/email-a-friend")]
    [ProducesResponseType(typeof(ProductEmailAFriendModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> EmailAFriendSend(int productId, ProductEmailAFriendModel model)
    {
        var product = await GetAvailableProductAsync(productId);
        if (product is not { Published: true } || !_catalogSettings.EmailAFriendEnabled)
            return NotFoundProblem();

        //check whether the current customer is guest and is allowed to email a friend
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsGuestAsync(customer) && !_catalogSettings.AllowAnonymousUsersToEmailAFriend)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Products.EmailAFriend.OnlyRegisteredUsers"));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await _workflowMessageService.SendProductEmailAFriendMessageAsync(customer,
            (await _workContext.GetWorkingLanguageAsync()).Id, product,
            model.YourEmailAddress, model.FriendEmail,
            _htmlFormatter.FormatText(model.PersonalMessage));

        model = await _productModelFactory.PrepareProductEmailAFriendModelAsync(model, product, true);
        model.SuccessfullySent = true;
        model.Result = await _localizationService.GetResourceAsync("Products.EmailAFriend.SuccessfullySent");

        return Ok(model);
    }

    #endregion
}

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HeadlessApi.Models.Products;
using Nop.Services.Catalog;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Tax;
using Nop.Web.Factories;
using Nop.Web.Infrastructure.Cache;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Media;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to handle the product attributes selected on the product details page
/// </summary>
/// <remarks>
/// The logic corresponds to ShoppingCartController.ProductDetails_AttributeChange and ProductController.EstimateShipping of the storefront;
/// the selected attributes are converted to a form collection, so the storefront parsers of nopCommerce can be used
/// </remarks>
public class HeadlessApiProductAttributeService
{
    #region Fields

    protected readonly ICurrencyService _currencyService;
    protected readonly IPermissionService _permissionService;
    protected readonly IPictureService _pictureService;
    protected readonly IPriceFormatter _priceFormatter;
    protected readonly IProductAttributeParser _productAttributeParser;
    protected readonly IProductAttributeService _productAttributeService;
    protected readonly IProductService _productService;
    protected readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStaticCacheManager _staticCacheManager;
    protected readonly IStoreContext _storeContext;
    protected readonly ITaxService _taxService;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;
    protected readonly MediaSettings _mediaSettings;

    #endregion

    #region Ctor

    public HeadlessApiProductAttributeService(ICurrencyService currencyService,
        IPermissionService permissionService,
        IPictureService pictureService,
        IPriceFormatter priceFormatter,
        IProductAttributeParser productAttributeParser,
        IProductAttributeService productAttributeService,
        IProductService productService,
        IShoppingCartModelFactory shoppingCartModelFactory,
        IShoppingCartService shoppingCartService,
        IStaticCacheManager staticCacheManager,
        IStoreContext storeContext,
        ITaxService taxService,
        IWebHelper webHelper,
        IWorkContext workContext,
        MediaSettings mediaSettings)
    {
        _currencyService = currencyService;
        _permissionService = permissionService;
        _pictureService = pictureService;
        _priceFormatter = priceFormatter;
        _productAttributeParser = productAttributeParser;
        _productAttributeService = productAttributeService;
        _productService = productService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _shoppingCartService = shoppingCartService;
        _staticCacheManager = staticCacheManager;
        _storeContext = storeContext;
        _taxService = taxService;
        _webHelper = webHelper;
        _workContext = workContext;
        _mediaSettings = mediaSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Convert the selected attributes to a form collection, so the storefront parsers of nopCommerce can be used
    /// </summary>
    /// <param name="product">Product</param>
    /// <param name="attributes">Selected attributes (storefront form keys and values)</param>
    /// <param name="quantity">Quantity; null to not pass the quantity</param>
    /// <param name="customerEnteredPrice">Price entered by the customer; null to not pass the price</param>
    /// <returns>Form collection</returns>
    protected virtual IFormCollection PrepareForm(Product product, IDictionary<string, string> attributes,
        int? quantity = null, decimal? customerEnteredPrice = null)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
            values[key] = value;

        if (quantity.HasValue)
            values[$"addtocart_{product.Id}.EnteredQuantity"] = quantity.Value.ToString(CultureInfo.InvariantCulture);

        //the storefront parser expects the price formatted with the current culture
        if (customerEnteredPrice.HasValue)
            values[$"addtocart_{product.Id}.CustomerEnteredPrice"] = customerEnteredPrice.Value.ToString(CultureInfo.CurrentCulture);

        return new FormCollection(values);
    }

    /// <summary>
    /// Prepare the price properties of the model
    /// </summary>
    /// <param name="model">Model</param>
    /// <param name="product">Product</param>
    /// <param name="attributeXml">Selected attributes (XML format)</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task PreparePriceAsync(ProductAttributeChangeModel model, Product product, string attributeXml,
        ProductAttributeChangeRequest request)
    {
        model.Price = string.Empty;
        model.BasePricePAngV = string.Empty;

        //we do not calculate price of "customer enters price" option is enabled
        if (product.CustomerEntersPrice || !await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.DISPLAY_PRICES))
            return;

        //weight adjustment for the base price
        var totalWeight = product.BasepriceAmount;
        foreach (var attributeValue in await _productAttributeParser.ParseProductAttributeValuesAsync(attributeXml))
        {
            switch (attributeValue.AttributeValueType)
            {
                case AttributeValueType.Simple:
                    totalWeight += attributeValue.WeightAdjustment;
                    break;
                case AttributeValueType.AssociatedToProduct:
                    var associatedProduct = await _productService.GetProductByIdAsync(attributeValue.AssociatedProductId);
                    if (associatedProduct != null)
                        totalWeight += associatedProduct.BasepriceAmount * attributeValue.Quantity;
                    break;
            }
        }

        var store = await _storeContext.GetCurrentStoreAsync();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var currency = await _workContext.GetWorkingCurrencyAsync();

        var rentalStartDate = product.IsRental ? request.RentalStartDate?.Date : null;
        var rentalEndDate = product.IsRental ? request.RentalEndDate?.Date : null;
        var (finalPrice, _, _) = await _shoppingCartService.GetUnitPriceAsync(product, customer, store, ShoppingCartType.ShoppingCart,
            1, attributeXml, 0, rentalStartDate, rentalEndDate, true);
        var (finalPriceWithDiscountBase, _) = await _taxService.GetProductPriceAsync(product, finalPrice);
        var finalPriceWithDiscount = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(finalPriceWithDiscountBase, currency);

        model.Price = await _priceFormatter.FormatPriceAsync(finalPriceWithDiscount);
        model.PriceValue = finalPriceWithDiscount;

        var basePrice = await _productService.GetBaseProductPriceAsync(product, finalPriceWithDiscount, totalWeight);
        model.BasePricePAngV = await _priceFormatter.FormatBasePriceAsync(product, basePrice);

        //old price (the same as on the product details page)
        var (oldPriceBase, _) = await _taxService.GetProductPriceAsync(product, product.OldPrice);
        if (oldPriceBase > decimal.Zero && oldPriceBase != finalPriceWithDiscountBase)
        {
            var oldPrice = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(oldPriceBase, currency);
            model.OldPrice = await _priceFormatter.FormatPriceAsync(oldPrice);
            model.OldPriceValue = oldPrice;
        }
    }

    /// <summary>
    /// Prepare the picture properties of the model
    /// </summary>
    /// <param name="model">Model</param>
    /// <param name="product">Product</param>
    /// <param name="attributeXml">Selected attributes (XML format)</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task PreparePictureAsync(ProductAttributeChangeModel model, Product product, string attributeXml)
    {
        //first, try to get product attribute combination picture
        var pictureId = 0;
        var combination = await _productAttributeParser.FindProductAttributeCombinationAsync(product, attributeXml);
        if (combination != null)
        {
            var combinationPictures = await _productAttributeService.GetProductAttributeCombinationPicturesAsync(combination.Id);
            model.PictureIds = combinationPictures.Select(picture => picture.PictureId).ToList();
            pictureId = combinationPictures.FirstOrDefault()?.PictureId ?? 0;
        }

        //then, let's see whether we have attribute values with pictures
        if (pictureId == 0)
        {
            var valuePictures = await (await _productAttributeParser.ParseProductAttributeValuesAsync(attributeXml))
                .SelectManyAwait(async attributeValue => await _productAttributeService.GetProductAttributeValuePicturesAsync(attributeValue.Id))
                .ToListAsync();
            model.PictureIds = valuePictures.Select(picture => picture.PictureId).ToList();
            pictureId = valuePictures.FirstOrDefault()?.PictureId ?? 0;
        }

        if (pictureId == 0)
            return;

        var cacheKey = _staticCacheManager.PrepareKeyForDefaultCache(NopModelCacheDefaults.ProductAttributePictureModelKey,
            pictureId, _webHelper.IsCurrentConnectionSecured(), await _storeContext.GetCurrentStoreAsync());
        var pictureModel = await _staticCacheManager.GetAsync(cacheKey, async () =>
        {
            var picture = await _pictureService.GetPictureByIdAsync(pictureId);
            string fullSizeImageUrl, imageUrl;

            (fullSizeImageUrl, picture) = await _pictureService.GetPictureUrlAsync(picture);
            (imageUrl, picture) = await _pictureService.GetPictureUrlAsync(picture, _mediaSettings.ProductDetailsPictureSize);

            return picture == null ? new PictureModel() : new PictureModel
            {
                FullSizeImageUrl = fullSizeImageUrl,
                ImageUrl = imageUrl
            };
        });

        model.PictureFullSizeUrl = pictureModel.FullSizeImageUrl;
        model.PictureDefaultSizeUrl = pictureModel.ImageUrl;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Prepare the product details for the selected attributes (price, sku/mpn/gtin, stock, conditional attributes, pictures)
    /// </summary>
    /// <param name="product">Product</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the model</returns>
    public virtual async Task<ProductAttributeChangeModel> PrepareAttributeChangeModelAsync(Product product, ProductAttributeChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var form = PrepareForm(product, request.Attributes);
        var attributeXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, errors);

        var model = new ProductAttributeChangeModel
        {
            ProductId = product.Id,
            Sku = await _productService.FormatSkuAsync(product, attributeXml),
            Mpn = await _productService.FormatMpnAsync(product, attributeXml),
            Gtin = await _productService.FormatGtinAsync(product, attributeXml),
            StockAvailability = await _productService.FormatStockMessageAsync(product, attributeXml),
            Messages = errors
        };

        await PreparePriceAsync(model, product, attributeXml, request);

        //conditional attributes
        if (request.ValidateAttributeConditions)
        {
            foreach (var attribute in await _productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id))
            {
                var conditionMet = await _productAttributeParser.IsConditionMetAsync(attribute, attributeXml);
                if (!conditionMet.HasValue)
                    continue;

                if (conditionMet.Value)
                    model.EnabledAttributeMappingIds.Add(attribute.Id);
                else
                    model.DisabledAttributeMappingIds.Add(attribute.Id);
            }
        }

        //picture. used when we want to override a default product picture when some attribute is selected
        if (request.LoadPicture)
            await PreparePictureAsync(model, product, attributeXml);

        model.IsFreeShipping = product.IsFreeShipping;
        if (model.IsFreeShipping && !string.IsNullOrEmpty(attributeXml))
        {
            model.IsFreeShipping = await (await _productAttributeParser.ParseProductAttributeValuesAsync(attributeXml))
                .Where(attributeValue => attributeValue.AttributeValueType == AttributeValueType.AssociatedToProduct)
                .SelectAwait(async attributeValue => await _productService.GetProductByIdAsync(attributeValue.AssociatedProductId))
                .AllAsync(associatedProduct => associatedProduct == null || !associatedProduct.IsShipEnabled || associatedProduct.IsFreeShipping);
        }

        return model;
    }

    /// <summary>
    /// Estimate the shipping of the product with the selected attributes
    /// </summary>
    /// <param name="product">Product</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipping options and errors</returns>
    public virtual async Task<EstimateShippingResultModel> EstimateShippingAsync(Product product, ProductEstimateShippingRequest request)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(request);

        var store = await _storeContext.GetCurrentStoreAsync();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var form = PrepareForm(product, request.Attributes, request.Quantity, request.CustomerEnteredPrice);

        //the product is wrapped in a (not saved) shopping cart item
        var wrappedProduct = new ShoppingCartItem
        {
            StoreId = store.Id,
            ShoppingCartTypeId = (int)ShoppingCartType.ShoppingCart,
            CustomerId = customer.Id,
            ProductId = product.Id,
            CreatedOnUtc = DateTime.UtcNow,
            CustomerEnteredPrice = await _productAttributeParser.ParseCustomerEnteredPriceAsync(product, form),
            Quantity = _productAttributeParser.ParseEnteredQuantity(product, form),
            AttributesXml = await _productAttributeParser.ParseProductAttributesAsync(product, form, new List<string>()),
            RentalStartDateUtc = product.IsRental ? request.RentalStartDate?.Date : null,
            RentalEndDateUtc = product.IsRental ? request.RentalEndDate?.Date : null
        };

        var estimateShippingModel = new ProductDetailsModel.ProductEstimateShippingModel
        {
            ProductId = product.Id,
            CountryId = request.CountryId,
            StateProvinceId = request.StateProvinceId,
            ZipPostalCode = request.ZipPostalCode,
            City = request.City
        };

        return await _shoppingCartModelFactory.PrepareEstimateShippingResultModelAsync([wrappedProduct], estimateShippingModel, false);
    }

    #endregion
}

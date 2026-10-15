using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Topics;
using Nop.Core.Domain.Vendors;
using Nop.Services.Catalog;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Services.Topics;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to check whether catalog entities are available for the current customer
/// </summary>
/// <remarks>
/// The checks correspond to the storefront controllers, except the admin preview of unpublished entities
/// </remarks>
public class HeadlessApiCatalogService
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly IAclService _aclService;
    protected readonly IProductService _productService;
    protected readonly IStoreMappingService _storeMappingService;
    protected readonly ITopicService _topicService;

    #endregion

    #region Ctor

    public HeadlessApiCatalogService(CatalogSettings catalogSettings,
        IAclService aclService,
        IProductService productService,
        IStoreMappingService storeMappingService,
        ITopicService topicService)
    {
        _catalogSettings = catalogSettings;
        _aclService = aclService;
        _productService = productService;
        _storeMappingService = storeMappingService;
        _topicService = topicService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Check whether the category is available
    /// </summary>
    /// <param name="category">Category</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the category is available</returns>
    public virtual async Task<bool> IsCategoryAvailableAsync(Category category)
    {
        return category is { Deleted: false, Published: true }
            && await _aclService.AuthorizeAsync(category)
            && await _storeMappingService.AuthorizeAsync(category);
    }

    /// <summary>
    /// Check whether the manufacturer is available
    /// </summary>
    /// <param name="manufacturer">Manufacturer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the manufacturer is available</returns>
    public virtual async Task<bool> IsManufacturerAvailableAsync(Manufacturer manufacturer)
    {
        return manufacturer is { Deleted: false, Published: true }
            && await _aclService.AuthorizeAsync(manufacturer)
            && await _storeMappingService.AuthorizeAsync(manufacturer);
    }

    /// <summary>
    /// Check whether the vendor is available
    /// </summary>
    /// <param name="vendor">Vendor</param>
    /// <returns>The value indicating whether the vendor is available</returns>
    public virtual bool IsVendorAvailable(Vendor vendor)
    {
        return vendor is { Deleted: false, Active: true };
    }

    /// <summary>
    /// Check whether the product is available
    /// </summary>
    /// <param name="product">Product</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the product is available</returns>
    public virtual async Task<bool> IsProductAvailableAsync(Product product)
    {
        return product is { Deleted: false }
            && (product.Published || _catalogSettings.AllowViewUnpublishedProductPage)
            && await _aclService.AuthorizeAsync(product)
            && await _storeMappingService.AuthorizeAsync(product)
            && _productService.ProductIsAvailable(product);
    }

    /// <summary>
    /// Check whether the topic is available
    /// </summary>
    /// <param name="topic">Topic</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the topic is available</returns>
    public virtual async Task<bool> IsTopicAvailableAsync(Topic topic)
    {
        return topic is { Published: true }
            && _topicService.TopicIsAvailable(topic)
            && await _aclService.AuthorizeAsync(topic)
            && await _storeMappingService.AuthorizeAsync(topic);
    }

    #endregion
}

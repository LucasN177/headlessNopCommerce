using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Products;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Seo;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the back in stock subscription endpoints
/// </summary>
/// <remarks>
/// The routes are defined on the actions, because they belong to the product (products/...) and to the customer account (account/...)
/// </remarks>
[HeadlessApiRoute]
public class BackInStockSubscriptionsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly CustomerSettings _customerSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly IBackInStockSubscriptionService _backInStockSubscriptionService;
    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly IUrlRecordService _urlRecordService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public BackInStockSubscriptionsApiController(CatalogSettings catalogSettings,
        CustomerSettings customerSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        IBackInStockSubscriptionService backInStockSubscriptionService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        IProductService productService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWorkContext workContext)
    {
        _catalogSettings = catalogSettings;
        _customerSettings = customerSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _backInStockSubscriptionService = backInStockSubscriptionService;
        _customerService = customerService;
        _localizationService = localizationService;
        _productService = productService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether a back in stock subscription is possible for the product (out of stock and subscriptions allowed)
    /// </summary>
    /// <param name="product">Product</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether a subscription is possible</returns>
    protected virtual async Task<bool> IsSubscriptionAllowedAsync(Product product)
    {
        return product.ManageInventoryMethod == ManageInventoryMethod.ManageStock &&
            product.BackorderMode == BackorderMode.NoBackorders &&
            product.AllowBackInStockSubscriptions &&
            await _productService.GetTotalStockQuantityAsync(product) <= 0;
    }

    /// <summary>
    /// Prepare the back in stock subscriptions model of the current customer
    /// </summary>
    /// <param name="page">Page number (starting from 1)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the subscriptions model</returns>
    protected virtual async Task<CustomerBackInStockSubscriptionsModel> PrepareCustomerSubscriptionsModelAsync(int? page)
    {
        var pageIndex = page > 0 ? page.Value - 1 : 0;
        var pageSize = 10;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var list = await _backInStockSubscriptionService.GetAllSubscriptionsByCustomerIdAsync(customer.Id, store.Id, pageIndex, pageSize);

        var model = new CustomerBackInStockSubscriptionsModel();

        foreach (var subscription in list)
        {
            var product = await _productService.GetProductByIdAsync(subscription.ProductId);
            if (product == null)
                continue;

            model.Subscriptions.Add(new CustomerBackInStockSubscriptionsModel.BackInStockSubscriptionModel
            {
                Id = subscription.Id,
                ProductId = product.Id,
                ProductName = await _localizationService.GetLocalizedAsync(product, x => x.Name),
                SeName = await _urlRecordService.GetSeNameAsync(product)
            });
        }

        model.PagerModel = new PagerModel(_localizationService)
        {
            PageSize = list.PageSize,
            TotalRecords = list.TotalCount,
            PageIndex = list.PageIndex,
            ShowTotalSummary = false
        };

        return model;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the back in stock subscription status of the current customer for the product
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the subscription status</returns>
    [HttpGet("products/{productId:int}/back-in-stock-subscription")]
    [ProducesResponseType(typeof(BackInStockSubscribeModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Status(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product))
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        var model = new BackInStockSubscribeModel
        {
            ProductId = product.Id,
            ProductName = await _localizationService.GetLocalizedAsync(product, x => x.Name),
            ProductSeName = await _urlRecordService.GetSeNameAsync(product),
            IsCurrentCustomerRegistered = await _customerService.IsRegisteredAsync(customer),
            MaximumBackInStockSubscriptions = _catalogSettings.MaximumBackInStockSubscriptions,
            CurrentNumberOfBackInStockSubscriptions = (await _backInStockSubscriptionService
                .GetAllSubscriptionsByCustomerIdAsync(customer.Id, store.Id, 0, 1)).TotalCount
        };

        if (await IsSubscriptionAllowedAsync(product))
        {
            //out of stock
            model.SubscriptionAllowed = true;
            model.AlreadySubscribed = await _backInStockSubscriptionService.FindSubscriptionAsync(customer.Id, product.Id, store.Id) != null;
        }

        return Ok(model);
    }

    /// <summary>
    /// Subscribes the current customer to the back in stock notification of the product, or unsubscribes if already subscribed
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the new subscription status</returns>
    [HttpPost("products/{productId:int}/back-in-stock-subscription")]
    [ProducesResponseType(typeof(BackInStockSubscriptionResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Toggle(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product))
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsRegisteredAsync(customer))
            return ForbiddenProblem(await _localizationService.GetResourceAsync("BackInStockSubscriptions.OnlyRegistered"));

        //subscription not possible
        if (!await IsSubscriptionAllowedAsync(product))
            return WarningsProblem([await _localizationService.GetResourceAsync("BackInStockSubscriptions.NotAllowed")]);

        var store = await _storeContext.GetCurrentStoreAsync();
        var subscription = await _backInStockSubscriptionService.FindSubscriptionAsync(customer.Id, product.Id, store.Id);
        if (subscription != null)
        {
            //subscription already exists, so unsubscribe
            await _backInStockSubscriptionService.DeleteSubscriptionAsync(subscription);

            return Ok(new BackInStockSubscriptionResultModel
            {
                ProductId = product.Id,
                Subscribed = false,
                Message = await _localizationService.GetResourceAsync("BackInStockSubscriptions.Notification.Unsubscribed")
            });
        }

        //subscription does not exist, so subscribe
        var currentNumber = (await _backInStockSubscriptionService.GetAllSubscriptionsByCustomerIdAsync(customer.Id, store.Id, 0, 1)).TotalCount;
        if (currentNumber >= _catalogSettings.MaximumBackInStockSubscriptions)
        {
            return WarningsProblem([string.Format(await _localizationService.GetResourceAsync("BackInStockSubscriptions.MaxSubscriptions"),
                _catalogSettings.MaximumBackInStockSubscriptions)]);
        }

        await _backInStockSubscriptionService.InsertSubscriptionAsync(new BackInStockSubscription
        {
            CustomerId = customer.Id,
            ProductId = product.Id,
            StoreId = store.Id,
            CreatedOnUtc = DateTime.UtcNow
        });

        return Ok(new BackInStockSubscriptionResultModel
        {
            ProductId = product.Id,
            Subscribed = true,
            Message = await _localizationService.GetResourceAsync("BackInStockSubscriptions.Notification.Subscribed")
        });
    }

    /// <summary>
    /// Gets the back in stock subscriptions of the current customer
    /// </summary>
    /// <param name="page">Page number (starting from 1)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the subscriptions</returns>
    [HttpGet("account/back-in-stock-subscriptions")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerBackInStockSubscriptionsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> CustomerSubscriptions([FromQuery] int? page = null)
    {
        if (_customerSettings.HideBackInStockSubscriptionsTab)
            return NotFoundProblem();

        return Ok(await PrepareCustomerSubscriptionsModelAsync(page));
    }

    /// <summary>
    /// Deletes a back in stock subscription of the current customer
    /// </summary>
    /// <param name="subscriptionId">Subscription identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated subscriptions (first page)</returns>
    [HttpDelete("account/back-in-stock-subscriptions/{subscriptionId:int}")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerBackInStockSubscriptionsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> DeleteCustomerSubscription(int subscriptionId)
    {
        if (_customerSettings.HideBackInStockSubscriptionsTab)
            return NotFoundProblem();

        //ensure that the subscription belongs to the current customer
        var subscription = await _backInStockSubscriptionService.GetSubscriptionByIdAsync(subscriptionId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (subscription == null || subscription.CustomerId != customer.Id)
            return NotFoundProblem();

        await _backInStockSubscriptionService.DeleteSubscriptionAsync(subscription);

        return Ok(await PrepareCustomerSubscriptionsModelAsync(null));
    }

    #endregion
}

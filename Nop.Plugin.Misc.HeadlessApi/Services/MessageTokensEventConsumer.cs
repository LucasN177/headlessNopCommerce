using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Stores;
using Nop.Core.Events;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Events;
using Nop.Services.Messages;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the event consumer that replaces the storefront links in email messages with links to the frontend
/// </summary>
/// <remarks>
/// Links are only replaced if the frontend URL and the corresponding template are configured and "Rewrite email links" is enabled
/// </remarks>
public class MessageTokensEventConsumer :
    IConsumer<EntityTokensAddedEvent<BackInStockSubscription>>,
    IConsumer<EntityTokensAddedEvent<Customer>>,
    IConsumer<EntityTokensAddedEvent<NewsLetterSubscription>>,
    IConsumer<EntityTokensAddedEvent<Order>>,
    IConsumer<EntityTokensAddedEvent<Product>>,
    IConsumer<EntityTokensAddedEvent<Shipment>>,
    IConsumer<EntityTokensAddedEvent<Store>>
{
    #region Fields

    protected readonly HeadlessApiFrontendUrlService _headlessApiFrontendUrlService;
    protected readonly HeadlessApiSettings _headlessApiSettings;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IProductService _productService;
    protected readonly IUrlRecordService _urlRecordService;

    #endregion

    #region Ctor

    public MessageTokensEventConsumer(HeadlessApiFrontendUrlService headlessApiFrontendUrlService,
        HeadlessApiSettings headlessApiSettings,
        IGenericAttributeService genericAttributeService,
        IProductService productService,
        IUrlRecordService urlRecordService)
    {
        _headlessApiFrontendUrlService = headlessApiFrontendUrlService;
        _headlessApiSettings = headlessApiSettings;
        _genericAttributeService = genericAttributeService;
        _productService = productService;
        _urlRecordService = urlRecordService;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets a value indicating whether the email links should be replaced
    /// </summary>
    protected virtual bool IsEnabled => _headlessApiSettings.Enabled && _headlessApiSettings.RewriteEmailLinks && _headlessApiFrontendUrlService.IsConfigured;

    /// <summary>
    /// Replace the value of a token with the URL built from the template
    /// </summary>
    /// <param name="tokens">Tokens</param>
    /// <param name="key">Token key</param>
    /// <param name="template">URL template</param>
    /// <param name="values">Placeholder values</param>
    protected virtual void ReplaceToken(IList<Token> tokens, string key, string template, IDictionary<string, object> values = null)
    {
        var url = _headlessApiFrontendUrlService.BuildUrl(template, values);
        if (string.IsNullOrEmpty(url))
            return;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (string.Equals(tokens[i].Key, key, StringComparison.InvariantCultureIgnoreCase))
                tokens[i] = new Token(key, url, true);
        }
    }

    /// <summary>
    /// Replace the product URL token
    /// </summary>
    /// <param name="tokens">Tokens</param>
    /// <param name="key">Token key</param>
    /// <param name="product">Product</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task ReplaceProductTokenAsync(IList<Token> tokens, string key, Product product)
    {
        if (product == null)
            return;

        ReplaceToken(tokens, key, _headlessApiSettings.ProductUrlTemplate, new Dictionary<string, object>
        {
            ["seName"] = await _urlRecordService.GetSeNameAsync(product),
            ["productId"] = product.Id
        });
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle the customer tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleEventAsync(EntityTokensAddedEvent<Customer> eventMessage)
    {
        if (!IsEnabled)
            return;

        var customer = eventMessage.Entity;
        async Task<Dictionary<string, object>> getValuesAsync(string tokenAttribute) => new()
        {
            ["token"] = await _genericAttributeService.GetAttributeAsync<string>(customer, tokenAttribute),
            ["guid"] = customer.CustomerGuid,
            ["email"] = customer.Email
        };

        ReplaceToken(eventMessage.Tokens, "Customer.PasswordRecoveryURL", _headlessApiSettings.PasswordRecoveryUrlTemplate,
            await getValuesAsync(NopCustomerDefaults.PasswordRecoveryTokenAttribute));
        ReplaceToken(eventMessage.Tokens, "Customer.AccountActivationURL", _headlessApiSettings.AccountActivationUrlTemplate,
            await getValuesAsync(NopCustomerDefaults.AccountActivationTokenAttribute));
        ReplaceToken(eventMessage.Tokens, "Customer.EmailRevalidationURL", _headlessApiSettings.EmailRevalidationUrlTemplate,
            await getValuesAsync(NopCustomerDefaults.EmailRevalidationTokenAttribute));
    }

    /// <summary>
    /// Handle the newsletter subscription tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task HandleEventAsync(EntityTokensAddedEvent<NewsLetterSubscription> eventMessage)
    {
        if (!IsEnabled)
            return Task.CompletedTask;

        var token = eventMessage.Entity.NewsLetterSubscriptionGuid;
        ReplaceToken(eventMessage.Tokens, "NewsLetterSubscription.ActivationUrl", _headlessApiSettings.NewsletterActivationUrlTemplate,
            new Dictionary<string, object> { ["token"] = token, ["active"] = "true" });
        ReplaceToken(eventMessage.Tokens, "NewsLetterSubscription.DeactivationUrl", _headlessApiSettings.NewsletterActivationUrlTemplate,
            new Dictionary<string, object> { ["token"] = token, ["active"] = "false" });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle the order tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task HandleEventAsync(EntityTokensAddedEvent<Order> eventMessage)
    {
        if (IsEnabled)
        {
            ReplaceToken(eventMessage.Tokens, "Order.OrderURLForCustomer", _headlessApiSettings.OrderDetailsUrlTemplate,
                new Dictionary<string, object> { ["orderId"] = eventMessage.Entity.Id });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle the shipment tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task HandleEventAsync(EntityTokensAddedEvent<Shipment> eventMessage)
    {
        if (IsEnabled)
        {
            ReplaceToken(eventMessage.Tokens, "Shipment.URLForCustomer", _headlessApiSettings.ShipmentDetailsUrlTemplate,
                new Dictionary<string, object> { ["shipmentId"] = eventMessage.Entity.Id });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle the product tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleEventAsync(EntityTokensAddedEvent<Product> eventMessage)
    {
        if (IsEnabled)
            await ReplaceProductTokenAsync(eventMessage.Tokens, "Product.ProductURLForCustomer", eventMessage.Entity);
    }

    /// <summary>
    /// Handle the back in stock subscription tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleEventAsync(EntityTokensAddedEvent<BackInStockSubscription> eventMessage)
    {
        if (!IsEnabled)
            return;

        var product = await _productService.GetProductByIdAsync(eventMessage.Entity.ProductId);
        await ReplaceProductTokenAsync(eventMessage.Tokens, "BackInStockSubscription.ProductUrl", product);
    }

    /// <summary>
    /// Handle the store tokens added event
    /// </summary>
    /// <param name="eventMessage">Event message</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public Task HandleEventAsync(EntityTokensAddedEvent<Store> eventMessage)
    {
        if (IsEnabled)
            ReplaceToken(eventMessage.Tokens, "Store.URL", "/");

        return Task.CompletedTask;
    }

    #endregion
}

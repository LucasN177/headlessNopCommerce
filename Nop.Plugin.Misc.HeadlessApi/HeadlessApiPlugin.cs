using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Plugins;

namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents the headless API plugin
/// </summary>
public class HeadlessApiPlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly ISettingService _settingService;
    protected readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public HeadlessApiPlugin(ILocalizationService localizationService,
        ISettingService settingService,
        IWebHelper webHelper)
    {
        _localizationService = localizationService;
        _settingService = settingService;
        _webHelper = webHelper;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/HeadlessApiAdmin/Configure";
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new HeadlessApiSettings
        {
            Enabled = false,
            AllowedOrigins = string.Empty,
            TokenSigningKey = HeadlessApiTokenService.GenerateSigningKey(),
            TokenLifetimeDays = 30,
            FrontendUrl = string.Empty,
            RewriteEmailLinks = true,
            RedirectStorefrontPagesToFrontend = true,
            PasswordRecoveryUrlTemplate = "/password-recovery?token={token}&guid={guid}",
            AccountActivationUrlTemplate = "/account/activation?token={token}&guid={guid}",
            EmailRevalidationUrlTemplate = "/account/email-revalidation?token={token}&guid={guid}",
            NewsletterActivationUrlTemplate = "/newsletter/activation?token={token}&active={active}",
            OrderDetailsUrlTemplate = "/account/orders/{orderId}",
            ShipmentDetailsUrlTemplate = "/account/shipments/{shipmentId}",
            ProductUrlTemplate = "/{seName}",
            CheckoutCompletedUrlTemplate = "/checkout/completed/{orderId}",
            ShoppingCartUrlTemplate = "/cart"
        });

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Misc.HeadlessApi.Instructions"] = "<p>This plugin provides a JSON API for a separate (headless) frontend. All endpoints are available under <code>/{0}</code>, e.g. <code>/{0}/status</code>.</p><p>The frontend gets a guest token via <code>POST /{0}/auth/guest</code> and sends it with every request in the header <code>Authorization: Bearer {{token}}</code>. On login (<code>POST /{0}/auth/login</code>) the guest's shopping cart is migrated and a new token is issued.</p>",
            ["Plugins.Misc.HeadlessApi.Fields.Enabled"] = "Enabled",
            ["Plugins.Misc.HeadlessApi.Fields.Enabled.Hint"] = "Check to enable the API endpoints. When disabled, all API requests return 404 (Not Found).",
            ["Plugins.Misc.HeadlessApi.Fields.AllowedOrigins"] = "Allowed origins (CORS)",
            ["Plugins.Misc.HeadlessApi.Fields.AllowedOrigins.Hint"] = "Enter a comma-separated list of origins (e.g. https://shop.example.com) of the frontend applications allowed to call the API from a browser. Leave empty to deny cross-origin requests.",
            ["Plugins.Misc.HeadlessApi.Fields.TokenLifetimeDays"] = "Token lifetime (days)",
            ["Plugins.Misc.HeadlessApi.Fields.TokenLifetimeDays.Hint"] = "Enter the number of days an API token is valid. The frontend can renew a token before it expires.",
            ["Plugins.Misc.HeadlessApi.Fields.TokenLifetimeDays.Range"] = "The token lifetime must be between 1 and 365 days.",
            ["Plugins.Misc.HeadlessApi.Fields.FrontendUrl.Invalid"] = "Enter an absolute URL, e.g. https://shop.example.com.",
            ["Plugins.Misc.HeadlessApi.Frontend"] = "Frontend",
            ["Plugins.Misc.HeadlessApi.Frontend.Instructions"] = "Links in emails and redirects of the storefront (e.g. after a payment) can point to the pages of the frontend. Templates are paths relative to the frontend URL; placeholders in curly braces are replaced with the actual values. Leave a template empty to keep the storefront link.",
            ["Plugins.Misc.HeadlessApi.Fields.FrontendUrl"] = "Frontend URL",
            ["Plugins.Misc.HeadlessApi.Fields.FrontendUrl.Hint"] = "Enter the base URL of the frontend (e.g. https://shop.example.com). Links and redirects are only changed if it's specified.",
            ["Plugins.Misc.HeadlessApi.Fields.RewriteEmailLinks"] = "Email links to the frontend",
            ["Plugins.Misc.HeadlessApi.Fields.RewriteEmailLinks.Hint"] = "Check to let the links in email messages (password recovery, account activation, order details, products, store URL, etc.) point to the frontend.",
            ["Plugins.Misc.HeadlessApi.Fields.RedirectStorefrontPagesToFrontend"] = "Redirect storefront pages to the frontend",
            ["Plugins.Misc.HeadlessApi.Fields.RedirectStorefrontPagesToFrontend.Hint"] = "Check to redirect customers to the frontend when the storefront redirects them to the checkout completed, order details or shopping cart page, e.g. when they return from a payment provider.",
            ["Plugins.Misc.HeadlessApi.Fields.PasswordRecoveryUrlTemplate"] = "Password recovery page",
            ["Plugins.Misc.HeadlessApi.Fields.PasswordRecoveryUrlTemplate.Hint"] = "Path of the page to set a new password. Placeholders: {token}, {guid}, {email}.",
            ["Plugins.Misc.HeadlessApi.Fields.AccountActivationUrlTemplate"] = "Account activation page",
            ["Plugins.Misc.HeadlessApi.Fields.AccountActivationUrlTemplate.Hint"] = "Path of the page to activate an account (email validation). Placeholders: {token}, {guid}, {email}.",
            ["Plugins.Misc.HeadlessApi.Fields.EmailRevalidationUrlTemplate"] = "Email revalidation page",
            ["Plugins.Misc.HeadlessApi.Fields.EmailRevalidationUrlTemplate.Hint"] = "Path of the page to confirm a changed email address. Placeholders: {token}, {guid}, {email}.",
            ["Plugins.Misc.HeadlessApi.Fields.NewsletterActivationUrlTemplate"] = "Newsletter activation page",
            ["Plugins.Misc.HeadlessApi.Fields.NewsletterActivationUrlTemplate.Hint"] = "Path of the page to confirm or cancel a newsletter subscription. Placeholders: {token}, {active}.",
            ["Plugins.Misc.HeadlessApi.Fields.OrderDetailsUrlTemplate"] = "Order details page",
            ["Plugins.Misc.HeadlessApi.Fields.OrderDetailsUrlTemplate.Hint"] = "Path of the order details page. Placeholders: {orderId}.",
            ["Plugins.Misc.HeadlessApi.Fields.ShipmentDetailsUrlTemplate"] = "Shipment details page",
            ["Plugins.Misc.HeadlessApi.Fields.ShipmentDetailsUrlTemplate.Hint"] = "Path of the shipment details page. Placeholders: {shipmentId}.",
            ["Plugins.Misc.HeadlessApi.Fields.ProductUrlTemplate"] = "Product page",
            ["Plugins.Misc.HeadlessApi.Fields.ProductUrlTemplate.Hint"] = "Path of the product details page. Placeholders: {seName}, {productId}.",
            ["Plugins.Misc.HeadlessApi.Fields.CheckoutCompletedUrlTemplate"] = "Checkout completed page",
            ["Plugins.Misc.HeadlessApi.Fields.CheckoutCompletedUrlTemplate.Hint"] = "Path of the page shown after an order has been placed, also used as return URL after payments. Placeholders: {orderId}.",
            ["Plugins.Misc.HeadlessApi.Fields.ShoppingCartUrlTemplate"] = "Shopping cart page",
            ["Plugins.Misc.HeadlessApi.Fields.ShoppingCartUrlTemplate.Hint"] = "Path of the shopping cart page, e.g. used when a payment has been canceled.",
            ["Plugins.Misc.HeadlessApi.RegenerateKey"] ="Regenerate token signing key",
            ["Plugins.Misc.HeadlessApi.RegenerateKey.Confirm"] = "All issued tokens become invalid, so all API clients (including logged in customers) have to request new tokens. Continue?",
            ["Plugins.Misc.HeadlessApi.RegenerateKey.Success"] = "The token signing key has been regenerated. All previously issued tokens are invalid now.",
            ["Plugins.Misc.HeadlessApi.Auth.MultiFactorAuthenticationNotSupported"] = "Multi-factor authentication is enabled for this account, but not supported by the API yet.",
            ["Plugins.Misc.HeadlessApi.Context.LanguageNotAvailable"] = "The language is not available.",
            ["Plugins.Misc.HeadlessApi.Context.CurrencyNotAvailable"] = "The currency is not available.",
            ["Plugins.Misc.HeadlessApi.NavigationNotAllowed"] = "You are not allowed to access the public store.",
            ["Plugins.Misc.HeadlessApi.RegisteredCustomerRequired"] = "Please log in to use this function.",
            ["Plugins.Misc.HeadlessApi.ShoppingCart.Disabled"] = "The shopping cart is not available.",
            ["Plugins.Misc.HeadlessApi.ShoppingCart.OnlySimpleProducts"] = "Only simple products can be added to the cart.",
            ["Plugins.Misc.HeadlessApi.Wishlist.Disabled"] = "The wishlist is not available.",
            ["Plugins.Misc.HeadlessApi.Account.AlreadyRegistered"] = "You are already registered.",
            ["Plugins.Misc.HeadlessApi.FeatureDisabled"] = "This feature is not available.",
            ["Plugins.Misc.HeadlessApi.Account.InvalidLink"] = "The link is invalid or has expired.",
            ["Plugins.Misc.HeadlessApi.ReturnRequests.NotAllowed"] = "Return requests are not allowed for this order.",
            ["Plugins.Misc.HeadlessApi.ReturnRequests.UseOrderHistory"] = "Registered customers can submit return requests from their order history.",
            ["Plugins.Misc.HeadlessApi.Downloads.NotAvailable"] = "The download is not available any more.",
            ["Plugins.Misc.HeadlessApi.Downloads.NotAllowed"] = "Downloads are not allowed.",
            ["Plugins.Misc.HeadlessApi.Downloads.NotYourOrder"] = "This is not your order.",
            ["Plugins.Misc.HeadlessApi.Downloads.UserAgreementRequired"] = "Please accept the user agreement to download this product.",
            ["Plugins.Misc.HeadlessApi.Wishlist.NameRequired"] = "Please enter a wishlist name.",
            ["Plugins.Misc.HeadlessApi.Upload.NoFile"] = "No file uploaded.",
            ["Plugins.Misc.HeadlessApi.Upload.ExtensionNotAllowed"] = "The file extension is not allowed. Allowed extensions: {0}",
            ["Plugins.Misc.HeadlessApi.Context.TaxTypeSelectionNotAllowed"] = "Customers are not allowed to select the tax display type.",
            ["Plugins.Misc.HeadlessApi.Context.TaxTypeNotAvailable"] = "The tax display type is not available.",
            ["Plugins.Misc.HeadlessApi.Common.EuCookieLawDisabled"] = "The EU cookie law warning is disabled.",
            ["Plugins.Misc.HeadlessApi.Common.SitemapDisabled"] = "The sitemap is disabled.",
            ["Plugins.Misc.HeadlessApi.ContactVendor.Disabled"] = "Customers are not allowed to contact vendors.",
            ["Plugins.Misc.HeadlessApi.Blog.Disabled"] = "The blog is disabled.",
            ["Plugins.Misc.HeadlessApi.Blog.WrongMonth"] = "The month must have the format yyyy-M, e.g. 2026-10.",
            ["Plugins.Misc.HeadlessApi.Blog.CommentsNotAllowed"] = "Comments are not allowed for this blog post.",
            ["Plugins.Misc.HeadlessApi.ShoppingCart.MiniCartDisabled"] = "The mini shopping cart is disabled.",
            ["Plugins.Misc.HeadlessApi.ShoppingCart.VendorCartDisabled"] = "The vendor selection in the shopping cart is disabled.",
            ["Plugins.Misc.HeadlessApi.ShoppingCart.CheckoutAttributes.NotFileUpload"] = "The checkout attribute does not exist or is not a file upload attribute.",
            ["Plugins.Misc.HeadlessApi.Checkout.AddressRequired"] ="Please select an existing address or enter a new one.",
            ["Plugins.Misc.HeadlessApi.Checkout.ButtonPaymentMethodsOnly"] = "Only payment methods displayed on the shopping cart page are available, the checkout is not possible.",
            ["Plugins.Misc.HeadlessApi.Checkout.ShippingNotRequired"] = "Shipping is not required for the shopping cart.",
            ["Plugins.Misc.HeadlessApi.Checkout.PickupPointNotAvailable"] = "The selected pickup point is not available.",
            ["Plugins.Misc.HeadlessApi.Checkout.ShippingMethodNotAvailable"] = "The selected shipping method is not available.",
            ["Plugins.Misc.HeadlessApi.Checkout.PaymentMethodNotAvailable"] = "The selected payment method is not available.",
            ["Plugins.Misc.HeadlessApi.Checkout.PaymentMethodNotSelected"] = "Please select a payment method first.",
            ["Plugins.Misc.HeadlessApi.Checkout.PaymentInfoRequired"] = "Please enter the payment information first.",
            ["Plugins.Misc.HeadlessApi.Checkout.RePostPaymentNotPossible"] = "The payment of this order can't be repeated."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<HeadlessApiSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.HeadlessApi");

        await base.UninstallAsync();
    }

    #endregion
}

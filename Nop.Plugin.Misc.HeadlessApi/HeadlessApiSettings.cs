using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.HeadlessApi;

/// <summary>
/// Represents plugin settings
/// </summary>
public class HeadlessApiSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the API endpoints are enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated list of origins (e.g. https://shop.example.com) allowed to call the API from a browser
    /// </summary>
    public string AllowedOrigins { get; set; }

    /// <summary>
    /// Gets or sets the key (Base64) used to sign the API tokens
    /// </summary>
    public string TokenSigningKey { get; set; }

    /// <summary>
    /// Gets or sets the token lifetime in days
    /// </summary>
    public int TokenLifetimeDays { get; set; }

    #region Frontend URLs

    /// <summary>
    /// Gets or sets the base URL of the frontend (e.g. https://shop.example.com); links and redirects are only rewritten if it's set
    /// </summary>
    public string FrontendUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether links in emails (password recovery, account activation, order details, etc.) point to the frontend
    /// </summary>
    public bool RewriteEmailLinks { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether redirects of the storefront to the checkout completed, order details and cart pages
    /// (e.g. when a customer returns from a payment provider) are redirected to the frontend
    /// </summary>
    public bool RedirectStorefrontPagesToFrontend { get; set; }

    /// <summary>
    /// Gets or sets the path template of the password recovery page; placeholders: {token}, {guid}, {email}
    /// </summary>
    public string PasswordRecoveryUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the account activation page; placeholders: {token}, {guid}, {email}
    /// </summary>
    public string AccountActivationUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the email revalidation page; placeholders: {token}, {guid}, {email}
    /// </summary>
    public string EmailRevalidationUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the newsletter activation page; placeholders: {token}, {active}
    /// </summary>
    public string NewsletterActivationUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the order details page; placeholders: {orderId}
    /// </summary>
    public string OrderDetailsUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the shipment details page; placeholders: {shipmentId}
    /// </summary>
    public string ShipmentDetailsUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the product details page; placeholders: {seName}, {productId}
    /// </summary>
    public string ProductUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the checkout completed page; placeholders: {orderId}
    /// </summary>
    public string CheckoutCompletedUrlTemplate { get; set; }

    /// <summary>
    /// Gets or sets the path template of the shopping cart page
    /// </summary>
    public string ShoppingCartUrlTemplate { get; set; }

    #endregion
}

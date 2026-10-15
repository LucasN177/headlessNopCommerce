using System.Text.RegularExpressions;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to build URLs of the frontend pages from the configured templates
/// </summary>
public partial class HeadlessApiFrontendUrlService
{
    #region Fields

    protected readonly HeadlessApiSettings _headlessApiSettings;

    #endregion

    #region Ctor

    public HeadlessApiFrontendUrlService(HeadlessApiSettings headlessApiSettings)
    {
        _headlessApiSettings = headlessApiSettings;
    }

    #endregion

    #region Utilities

    [GeneratedRegex(@"\{(?<name>[a-zA-Z]+)\}")]
    private static partial Regex PlaceholderRegex();

    #endregion

    #region Methods

    /// <summary>
    /// Gets a value indicating whether the frontend URL is configured
    /// </summary>
    public virtual bool IsConfigured => !string.IsNullOrWhiteSpace(_headlessApiSettings.FrontendUrl);

    /// <summary>
    /// Builds an absolute URL of a frontend page
    /// </summary>
    /// <param name="template">Path template, e.g. "/account/orders/{orderId}"</param>
    /// <param name="values">Placeholder values; they are URL encoded</param>
    /// <returns>Absolute URL or null if the frontend URL or the template is not configured</returns>
    public virtual string BuildUrl(string template, IDictionary<string, object> values = null)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(template))
            return null;

        var path = PlaceholderRegex().Replace(template.Trim(), match =>
        {
            var name = match.Groups["name"].Value;
            var value = values?.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

            return Uri.EscapeDataString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        });

        return $"{_headlessApiSettings.FrontendUrl.Trim().TrimEnd('/')}/{path.TrimStart('/')}";
    }

    /// <summary>
    /// Gets the URL of the frontend home page
    /// </summary>
    /// <returns>URL or null if the frontend URL is not configured</returns>
    public virtual string GetHomeUrl()
    {
        return IsConfigured ? $"{_headlessApiSettings.FrontendUrl.Trim().TrimEnd('/')}/" : null;
    }

    /// <summary>
    /// Gets the URL of the checkout completed page
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>URL or null if not configured</returns>
    public virtual string GetCheckoutCompletedUrl(int orderId)
    {
        return BuildUrl(_headlessApiSettings.CheckoutCompletedUrlTemplate, new Dictionary<string, object> { ["orderId"] = orderId });
    }

    /// <summary>
    /// Gets the URL of the order details page
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>URL or null if not configured</returns>
    public virtual string GetOrderDetailsUrl(int orderId)
    {
        return BuildUrl(_headlessApiSettings.OrderDetailsUrlTemplate, new Dictionary<string, object> { ["orderId"] = orderId });
    }

    /// <summary>
    /// Gets the URL of the shopping cart page
    /// </summary>
    /// <returns>URL or null if not configured</returns>
    public virtual string GetShoppingCartUrl()
    {
        return BuildUrl(_headlessApiSettings.ShoppingCartUrlTemplate);
    }

    #endregion
}

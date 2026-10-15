using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Plugin.Misc.HeadlessApi.Services;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Represents object for the configuring the middleware that redirects storefront pages to the frontend
/// </summary>
/// <remarks>
/// Payment plugins send customers back to the storefront (e.g. checkout/completed/{orderId} or orderdetails/{orderId})
/// after the payment; with "Redirect storefront pages to the frontend" these redirects lead to the frontend instead
/// </remarks>
public partial class FrontendRedirectStartup : INopStartup
{
    #region Utilities

    [GeneratedRegex(@"^(?:/[a-zA-Z]{2})?/checkout/completed(?:/(?<orderId>\d+))?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex CheckoutCompletedRegex();

    [GeneratedRegex(@"^(?:/[a-zA-Z]{2})?/orderdetails/(?<orderId>\d+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex OrderDetailsRegex();

    [GeneratedRegex(@"^(?:/[a-zA-Z]{2})?/cart/?$", RegexOptions.IgnoreCase)]
    private static partial Regex ShoppingCartRegex();

    /// <summary>
    /// Get the frontend URL for a storefront path
    /// </summary>
    /// <param name="path">Storefront path</param>
    /// <param name="frontendUrlService">Frontend URL service</param>
    /// <returns>Frontend URL or null if the path is not redirected</returns>
    private static string GetFrontendUrl(string path, HeadlessApiFrontendUrlService frontendUrlService)
    {
        var match = CheckoutCompletedRegex().Match(path);
        if (match.Success)
        {
            return int.TryParse(match.Groups["orderId"].Value, out var orderId)
                ? frontendUrlService.GetCheckoutCompletedUrl(orderId)
                : frontendUrlService.GetHomeUrl();
        }

        match = OrderDetailsRegex().Match(path);
        if (match.Success)
            return frontendUrlService.GetOrderDetailsUrl(int.Parse(match.Groups["orderId"].Value));

        if (ShoppingCartRegex().IsMatch(path))
            return frontendUrlService.GetShoppingCartUrl();

        return null;
    }

    /// <summary>
    /// Rewrite the redirect location of the response if it targets a storefront page that is handled by the frontend
    /// </summary>
    /// <param name="context">HTTP context</param>
    private static void RewriteRedirect(HttpContext context)
    {
        var response = context.Response;
        if (response.StatusCode is < 300 or >= 400 || string.IsNullOrEmpty(response.Headers.Location))
            return;

        var settings = context.RequestServices.GetService<HeadlessApiSettings>();
        if (settings is not { Enabled: true, RedirectStorefrontPagesToFrontend: true })
            return;

        //only local redirects
        if (!Uri.TryCreate(response.Headers.Location.ToString(), UriKind.RelativeOrAbsolute, out var location))
            return;
        if (location.IsAbsoluteUri && !string.Equals(location.Host, context.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            return;

        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
        var frontendUrl = GetFrontendUrl(path, context.RequestServices.GetRequiredService<HeadlessApiFrontendUrlService>());
        if (!string.IsNullOrEmpty(frontendUrl))
            response.Headers.Location = frontendUrl;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Add and configure any of the middleware
    /// </summary>
    /// <param name="services">Collection of service descriptors</param>
    /// <param name="configuration">Configuration of the application</param>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    /// <summary>
    /// Configure the using of added middleware
    /// </summary>
    /// <param name="application">Builder for configuring an application's request pipeline</param>
    public void Configure(IApplicationBuilder application)
    {
        application.Use(async (context, next) =>
        {
            if (DataSettingsManager.IsDatabaseInstalled()
                && !context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)
                && !context.Request.Path.StartsWithSegments(HeadlessApiDefaults.BasePath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.OnStarting(() =>
                {
                    RewriteRedirect(context);
                    return Task.CompletedTask;
                });
            }

            await next();
        });
    }

    /// <summary>
    /// Gets order of this startup configuration implementation
    /// </summary>
    public int Order => 410; //after the routing, before the endpoints

    #endregion
}

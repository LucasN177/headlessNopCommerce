using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Misc.HeadlessApi.Models;

/// <summary>
/// Represents the plugin configuration model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.Enabled")]
    public bool Enabled { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.AllowedOrigins")]
    public string AllowedOrigins { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.TokenLifetimeDays")]
    public int TokenLifetimeDays { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.FrontendUrl")]
    public string FrontendUrl { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.RewriteEmailLinks")]
    public bool RewriteEmailLinks { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.RedirectStorefrontPagesToFrontend")]
    public bool RedirectStorefrontPagesToFrontend { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.PasswordRecoveryUrlTemplate")]
    public string PasswordRecoveryUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.AccountActivationUrlTemplate")]
    public string AccountActivationUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.EmailRevalidationUrlTemplate")]
    public string EmailRevalidationUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.NewsletterActivationUrlTemplate")]
    public string NewsletterActivationUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.OrderDetailsUrlTemplate")]
    public string OrderDetailsUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.ShipmentDetailsUrlTemplate")]
    public string ShipmentDetailsUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.ProductUrlTemplate")]
    public string ProductUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.CheckoutCompletedUrlTemplate")]
    public string CheckoutCompletedUrlTemplate { get; set; }

    [NopResourceDisplayName("Plugins.Misc.HeadlessApi.Fields.ShoppingCartUrlTemplate")]
    public string ShoppingCartUrlTemplate { get; set; }
}

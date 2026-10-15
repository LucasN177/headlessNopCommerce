using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class HeadlessApiAdminController : BasePluginController
{
    #region Fields

    protected readonly HeadlessApiSettings _headlessApiSettings;
    protected readonly ILocalizationService _localizationService;
    protected readonly INotificationService _notificationService;
    protected readonly ISettingService _settingService;

    #endregion

    #region Ctor

    public HeadlessApiAdminController(HeadlessApiSettings headlessApiSettings,
        ILocalizationService localizationService,
        INotificationService notificationService,
        ISettingService settingService)
    {
        _headlessApiSettings = headlessApiSettings;
        _localizationService = localizationService;
        _notificationService = notificationService;
        _settingService = settingService;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public virtual IActionResult Configure()
    {
        var model = new ConfigurationModel
        {
            Enabled = _headlessApiSettings.Enabled,
            AllowedOrigins = _headlessApiSettings.AllowedOrigins,
            TokenLifetimeDays = _headlessApiSettings.TokenLifetimeDays,
            FrontendUrl = _headlessApiSettings.FrontendUrl,
            RewriteEmailLinks = _headlessApiSettings.RewriteEmailLinks,
            RedirectStorefrontPagesToFrontend = _headlessApiSettings.RedirectStorefrontPagesToFrontend,
            PasswordRecoveryUrlTemplate = _headlessApiSettings.PasswordRecoveryUrlTemplate,
            AccountActivationUrlTemplate = _headlessApiSettings.AccountActivationUrlTemplate,
            EmailRevalidationUrlTemplate = _headlessApiSettings.EmailRevalidationUrlTemplate,
            NewsletterActivationUrlTemplate = _headlessApiSettings.NewsletterActivationUrlTemplate,
            OrderDetailsUrlTemplate = _headlessApiSettings.OrderDetailsUrlTemplate,
            ShipmentDetailsUrlTemplate = _headlessApiSettings.ShipmentDetailsUrlTemplate,
            ProductUrlTemplate = _headlessApiSettings.ProductUrlTemplate,
            CheckoutCompletedUrlTemplate = _headlessApiSettings.CheckoutCompletedUrlTemplate,
            ShoppingCartUrlTemplate = _headlessApiSettings.ShoppingCartUrlTemplate
        };

        return View("~/Plugins/Misc.HeadlessApi/Views/Configure.cshtml", model);
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("save")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public virtual async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return View("~/Plugins/Misc.HeadlessApi/Views/Configure.cshtml", model);

        _headlessApiSettings.Enabled = model.Enabled;
        _headlessApiSettings.AllowedOrigins = model.AllowedOrigins?.Trim() ?? string.Empty;
        _headlessApiSettings.TokenLifetimeDays = model.TokenLifetimeDays;
        _headlessApiSettings.FrontendUrl = model.FrontendUrl?.Trim().TrimEnd('/') ?? string.Empty;
        _headlessApiSettings.RewriteEmailLinks = model.RewriteEmailLinks;
        _headlessApiSettings.RedirectStorefrontPagesToFrontend = model.RedirectStorefrontPagesToFrontend;
        _headlessApiSettings.PasswordRecoveryUrlTemplate = model.PasswordRecoveryUrlTemplate?.Trim();
        _headlessApiSettings.AccountActivationUrlTemplate = model.AccountActivationUrlTemplate?.Trim();
        _headlessApiSettings.EmailRevalidationUrlTemplate = model.EmailRevalidationUrlTemplate?.Trim();
        _headlessApiSettings.NewsletterActivationUrlTemplate = model.NewsletterActivationUrlTemplate?.Trim();
        _headlessApiSettings.OrderDetailsUrlTemplate = model.OrderDetailsUrlTemplate?.Trim();
        _headlessApiSettings.ShipmentDetailsUrlTemplate = model.ShipmentDetailsUrlTemplate?.Trim();
        _headlessApiSettings.ProductUrlTemplate = model.ProductUrlTemplate?.Trim();
        _headlessApiSettings.CheckoutCompletedUrlTemplate = model.CheckoutCompletedUrlTemplate?.Trim();
        _headlessApiSettings.ShoppingCartUrlTemplate = model.ShoppingCartUrlTemplate?.Trim();
        await _settingService.SaveSettingAsync(_headlessApiSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return Configure();
    }

    [HttpPost, ActionName("Configure")]
    [FormValueRequired("regenerateKey")]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public virtual async Task<IActionResult> RegenerateKey()
    {
        _headlessApiSettings.TokenSigningKey = HeadlessApiTokenService.GenerateSigningKey();
        await _settingService.SaveSettingAsync(_headlessApiSettings, settings => settings.TokenSigningKey);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.RegenerateKey.Success"));

        return Configure();
    }

    #endregion
}

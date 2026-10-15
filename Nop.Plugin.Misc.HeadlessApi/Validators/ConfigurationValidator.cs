using FluentValidation;
using Nop.Plugin.Misc.HeadlessApi.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Misc.HeadlessApi.Validators;

/// <summary>
/// Represents the configuration model validator
/// </summary>
public class ConfigurationValidator : BaseNopValidator<ConfigurationModel>
{
    #region Ctor

    public ConfigurationValidator(ILocalizationService localizationService)
    {
        RuleFor(model => model.TokenLifetimeDays)
            .InclusiveBetween(1, 365)
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Fields.TokenLifetimeDays.Range"));

        RuleFor(model => model.FrontendUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .When(model => !string.IsNullOrWhiteSpace(model.FrontendUrl))
            .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Fields.FrontendUrl.Invalid"));
    }

    #endregion
}

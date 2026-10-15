using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Nop.Core.Domain;
using Nop.Services.Localization;
using Nop.Services.Security;

namespace Nop.Plugin.Misc.HeadlessApi.Filters;

/// <summary>
/// Represents a filter attribute that checks whether the current customer has access to the public store
/// (store closed, "Public store. Allow navigation" permission), like the storefront controllers do
/// </summary>
public sealed class HeadlessApiStoreAccessAttribute : TypeFilterAttribute
{
    #region Ctor

    /// <summary>
    /// Create instance of the filter attribute
    /// </summary>
    /// <param name="ignore">Whether to ignore the execution of filter actions (e.g. for login or registration)</param>
    public HeadlessApiStoreAccessAttribute(bool ignore = false) : base(typeof(HeadlessApiStoreAccessFilter))
    {
        IgnoreFilter = ignore;
        Arguments = [ignore];
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to ignore the execution of filter actions
    /// </summary>
    public bool IgnoreFilter { get; }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that checks the access to the public store
    /// </summary>
    private class HeadlessApiStoreAccessFilter : IAsyncActionFilter
    {
        #region Fields

        private readonly bool _ignoreFilter;
        private readonly ILocalizationService _localizationService;
        private readonly IPermissionService _permissionService;
        private readonly ProblemDetailsFactory _problemDetailsFactory;
        private readonly StoreInformationSettings _storeInformationSettings;

        #endregion

        #region Ctor

        public HeadlessApiStoreAccessFilter(bool ignoreFilter,
            ILocalizationService localizationService,
            IPermissionService permissionService,
            ProblemDetailsFactory problemDetailsFactory,
            StoreInformationSettings storeInformationSettings)
        {
            _ignoreFilter = ignoreFilter;
            _localizationService = localizationService;
            _permissionService = permissionService;
            _problemDetailsFactory = problemDetailsFactory;
            _storeInformationSettings = storeInformationSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets a value indicating whether to skip the check for the current action
        /// </summary>
        /// <param name="context">Action context</param>
        private bool IgnoreFilter(ActionExecutingContext context)
        {
            //anonymous endpoints don't have a customer (and must not create a guest one)
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
                return true;

            //check whether this filter has been overridden for the action
            var actionFilter = context.ActionDescriptor.FilterDescriptors
                .Where(filterDescriptor => filterDescriptor.Scope == FilterScope.Action)
                .Select(filterDescriptor => filterDescriptor.Filter)
                .OfType<HeadlessApiStoreAccessAttribute>()
                .FirstOrDefault();

            return actionFilter?.IgnoreFilter ?? _ignoreFilter;
        }

        /// <summary>
        /// Create a problem result
        /// </summary>
        /// <param name="context">Action context</param>
        /// <param name="statusCode">Status code</param>
        /// <param name="detail">Detail</param>
        private IActionResult Problem(ActionExecutingContext context, int statusCode, string detail)
        {
            var problemDetails = _problemDetailsFactory.CreateProblemDetails(context.HttpContext, statusCode, detail: detail);

            return new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called asynchronously before the action, after model binding is complete
        /// </summary>
        /// <param name="context">Action context</param>
        /// <param name="next">Action execution delegate</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!IgnoreFilter(context))
            {
                if (_storeInformationSettings.StoreClosed && !await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ACCESS_CLOSED_STORE))
                {
                    context.Result = Problem(context, StatusCodes.Status503ServiceUnavailable, await _localizationService.GetResourceAsync("StoreClosed"));
                    return;
                }

                if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.PUBLIC_STORE_ALLOW_NAVIGATION))
                {
                    context.Result = Problem(context, StatusCodes.Status403Forbidden, await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.NavigationNotAllowed"));
                    return;
                }
            }

            await next();
        }

        #endregion
    }

    #endregion
}

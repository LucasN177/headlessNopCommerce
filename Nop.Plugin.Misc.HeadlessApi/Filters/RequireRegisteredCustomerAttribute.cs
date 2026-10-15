using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Nop.Core;
using Nop.Services.Customers;
using Nop.Services.Localization;

namespace Nop.Plugin.Misc.HeadlessApi.Filters;

/// <summary>
/// Represents a filter attribute that allows the action only for registered customers (not for guests)
/// </summary>
public sealed class RequireRegisteredCustomerAttribute : TypeFilterAttribute
{
    #region Ctor

    public RequireRegisteredCustomerAttribute() : base(typeof(RequireRegisteredCustomerFilter))
    {
    }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that checks whether the current customer is registered
    /// </summary>
    private class RequireRegisteredCustomerFilter : IAsyncActionFilter
    {
        #region Fields

        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly ProblemDetailsFactory _problemDetailsFactory;

        #endregion

        #region Ctor

        public RequireRegisteredCustomerFilter(ICustomerService customerService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            ProblemDetailsFactory problemDetailsFactory)
        {
            _customerService = customerService;
            _localizationService = localizationService;
            _workContext = workContext;
            _problemDetailsFactory = problemDetailsFactory;
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

            if (!await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
            {
                var problemDetails = _problemDetailsFactory.CreateProblemDetails(context.HttpContext, StatusCodes.Status403Forbidden,
                    detail: await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.RegisteredCustomerRequired"));
                context.Result = new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };

                return;
            }

            await next();
        }

        #endregion
    }

    #endregion
}

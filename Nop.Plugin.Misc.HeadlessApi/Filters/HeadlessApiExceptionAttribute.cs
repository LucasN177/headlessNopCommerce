using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.HeadlessApi.Filters;

/// <summary>
/// Represents a filter attribute that logs unhandled exceptions of the API endpoints and returns them as problem details
/// </summary>
public sealed class HeadlessApiExceptionAttribute : TypeFilterAttribute
{
    #region Ctor

    public HeadlessApiExceptionAttribute() : base(typeof(HeadlessApiExceptionFilter))
    {
    }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that handles unhandled exceptions of the API endpoints
    /// </summary>
    private class HeadlessApiExceptionFilter : IAsyncExceptionFilter
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly ProblemDetailsFactory _problemDetailsFactory;

        #endregion

        #region Ctor

        public HeadlessApiExceptionFilter(ILogger logger,
            ProblemDetailsFactory problemDetailsFactory)
        {
            _logger = logger;
            _problemDetailsFactory = problemDetailsFactory;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called after an action has thrown an exception
        /// </summary>
        /// <param name="context">Exception context</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task OnExceptionAsync(ExceptionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.ExceptionHandled)
                return;

            await _logger.ErrorAsync($"{HeadlessApiDefaults.SystemName}: {context.Exception.Message}", context.Exception);

            //don't expose exception details to the client, they are available in the log
            var problemDetails = _problemDetailsFactory.CreateProblemDetails(context.HttpContext, StatusCodes.Status500InternalServerError);
            context.Result = new ObjectResult(problemDetails) { StatusCode = problemDetails.Status };
            context.ExceptionHandled = true;
        }

        #endregion
    }

    #endregion
}

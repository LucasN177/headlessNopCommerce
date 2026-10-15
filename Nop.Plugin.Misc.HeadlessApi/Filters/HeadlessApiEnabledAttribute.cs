using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessApi.Filters;

/// <summary>
/// Represents a filter attribute that returns 404 (Not Found) for API requests while the API is disabled
/// </summary>
public sealed class HeadlessApiEnabledAttribute : TypeFilterAttribute
{
    #region Ctor

    public HeadlessApiEnabledAttribute() : base(typeof(HeadlessApiEnabledFilter))
    {
        //run before the authorization, so a disabled API always responds with 404 (Not Found)
        Order = int.MinValue;
    }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that checks whether the API is enabled
    /// </summary>
    private class HeadlessApiEnabledFilter : IAuthorizationFilter
    {
        #region Fields

        private readonly HeadlessApiSettings _headlessApiSettings;

        #endregion

        #region Ctor

        public HeadlessApiEnabledFilter(HeadlessApiSettings headlessApiSettings)
        {
            _headlessApiSettings = headlessApiSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called early in the filter pipeline to confirm request is authorized
        /// </summary>
        /// <param name="context">Authorization filter context</param>
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (!_headlessApiSettings.Enabled)
                context.Result = new NotFoundResult();
        }

        #endregion
    }

    #endregion
}

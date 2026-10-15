using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the base controller for all headless API endpoints
/// </summary>
/// <remarks>
/// Derived controllers define their route with [HeadlessApiRoute("catalog")], the endpoints are then
/// available under /api/headless/v1/catalog/...
/// All endpoints require a token (guest or registered customer) unless they are marked with [AllowAnonymous].
/// Request models of nopCommerce (e.g. RegisterModel) are validated by their FluentValidation validators,
/// so actions receiving such models have to check ModelState.IsValid.
/// </remarks>
[ApiController]
[Produces("application/json")]
[EnableCors(HeadlessApiDefaults.CorsPolicyName)]
[Authorize(AuthenticationSchemes = HeadlessApiDefaults.AuthenticationScheme)]
[HeadlessApiEnabled]
[HeadlessApiStoreAccess]
[HeadlessApiException]
[CamelCaseJsonResult]
[AutoValidation]
public abstract class BaseHeadlessApiController : ControllerBase
{
    #region Utilities

    /// <summary>
    /// Creates a validation problem result (400) from warnings or errors returned by nopCommerce services
    /// </summary>
    /// <param name="warnings">Warnings</param>
    /// <returns>Validation problem result</returns>
    protected virtual IActionResult WarningsProblem(IEnumerable<string> warnings)
    {
        foreach (var warning in warnings)
            ModelState.AddModelError(string.Empty, warning);

        return ValidationProblem(ModelState);
    }

    /// <summary>
    /// Creates a not found problem result (404)
    /// </summary>
    /// <param name="detail">Detail</param>
    /// <returns>Problem result</returns>
    protected virtual IActionResult NotFoundProblem(string detail = null)
    {
        return Problem(statusCode: StatusCodes.Status404NotFound, detail: detail);
    }

    /// <summary>
    /// Creates a forbidden problem result (403)
    /// </summary>
    /// <param name="detail">Detail</param>
    /// <returns>Problem result</returns>
    protected virtual IActionResult ForbiddenProblem(string detail = null)
    {
        return Problem(statusCode: StatusCodes.Status403Forbidden, detail: detail);
    }

    #endregion
}

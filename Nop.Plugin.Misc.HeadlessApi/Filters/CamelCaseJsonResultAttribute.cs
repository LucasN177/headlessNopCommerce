using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Nop.Plugin.Misc.HeadlessApi.Filters;

/// <summary>
/// Represents a filter attribute that serializes the API results as JSON with camelCase property names
/// </summary>
/// <remarks>
/// nopCommerce configures MVC globally to keep PascalCase names (DefaultContractResolver);
/// this filter changes the serialization for the API endpoints only, so the storefront and admin area are not affected
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class CamelCaseJsonResultAttribute : Attribute, IAlwaysRunResultFilter
{
    #region Fields

    private static readonly JsonSerializerSettings _serializerSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    #endregion

    #region Methods

    /// <summary>
    /// Called before the action result executes
    /// </summary>
    /// <param name="context">Result executing context</param>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult objectResult)
            return;

        context.Result = new JsonResult(objectResult.Value, _serializerSettings)
        {
            StatusCode = objectResult.StatusCode,
            ContentType = objectResult.Value is ProblemDetails ? "application/problem+json" : "application/json"
        };
    }

    /// <summary>
    /// Called after the action result executes
    /// </summary>
    /// <param name="context">Result executed context</param>
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    #endregion
}

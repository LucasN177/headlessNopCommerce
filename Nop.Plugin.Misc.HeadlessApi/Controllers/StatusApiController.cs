using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Plugin.Misc.HeadlessApi.Models;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the status endpoint, which allows a frontend to check the API availability
/// </summary>
[HeadlessApiRoute]
public class StatusApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly IStoreContext _storeContext;

    #endregion

    #region Ctor

    public StatusApiController(IStoreContext storeContext)
    {
        _storeContext = storeContext;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the API status
    /// </summary>
    /// <remarks>
    /// The endpoint doesn't use the work context, so no guest customer is created for anonymous calls
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation; the task result contains the API status</returns>
    [AllowAnonymous]
    [HttpGet("status")]
    [ProducesResponseType(typeof(StatusModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Get()
    {
        var store = await _storeContext.GetCurrentStoreAsync();

        return Ok(new StatusModel
        {
            ApiVersion = HeadlessApiDefaults.ApiVersion,
            NopVersion = NopVersion.FULL_VERSION,
            StoreId = store.Id,
            StoreName = store.Name
        });
    }

    #endregion
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.HeadlessApi.Models.Downloads;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the download endpoints (product samples, purchased downloadable products, license files, order note files, uploaded files)
/// </summary>
/// <remarks>
/// The endpoints return the file itself; if the download is stored as an external URL, a JSON object with the "downloadUrl" is returned instead
/// (status 200, content type application/json), so the frontend has to check the content type of the response.
/// </remarks>
[HeadlessApiRoute("downloads")]
public class DownloadsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CustomerSettings _customerSettings;
    protected readonly IDownloadService _downloadService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IOrderService _orderService;
    protected readonly IProductService _productService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public DownloadsApiController(CustomerSettings customerSettings,
        IDownloadService downloadService,
        ILocalizationService localizationService,
        IOrderService orderService,
        IProductService productService,
        IWorkContext workContext)
    {
        _customerSettings = customerSettings;
        _downloadService = downloadService;
        _localizationService = localizationService;
        _orderService = orderService;
        _productService = productService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Create the result of a download: the file or the external URL
    /// </summary>
    /// <param name="download">Download</param>
    /// <param name="defaultFileName">File name used if the download has no file name</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the result</returns>
    protected virtual async Task<IActionResult> DownloadResultAsync(Download download, string defaultFileName)
    {
        if (download.UseDownloadUrl)
            return Ok(new DownloadUrlModel { DownloadUrl = download.DownloadUrl });

        //binary download
        if (download.DownloadBinary == null)
            return await DownloadNotAvailableProblemAsync();

        var fileName = !string.IsNullOrWhiteSpace(download.Filename) ? download.Filename : defaultFileName;
        var contentType = !string.IsNullOrWhiteSpace(download.ContentType) ? download.ContentType : MimeTypes.ApplicationOctetStream;

        return File(download.DownloadBinary, contentType, fileName + download.Extension);
    }

    /// <summary>
    /// Create a problem result (404) for a download that is not available any more
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result</returns>
    protected virtual async Task<IActionResult> DownloadNotAvailableProblemAsync()
    {
        return NotFoundProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Downloads.NotAvailable"));
    }

    /// <summary>
    /// Create a problem result (403) for a download that is not allowed
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result</returns>
    protected virtual async Task<IActionResult> DownloadNotAllowedProblemAsync()
    {
        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Downloads.NotAllowed"));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Downloads the sample file of a product
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file or the download URL</returns>
    [HttpGet("sample/{productId:int}")]
    [ProducesResponseType(typeof(DownloadUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Sample(int productId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null || !product.HasSampleDownload)
            return NotFoundProblem();

        var download = await _downloadService.GetDownloadByIdAsync(product.SampleDownloadId);
        if (download == null)
            return await DownloadNotAvailableProblemAsync();

        return await DownloadResultAsync(download, product.Id.ToString());
    }

    /// <summary>
    /// Downloads a purchased downloadable product
    /// </summary>
    /// <param name="orderItemGuid">Order item GUID</param>
    /// <param name="agree">Whether the customer has accepted the user agreement of the product (see GET account/user-agreement/{orderItemGuid})</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file or the download URL</returns>
    /// <remarks>
    /// Returns 409 (Conflict) if the product has a user agreement and "agree" is not set
    /// </remarks>
    [HttpGet("order-item/{orderItemGuid:guid}")]
    [ProducesResponseType(typeof(DownloadUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public virtual async Task<IActionResult> OrderItemDownload(Guid orderItemGuid, [FromQuery] bool agree = false)
    {
        var orderItem = await _orderService.GetOrderItemByGuidAsync(orderItemGuid);
        if (orderItem == null)
            return NotFoundProblem();

        var order = await _orderService.GetOrderByIdAsync(orderItem.OrderId);

        if (!await _orderService.IsDownloadAllowedAsync(orderItem))
            return await DownloadNotAllowedProblemAsync();

        if (_customerSettings.DownloadableProductsValidateUser)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null || order?.CustomerId != customer.Id)
                return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Downloads.NotYourOrder"));
        }

        var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
        var download = await _downloadService.GetDownloadByIdAsync(product?.DownloadId ?? 0);
        if (download == null)
            return await DownloadNotAvailableProblemAsync();

        if (product.HasUserAgreement && !agree)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                detail: await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Downloads.UserAgreementRequired"));
        }

        if (!product.UnlimitedDownloads && orderItem.DownloadCount >= product.MaxNumberOfDownloads)
        {
            return ForbiddenProblem(string.Format(await _localizationService.GetResourceAsync("DownloadableProducts.ReachedMaximumNumber"),
                product.MaxNumberOfDownloads));
        }

        if (!download.UseDownloadUrl && download.DownloadBinary == null)
            return await DownloadNotAvailableProblemAsync();

        //increase download
        orderItem.DownloadCount++;
        await _orderService.UpdateOrderItemAsync(orderItem);

        return await DownloadResultAsync(download, product.Id.ToString());
    }

    /// <summary>
    /// Downloads the license file of a purchased downloadable product
    /// </summary>
    /// <param name="orderItemGuid">Order item GUID</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file or the download URL</returns>
    [HttpGet("license/{orderItemGuid:guid}")]
    [ProducesResponseType(typeof(DownloadUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> License(Guid orderItemGuid)
    {
        var orderItem = await _orderService.GetOrderItemByGuidAsync(orderItemGuid);
        if (orderItem == null)
            return NotFoundProblem();

        var order = await _orderService.GetOrderByIdAsync(orderItem.OrderId);

        if (!await _orderService.IsLicenseDownloadAllowedAsync(orderItem))
            return await DownloadNotAllowedProblemAsync();

        if (_customerSettings.DownloadableProductsValidateUser)
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            if (customer == null || order?.CustomerId != customer.Id)
                return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Downloads.NotYourOrder"));
        }

        var download = await _downloadService.GetDownloadByIdAsync(orderItem.LicenseDownloadId ?? 0);
        if (download == null)
            return await DownloadNotAvailableProblemAsync();

        return await DownloadResultAsync(download, orderItem.ProductId.ToString());
    }

    /// <summary>
    /// Downloads the file attached to an order note of an order of the current customer
    /// </summary>
    /// <param name="orderNoteId">Order note identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file or the download URL</returns>
    [HttpGet("order-note/{orderNoteId:int}")]
    [ProducesResponseType(typeof(DownloadUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> OrderNoteFile(int orderNoteId)
    {
        var orderNote = await _orderService.GetOrderNoteByIdAsync(orderNoteId);
        if (orderNote == null || !orderNote.DisplayToCustomer)
            return NotFoundProblem();

        var order = await _orderService.GetOrderByIdAsync(orderNote.OrderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer == null || order == null || order.CustomerId != customer.Id)
            return NotFoundProblem();

        var download = await _downloadService.GetDownloadByIdAsync(orderNote.DownloadId);
        if (download == null)
            return await DownloadNotAvailableProblemAsync();

        return await DownloadResultAsync(download, orderNote.Id.ToString());
    }

    /// <summary>
    /// Downloads a file uploaded by a customer (e.g. for a product or checkout attribute of the type "File upload")
    /// </summary>
    /// <param name="downloadGuid">Download GUID</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file or the download URL</returns>
    [HttpGet("file/{downloadGuid:guid}")]
    [ProducesResponseType(typeof(DownloadUrlModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UploadedFile(Guid downloadGuid)
    {
        var download = await _downloadService.GetDownloadByGuidAsync(downloadGuid);
        if (download == null)
            return await DownloadNotAvailableProblemAsync();

        return await DownloadResultAsync(download, downloadGuid.ToString());
    }

    #endregion
}

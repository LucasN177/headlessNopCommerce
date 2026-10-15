using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessApi.Models.ReturnRequests;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Factories;
using Nop.Web.Models.Order;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the return request endpoints that are not bound to an order of the current customer (file upload, guest withdrawal form)
/// </summary>
/// <remarks>
/// The return request form and submission are provided by <see cref="CustomerOrdersApiController"/>
/// (GET/POST account/orders/{orderId}/return-request). CAPTCHA is not supported by the API.
/// </remarks>
[HeadlessApiRoute("return-requests")]
public class ReturnRequestsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly IAddressService _addressService;
    protected readonly ICustomerService _customerService;
    protected readonly IDownloadService _downloadService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INopFileProvider _fileProvider;
    protected readonly IOrderProcessingService _orderProcessingService;
    protected readonly IOrderService _orderService;
    protected readonly IReturnRequestModelFactory _returnRequestModelFactory;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly ReturnRequestSettings _returnRequestSettings;

    #endregion

    #region Ctor

    public ReturnRequestsApiController(IAddressService addressService,
        ICustomerService customerService,
        IDownloadService downloadService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        INopFileProvider fileProvider,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IReturnRequestModelFactory returnRequestModelFactory,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        ReturnRequestSettings returnRequestSettings)
    {
        _addressService = addressService;
        _customerService = customerService;
        _downloadService = downloadService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _fileProvider = fileProvider;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _returnRequestModelFactory = returnRequestModelFactory;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _returnRequestSettings = returnRequestSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether the current customer can use the guest withdrawal form
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the form can be used</returns>
    protected virtual async Task<IActionResult> CheckWithdrawalFormAvailableAsync()
    {
        //registered customers submit return requests from their order history
        if (await _customerService.IsRegisteredAsync(await _workContext.GetCurrentCustomerAsync()))
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ReturnRequests.UseOrderHistory"));

        if (!_returnRequestSettings.GuestReturnRequestsAllowed)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        return null;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Uploads a file for a return request (multipart/form-data, field "file")
    /// </summary>
    /// <param name="file">File</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the GUID of the uploaded file</returns>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ReturnRequestFileModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (!_returnRequestSettings.ReturnRequestsEnabled || !_returnRequestSettings.ReturnRequestsAllowFiles)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        if (file == null)
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Upload.NoFile")]);

        var fileBinary = await _downloadService.GetDownloadBitsAsync(file);

        //remove path (passed in IE)
        var fileName = _fileProvider.GetFileName(file.FileName);

        var fileExtension = _fileProvider.GetFileExtension(fileName);
        if (!string.IsNullOrEmpty(fileExtension))
            fileExtension = fileExtension.ToLowerInvariant();

        var validationFileMaximumSize = _returnRequestSettings.ReturnRequestsFileMaximumSize;
        if (validationFileMaximumSize > 0)
        {
            //compare in bytes
            var maxFileSizeBytes = validationFileMaximumSize * 1024;
            if (fileBinary.Length > maxFileSizeBytes)
                return WarningsProblem([string.Format(await _localizationService.GetResourceAsync("ShoppingCart.MaximumUploadedFileSize"), validationFileMaximumSize)]);
        }

        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadUrl = string.Empty,
            DownloadBinary = fileBinary,
            ContentType = file.ContentType,
            //we store filename without extension for downloads
            Filename = _fileProvider.GetFileNameWithoutExtension(fileName),
            Extension = fileExtension,
            IsNew = true
        };
        await _downloadService.InsertDownloadAsync(download);

        return Ok(new ReturnRequestFileModel
        {
            DownloadGuid = download.DownloadGuid,
            Message = await _localizationService.GetResourceAsync("ShoppingCart.FileUploaded")
        });
    }

    /// <summary>
    /// Gets the withdrawal form for guests (find an order by order number and billing email)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the withdrawal form</returns>
    [HttpGet("find")]
    [ProducesResponseType(typeof(WithdrawalFormModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> WithdrawalForm()
    {
        if (await CheckWithdrawalFormAvailableAsync() is IActionResult problem)
            return problem;

        return Ok(await _returnRequestModelFactory.PrepareWithdrawalFormModelAsync(null));
    }

    /// <summary>
    /// Sends the withdrawal link (return request of a guest order) to the billing email of the order;
    /// always returns the same confirmation text, so it can't be used to find out whether an order exists
    /// </summary>
    /// <param name="model">Order number and billing email</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the withdrawal form with the result message</returns>
    /// <remarks>
    /// The email link has to point to a frontend page that calls GET/POST account/orders/{orderId}/return-request with the token
    /// </remarks>
    [HttpPost("find")]
    [ProducesResponseType(typeof(WithdrawalFormModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> FindOrder(WithdrawalFormModel model)
    {
        if (await CheckWithdrawalFormAvailableAsync() is IActionResult problem)
            return problem;

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var order = int.TryParse(model.OrderNumber, out var orderNumber)
            ? await _orderService.GetOrderByIdAsync(orderNumber)
            : await _orderService.GetOrderByCustomOrderNumberAsync(model.OrderNumber);

        var store = await _storeContext.GetCurrentStoreAsync();
        model = await _returnRequestModelFactory.PrepareWithdrawalFormModelAsync(model);
        model = model with { Result = await _localizationService.GetResourceAsync("ReturnRequests.WithdrawalForm.ConfirmationText") };

        if (order is null || order.StoreId != store.Id || !await _orderProcessingService.IsReturnRequestAllowedAsync(order))
            return Ok(model);

        var address = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
        if (!string.Equals(address?.Email, model.EmailAddress, StringComparison.OrdinalIgnoreCase))
            return Ok(model);

        //save token and current date
        await _genericAttributeService.SaveAttributeAsync(order, NopOrderDefaults.WithdrawalTokenAttribute, Guid.NewGuid().ToString());
        await _genericAttributeService.SaveAttributeAsync(order, NopOrderDefaults.WithdrawalTokenDateGeneratedAttribute, DateTime.UtcNow);

        //send email
        await _workflowMessageService.SendWithdrawalRequestConfirmationNotificationAsync(order);

        return Ok(model);
    }

    #endregion
}

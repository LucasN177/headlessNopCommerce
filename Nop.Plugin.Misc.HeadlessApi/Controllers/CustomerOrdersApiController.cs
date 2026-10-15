using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Orders;
using Nop.Plugin.Misc.HeadlessApi.Models.ReturnRequests;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Shipping;
using Nop.Web.Factories;
using Nop.Web.Models.Order;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the order related endpoints of "My account" (PDF invoice, re-order, cancellation, shipments, recurring payments, reward points, return requests)
/// </summary>
/// <remarks>
/// Order list and details are provided by <see cref="AccountApiController"/>.
/// Return requests can also be submitted by guests using the withdrawal token of the email link (see POST return-requests/find).
/// </remarks>
[HeadlessApiRoute("account")]
public class CustomerOrdersApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly ICustomerService _customerService;
    protected readonly ICustomNumberFormatter _customNumberFormatter;
    protected readonly IDownloadService _downloadService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IOrderModelFactory _orderModelFactory;
    protected readonly IOrderProcessingService _orderProcessingService;
    protected readonly IOrderService _orderService;
    protected readonly IPdfService _pdfService;
    protected readonly IReturnRequestModelFactory _returnRequestModelFactory;
    protected readonly IReturnRequestService _returnRequestService;
    protected readonly IShipmentService _shipmentService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly LocalizationSettings _localizationSettings;
    protected readonly OrderSettings _orderSettings;
    protected readonly ReturnRequestSettings _returnRequestSettings;
    protected readonly RewardPointsSettings _rewardPointsSettings;

    #endregion

    #region Ctor

    public CustomerOrdersApiController(ICustomerService customerService,
        ICustomNumberFormatter customNumberFormatter,
        IDownloadService downloadService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        IOrderModelFactory orderModelFactory,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IPdfService pdfService,
        IReturnRequestModelFactory returnRequestModelFactory,
        IReturnRequestService returnRequestService,
        IShipmentService shipmentService,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings,
        OrderSettings orderSettings,
        ReturnRequestSettings returnRequestSettings,
        RewardPointsSettings rewardPointsSettings)
    {
        _customerService = customerService;
        _customNumberFormatter = customNumberFormatter;
        _downloadService = downloadService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _orderModelFactory = orderModelFactory;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _pdfService = pdfService;
        _returnRequestModelFactory = returnRequestModelFactory;
        _returnRequestService = returnRequestService;
        _shipmentService = shipmentService;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
        _orderSettings = orderSettings;
        _returnRequestSettings = returnRequestSettings;
        _rewardPointsSettings = rewardPointsSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets an order of the current customer
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the order or null if not found or not owned by the current customer</returns>
    protected virtual async Task<Order> GetCustomerOrderAsync(int orderId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (order == null || order.Deleted || order.CustomerId != customer.Id)
            return null;

        return order;
    }

    /// <summary>
    /// Gets a recurring payment the current customer can manage
    /// </summary>
    /// <param name="recurringPaymentId">Recurring payment identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the recurring payment or null if not found or not owned by the current customer</returns>
    protected virtual async Task<RecurringPayment> GetCustomerRecurringPaymentAsync(int recurringPaymentId)
    {
        var recurringPayment = await _orderService.GetRecurringPaymentByIdAsync(recurringPaymentId);
        if (recurringPayment == null || recurringPayment.Deleted)
            return null;

        var initialOrder = await _orderService.GetOrderByIdAsync(recurringPayment.InitialOrderId);
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (initialOrder == null || initialOrder.CustomerId != customer.Id)
            return null;

        return recurringPayment;
    }

    /// <summary>
    /// Validate the withdrawal token of a guest return request (email link)
    /// </summary>
    /// <param name="order">Order</param>
    /// <param name="token">Withdrawal token</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains true if the token is valid; otherwise false</returns>
    protected virtual async Task<bool> ValidateWithdrawalTokenAsync(Order order, string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var orderCustomer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

        //order has been created by registered customer
        if (orderCustomer != null && !await _customerService.IsGuestAsync(orderCustomer))
            return false;

        var existedToken = await _genericAttributeService.GetAttributeAsync<string>(order, NopOrderDefaults.WithdrawalTokenAttribute);
        var existedTokenGeneratedDate = await _genericAttributeService.GetAttributeAsync<DateTime>(order, NopOrderDefaults.WithdrawalTokenDateGeneratedAttribute);

        return string.Equals(token, existedToken, StringComparison.OrdinalIgnoreCase)
            && existedTokenGeneratedDate > DateTime.UtcNow.AddDays(-_returnRequestSettings.WithdrawalLinkDaysValid);
    }

    /// <summary>
    /// Gets an order for which the current customer (or a guest with the withdrawal token) can submit a return request
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <param name="withdrawalToken">Withdrawal token of the email link; empty for the own orders of the current customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the order and the problem result if the order can't be used</returns>
    protected virtual async Task<(Order order, IActionResult problem)> GetReturnRequestOrderAsync(int orderId, string withdrawalToken)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null || order.Deleted)
            return (null, NotFoundProblem());

        if (!string.IsNullOrEmpty(withdrawalToken))
        {
            if (!await ValidateWithdrawalTokenAsync(order, withdrawalToken))
                return (null, ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Account.InvalidLink")));
        }
        else if ((await _workContext.GetCurrentCustomerAsync()).Id != order.CustomerId)
            return (null, NotFoundProblem());

        if (!await _orderProcessingService.IsReturnRequestAllowedAsync(order))
            return (null, ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ReturnRequests.NotAllowed")));

        return (order, null);
    }

    #endregion

    #region Orders

    /// <summary>
    /// Gets the PDF invoice of an order of the current customer
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the PDF file</returns>
    [HttpGet("orders/{orderId:int}/pdf")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> PdfInvoice(int orderId)
    {
        var order = await GetCustomerOrderAsync(orderId);
        if (order == null)
            return NotFoundProblem();

        byte[] bytes;
        await using (var stream = new MemoryStream())
        {
            await _pdfService.PrintOrderToPdfAsync(stream, order, await _workContext.GetWorkingLanguageAsync());
            bytes = stream.ToArray();
        }

        return File(bytes, MimeTypes.ApplicationPdf, string.Format(await _localizationService.GetResourceAsync("PDFInvoice.FileName"), order.CustomOrderNumber) + ".pdf");
    }

    /// <summary>
    /// Adds the products of an order of the current customer to the shopping cart
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the warnings of products that couldn't be added</returns>
    [HttpPost("orders/{orderId:int}/reorder")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(ReorderResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ReOrder(int orderId)
    {
        var order = await GetCustomerOrderAsync(orderId);
        if (order == null)
            return NotFoundProblem();

        var warnings = await _orderProcessingService.ReOrderAsync(order);
        var result = new ReorderResultModel { Warnings = warnings };
        if (warnings.Any())
            result.Message = await _localizationService.GetResourceAsync("ShoppingCart.ReorderWarning");

        return Ok(result);
    }

    /// <summary>
    /// Cancels an order of the current customer (only if "Allow customers to cancel orders" is enabled)
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated order details</returns>
    [HttpPost("orders/{orderId:int}/cancel")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(OrderDetailsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> CancelOrder(int orderId)
    {
        if (!_orderSettings.AllowCustomersCancelOrders)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        var order = await GetCustomerOrderAsync(orderId);
        if (order == null)
            return NotFoundProblem();

        if (!_orderProcessingService.CanCancelOrder(order))
            return WarningsProblem([await _localizationService.GetResourceAsync("Order.Cancel.Failed")]);

        try
        {
            await _orderProcessingService.CancelOrderAsync(order, false);
        }
        catch
        {
            return WarningsProblem([await _localizationService.GetResourceAsync("Order.Cancel.Failed")]);
        }

        await _orderService.UpdateOrderAsync(order);

        return Ok(await _orderModelFactory.PrepareOrderDetailsModelAsync(order));
    }

    /// <summary>
    /// Gets the details of a shipment of an order of the current customer
    /// </summary>
    /// <param name="shipmentId">Shipment identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the shipment details</returns>
    [HttpGet("shipments/{shipmentId:int}")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(ShipmentDetailsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ShipmentDetails(int shipmentId)
    {
        var shipment = await _shipmentService.GetShipmentByIdAsync(shipmentId);
        if (shipment == null || await GetCustomerOrderAsync(shipment.OrderId) == null)
            return NotFoundProblem();

        return Ok(await _orderModelFactory.PrepareShipmentDetailsModelAsync(shipment));
    }

    #endregion

    #region Recurring payments

    /// <summary>
    /// Gets the recurring payments of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the recurring payments</returns>
    [HttpGet("recurring-payments")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerRecurringPaymentListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> RecurringPayments()
    {
        return Ok(await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync());
    }

    /// <summary>
    /// Cancels a recurring payment of the current customer
    /// </summary>
    /// <param name="recurringPaymentId">Recurring payment identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated recurring payments (see "recurringPaymentErrors")</returns>
    [HttpPost("recurring-payments/{recurringPaymentId:int}/cancel")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerRecurringPaymentListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> CancelRecurringPayment(int recurringPaymentId)
    {
        var recurringPayment = await GetCustomerRecurringPaymentAsync(recurringPaymentId);
        if (recurringPayment == null)
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _orderProcessingService.CanCancelRecurringPaymentAsync(customer, recurringPayment))
            return ForbiddenProblem();

        var errors = await _orderProcessingService.CancelRecurringPaymentAsync(recurringPayment);

        var model = await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync();
        model.RecurringPaymentErrors = errors.ToList();

        return Ok(model);
    }

    /// <summary>
    /// Retries the last failed payment of a recurring payment of the current customer
    /// </summary>
    /// <param name="recurringPaymentId">Recurring payment identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated recurring payments (see "recurringPaymentErrors")</returns>
    [HttpPost("recurring-payments/{recurringPaymentId:int}/retry")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerRecurringPaymentListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> RetryLastRecurringPayment(int recurringPaymentId)
    {
        var recurringPayment = await GetCustomerRecurringPaymentAsync(recurringPaymentId);
        if (recurringPayment == null)
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _orderProcessingService.CanRetryLastRecurringPaymentAsync(customer, recurringPayment))
            return ForbiddenProblem();

        var errors = await _orderProcessingService.ProcessNextRecurringPaymentAsync(recurringPayment);

        var model = await _orderModelFactory.PrepareCustomerRecurringPaymentListModelAsync();
        model.RecurringPaymentErrors = errors.ToList();

        return Ok(model);
    }

    #endregion

    #region Reward points

    /// <summary>
    /// Gets the reward points history of the current customer
    /// </summary>
    /// <param name="page">Page number</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the reward points</returns>
    [HttpGet("reward-points")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerRewardPointsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> RewardPoints([FromQuery] int? page = null)
    {
        if (!_rewardPointsSettings.Enabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        return Ok(await _orderModelFactory.PrepareCustomerRewardPointsAsync(page));
    }

    #endregion

    #region Return requests

    /// <summary>
    /// Gets the return requests of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the return requests</returns>
    [HttpGet("return-requests")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerReturnRequestsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ReturnRequests()
    {
        return Ok(await _returnRequestModelFactory.PrepareCustomerReturnRequestsModelAsync());
    }

    /// <summary>
    /// Gets the return request form of an order (returnable items, reasons, actions)
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <param name="token">Withdrawal token of the email link (guest return requests); empty for the own orders of the current customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the return request form</returns>
    [HttpGet("orders/{orderId:int}/return-request")]
    [ProducesResponseType(typeof(SubmitReturnRequestModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ReturnRequestForm(int orderId, [FromQuery] string token = null)
    {
        var (order, problem) = await GetReturnRequestOrderAsync(orderId, token);
        if (problem != null)
            return problem;

        return Ok(await _returnRequestModelFactory.PrepareSubmitReturnRequestModelAsync(new SubmitReturnRequestModel { WithdrawalToken = token }, order));
    }

    /// <summary>
    /// Submits a return request (withdrawal) for items of an order
    /// </summary>
    /// <param name="orderId">Order identifier</param>
    /// <param name="request">Return request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the return request form with the result message</returns>
    [HttpPost("orders/{orderId:int}/return-request")]
    [ProducesResponseType(typeof(SubmitReturnRequestModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SubmitReturnRequest(int orderId, SubmitReturnRequestRequest request)
    {
        var (order, problem) = await GetReturnRequestOrderAsync(orderId, request.WithdrawalToken);
        if (problem != null)
            return problem;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        var downloadId = 0;
        if (_returnRequestSettings.ReturnRequestsAllowFiles)
        {
            var download = await _downloadService.GetDownloadByGuidAsync(request.UploadedFileGuid);
            if (download != null)
                downloadId = download.Id;
        }

        var returnRequestReason = await _returnRequestService.GetReturnRequestReasonByIdAsync(request.ReturnRequestReasonId);
        var returnRequestAction = await _returnRequestService.GetReturnRequestActionByIdAsync(request.ReturnRequestActionId);
        var quantities = request.Quantities ?? new Dictionary<int, int>();

        //returnable products
        var count = 0;
        var orderItems = await _orderService.GetOrderItemsAsync(order.Id, isNotReturnable: false);
        foreach (var orderItem in orderItems)
        {
            if (!quantities.TryGetValue(orderItem.Id, out var quantity) || quantity <= 0)
                continue;

            var returnRequest = new ReturnRequest
            {
                CustomNumber = string.Empty,
                StoreId = store.Id,
                OrderItemId = orderItem.Id,
                Quantity = quantity,
                CustomerId = customer.Id,
                CustomerComments = request.Comments,
                UploadedFileId = downloadId,
                StaffNotes = string.Empty,
                ReasonForReturn = _returnRequestSettings.ReturnReasonsEnabled && returnRequestReason != null
                    ? await _localizationService.GetLocalizedAsync(returnRequestReason, reason => reason.Name)
                    : "not available",
                RequestedAction = _returnRequestSettings.ReturnActionsEnabled && returnRequestAction != null
                    ? await _localizationService.GetLocalizedAsync(returnRequestAction, action => action.Name)
                    : "not available",
                ReturnRequestStatus = ReturnRequestStatus.Pending,
                CreatedOnUtc = DateTime.UtcNow,
                UpdatedOnUtc = DateTime.UtcNow
            };

            await _returnRequestService.InsertReturnRequestAsync(returnRequest);

            //set return request custom number
            returnRequest.CustomNumber = _customNumberFormatter.GenerateReturnRequestCustomNumber(returnRequest);
            await _customerService.UpdateCustomerAsync(customer);
            await _returnRequestService.UpdateReturnRequestAsync(returnRequest);

            //notify store owner
            await _workflowMessageService.SendNewReturnRequestStoreOwnerNotificationAsync(returnRequest, orderItem, order, _localizationSettings.DefaultAdminLanguageId);
            //notify customer
            await _workflowMessageService.SendNewReturnRequestCustomerNotificationAsync(returnRequest, orderItem, order);

            count++;
        }

        if (count == 0)
            return WarningsProblem([await _localizationService.GetResourceAsync("ReturnRequests.NoItemsSubmitted")]);

        var model = new SubmitReturnRequestModel
        {
            WithdrawalToken = request.WithdrawalToken,
            ReturnRequestReasonId = request.ReturnRequestReasonId,
            ReturnRequestActionId = request.ReturnRequestActionId,
            Comments = request.Comments,
            UploadedFileGuid = request.UploadedFileGuid
        };
        model = await _returnRequestModelFactory.PrepareSubmitReturnRequestModelAsync(model, order);
        model.Result = _returnRequestSettings.UseEuWithdrawalLocales
            ? await _localizationService.GetResourceAsync("ReturnRequests.Withdrawal.Submitted")
            : await _localizationService.GetResourceAsync("ReturnRequests.Submitted");

        return Ok(model);
    }

    #endregion
}

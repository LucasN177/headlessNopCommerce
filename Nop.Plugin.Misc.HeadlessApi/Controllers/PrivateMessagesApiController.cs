using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Web.Factories;
using Nop.Web.Models.PrivateMessages;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the private message endpoints of the current customer (only if private messages are enabled)
/// </summary>
[HeadlessApiRoute("private-messages")]
[RequireRegisteredCustomer]
public class PrivateMessagesApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ICustomerService _customerService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPrivateMessagesModelFactory _privateMessagesModelFactory;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly PrivateMessageSettings _privateMessageSettings;

    #endregion

    #region Ctor

    public PrivateMessagesApiController(ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        IPrivateMessagesModelFactory privateMessagesModelFactory,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        PrivateMessageSettings privateMessageSettings)
    {
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _privateMessagesModelFactory = privateMessagesModelFactory;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _privateMessageSettings = privateMessageSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether private messages are enabled
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if private messages are enabled</returns>
    protected virtual async Task<IActionResult> CheckPrivateMessagesEnabledAsync()
    {
        if (_privateMessageSettings.AllowPrivateMessages)
            return null;

        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));
    }

    /// <summary>
    /// Gets a private message sent or received by the current customer
    /// </summary>
    /// <param name="privateMessageId">Private message identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the private message or null if not found</returns>
    protected virtual async Task<PrivateMessage> GetCustomerPrivateMessageAsync(int privateMessageId)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var pm = await _customerService.GetPrivateMessageByIdAsync(privateMessageId);
        if (pm == null || (pm.ToCustomerId != customer.Id && pm.FromCustomerId != customer.Id))
            return null;

        return pm;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the received (inbox) or sent private messages of the current customer
    /// </summary>
    /// <param name="tab">"inbox" (default) or "sent"</param>
    /// <param name="page">Page number</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the private messages</returns>
    [HttpGet]
    [ProducesResponseType(typeof(PrivateMessageListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> List([FromQuery] string tab = "inbox", [FromQuery] int page = 1)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var model = string.Equals(tab, "sent", StringComparison.InvariantCultureIgnoreCase)
            ? await _privateMessagesModelFactory.PrepareSentModelAsync(page, "sent")
            : await _privateMessagesModelFactory.PrepareInboxModelAsync(page, "inbox");

        return Ok(model);
    }

    /// <summary>
    /// Gets a private message of the current customer; a received message is marked as read
    /// </summary>
    /// <param name="privateMessageId">Private message identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the private message</returns>
    [HttpGet("{privateMessageId:int}")]
    [ProducesResponseType(typeof(PrivateMessageModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Get(int privateMessageId)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var pm = await GetCustomerPrivateMessageAsync(privateMessageId);
        if (pm == null)
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!pm.IsRead && pm.ToCustomerId == customer.Id)
        {
            pm.IsRead = true;
            await _customerService.UpdatePrivateMessageAsync(pm);
        }

        return Ok(await _privateMessagesModelFactory.PreparePrivateMessageModelAsync(pm));
    }

    /// <summary>
    /// Gets the form to send a private message
    /// </summary>
    /// <param name="toCustomerId">Recipient customer identifier</param>
    /// <param name="replyToMessageId">Identifier of the message to reply to (optional)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form</returns>
    [HttpGet("new")]
    [ProducesResponseType(typeof(SendPrivateMessageModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SendForm([FromQuery] int toCustomerId, [FromQuery] int? replyToMessageId = null)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var customerTo = await _customerService.GetCustomerByIdAsync(toCustomerId);
        if (customerTo == null || await _customerService.IsGuestAsync(customerTo))
            return NotFoundProblem();

        //reply to a previous PM
        var replyToPM = replyToMessageId.HasValue ? await GetCustomerPrivateMessageAsync(replyToMessageId.Value) : null;

        return Ok(await _privateMessagesModelFactory.PrepareSendPrivateMessageModelAsync(customerTo, replyToPM));
    }

    /// <summary>
    /// Sends a private message; on a reply ("replyToMessageId") the recipient is taken from the original message
    /// </summary>
    /// <param name="model">Recipient, subject and message</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the sent message</returns>
    [HttpPost]
    [ProducesResponseType(typeof(PrivateMessageModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Send(SendPrivateMessageModel model)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var customer = await _workContext.GetCurrentCustomerAsync();

        Customer toCustomer;
        var replyToPM = await _customerService.GetPrivateMessageByIdAsync(model.ReplyToMessageId);
        if (replyToPM != null)
        {
            //reply to a previous PM
            if (replyToPM.ToCustomerId != customer.Id && replyToPM.FromCustomerId != customer.Id)
                return NotFoundProblem();

            //reply to already sent PM (by current customer) should not be sent to yourself
            toCustomer = await _customerService.GetCustomerByIdAsync(replyToPM.FromCustomerId == customer.Id
                ? replyToPM.ToCustomerId
                : replyToPM.FromCustomerId);
        }
        else
        {
            //first PM
            toCustomer = await _customerService.GetCustomerByIdAsync(model.ToCustomerId);
        }

        if (toCustomer == null || await _customerService.IsGuestAsync(toCustomer))
            return NotFoundProblem();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var subject = model.Subject;
        if (_privateMessageSettings.PMSubjectMaxLength > 0 && subject.Length > _privateMessageSettings.PMSubjectMaxLength)
            subject = subject[.._privateMessageSettings.PMSubjectMaxLength];

        var text = model.Message;
        if (_privateMessageSettings.PMTextMaxLength > 0 && text.Length > _privateMessageSettings.PMTextMaxLength)
            text = text[.._privateMessageSettings.PMTextMaxLength];

        var store = await _storeContext.GetCurrentStoreAsync();
        var privateMessage = new PrivateMessage
        {
            StoreId = store.Id,
            ToCustomerId = toCustomer.Id,
            FromCustomerId = customer.Id,
            Subject = subject,
            Text = text,
            IsDeletedByAuthor = false,
            IsDeletedByRecipient = false,
            IsRead = false,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _customerService.InsertPrivateMessageAsync(privateMessage);

        //UI notification
        await _genericAttributeService.SaveAttributeAsync(toCustomer, NopCustomerDefaults.NotifiedAboutNewPrivateMessagesAttribute, false, privateMessage.StoreId);

        //email notification
        if (_privateMessageSettings.NotifyAboutPrivateMessages)
            await _workflowMessageService.SendPrivateMessageNotificationAsync(privateMessage, toCustomer.LanguageId ?? 0);

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.SendPM",
            string.Format(await _localizationService.GetResourceAsync("ActivityLog.PublicStore.SendPM"), toCustomer.Email), toCustomer);

        return Ok(await _privateMessagesModelFactory.PreparePrivateMessageModelAsync(privateMessage));
    }

    /// <summary>
    /// Deletes a private message for the current customer (as author and/or recipient)
    /// </summary>
    /// <param name="privateMessageId">Private message identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    [HttpDelete("{privateMessageId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Delete(int privateMessageId)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var pm = await GetCustomerPrivateMessageAsync(privateMessageId);
        if (pm == null)
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (pm.FromCustomerId == customer.Id)
            pm.IsDeletedByAuthor = true;
        if (pm.ToCustomerId == customer.Id)
            pm.IsDeletedByRecipient = true;
        await _customerService.UpdatePrivateMessageAsync(pm);

        return NoContent();
    }

    /// <summary>
    /// Marks a received private message as unread
    /// </summary>
    /// <param name="privateMessageId">Private message identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    [HttpPost("{privateMessageId:int}/mark-unread")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> MarkUnread(int privateMessageId)
    {
        if (await CheckPrivateMessagesEnabledAsync() is IActionResult problem)
            return problem;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var pm = await GetCustomerPrivateMessageAsync(privateMessageId);
        if (pm == null || pm.ToCustomerId != customer.Id)
            return NotFoundProblem();

        pm.IsRead = false;
        await _customerService.UpdatePrivateMessageAsync(pm);

        return NoContent();
    }

    #endregion
}

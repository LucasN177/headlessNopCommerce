using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Gdpr;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.ExportImport;
using Nop.Services.Gdpr;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Web.Factories;
using Nop.Web.Models.Customer;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the endpoints of the "My account" tools (avatar, GDPR tools, gift card balance, downloadable products)
/// </summary>
/// <remarks>
/// CAPTCHA is not supported by the API
/// </remarks>
[HeadlessApiRoute("account")]
public class AccountToolsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CustomerSettings _customerSettings;
    protected readonly GdprSettings _gdprSettings;
    protected readonly ICurrencyService _currencyService;
    protected readonly ICustomerModelFactory _customerModelFactory;
    protected readonly IDownloadService _downloadService;
    protected readonly IExportManager _exportManager;
    protected readonly IGdprService _gdprService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IGiftCardService _giftCardService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IOrderService _orderService;
    protected readonly IPictureService _pictureService;
    protected readonly IPriceFormatter _priceFormatter;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly LocalizationSettings _localizationSettings;
    protected readonly MediaSettings _mediaSettings;

    #endregion

    #region Ctor

    public AccountToolsApiController(CustomerSettings customerSettings,
        GdprSettings gdprSettings,
        ICurrencyService currencyService,
        ICustomerModelFactory customerModelFactory,
        IDownloadService downloadService,
        IExportManager exportManager,
        IGdprService gdprService,
        IGenericAttributeService genericAttributeService,
        IGiftCardService giftCardService,
        ILocalizationService localizationService,
        IOrderService orderService,
        IPictureService pictureService,
        IPriceFormatter priceFormatter,
        IProductService productService,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings,
        MediaSettings mediaSettings)
    {
        _customerSettings = customerSettings;
        _gdprSettings = gdprSettings;
        _currencyService = currencyService;
        _customerModelFactory = customerModelFactory;
        _downloadService = downloadService;
        _exportManager = exportManager;
        _gdprService = gdprService;
        _genericAttributeService = genericAttributeService;
        _giftCardService = giftCardService;
        _localizationService = localizationService;
        _orderService = orderService;
        _pictureService = pictureService;
        _priceFormatter = priceFormatter;
        _productService = productService;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
        _mediaSettings = mediaSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Create a problem result (403) for a feature disabled in the settings
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result</returns>
    protected virtual async Task<IActionResult> FeatureDisabledProblemAsync()
    {
        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));
    }

    #endregion

    #region Avatar

    /// <summary>
    /// Gets the avatar of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the avatar</returns>
    [HttpGet("avatar")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAvatarModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Avatar()
    {
        if (!_customerSettings.AllowCustomersToUploadAvatars)
            return await FeatureDisabledProblemAsync();

        return Ok(await _customerModelFactory.PrepareCustomerAvatarModelAsync(new CustomerAvatarModel()));
    }

    /// <summary>
    /// Uploads the avatar of the current customer (multipart/form-data, field "uploadedFile"; JPEG or GIF)
    /// </summary>
    /// <param name="uploadedFile">Avatar image</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated avatar</returns>
    [HttpPost("avatar")]
    [RequireRegisteredCustomer]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CustomerAvatarModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> UploadAvatar(IFormFile uploadedFile)
    {
        if (!_customerSettings.AllowCustomersToUploadAvatars)
            return await FeatureDisabledProblemAsync();

        var contentType = uploadedFile?.ContentType?.ToLowerInvariant();
        if (contentType != null && !contentType.Equals("image/jpeg") && !contentType.Equals("image/gif"))
            return WarningsProblem([await _localizationService.GetResourceAsync("Account.Avatar.UploadRules")]);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var customerAvatar = await _pictureService.GetPictureByIdAsync(await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute));
        if (uploadedFile != null && !string.IsNullOrEmpty(uploadedFile.FileName))
        {
            var avatarMaxSize = _customerSettings.AvatarMaximumSizeBytes;
            if (uploadedFile.Length > avatarMaxSize)
                return WarningsProblem([string.Format(await _localizationService.GetResourceAsync("Account.Avatar.MaximumUploadedFileSize"), avatarMaxSize)]);

            var customerPictureBinary = await _downloadService.GetDownloadBitsAsync(uploadedFile);
            if (customerAvatar != null)
                customerAvatar = await _pictureService.UpdatePictureAsync(customerAvatar.Id, customerPictureBinary, contentType, null);
            else
                customerAvatar = await _pictureService.InsertPictureAsync(customerPictureBinary, contentType, null);
        }

        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.AvatarPictureIdAttribute, customerAvatar?.Id ?? 0);

        return Ok(new CustomerAvatarModel
        {
            AvatarUrl = await _pictureService.GetPictureUrlAsync(customerAvatar?.Id ?? 0, _mediaSettings.AvatarPictureSize, false)
        });
    }

    /// <summary>
    /// Removes the avatar of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated avatar</returns>
    [HttpDelete("avatar")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerAvatarModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> RemoveAvatar()
    {
        if (!_customerSettings.AllowCustomersToUploadAvatars)
            return await FeatureDisabledProblemAsync();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var customerAvatar = await _pictureService.GetPictureByIdAsync(await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.AvatarPictureIdAttribute));
        if (customerAvatar != null)
            await _pictureService.DeletePictureAsync(customerAvatar);
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.AvatarPictureIdAttribute, 0);

        return Ok(await _customerModelFactory.PrepareCustomerAvatarModelAsync(new CustomerAvatarModel()));
    }

    #endregion

    #region GDPR tools

    /// <summary>
    /// Gets the GDPR tools of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the GDPR tools model</returns>
    [HttpGet("gdpr")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(GdprToolsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> GdprTools()
    {
        if (!_gdprSettings.GdprEnabled)
            return await FeatureDisabledProblemAsync();

        return Ok(await _customerModelFactory.PrepareGdprToolsModelAsync());
    }

    /// <summary>
    /// Exports the personal data of the current customer (XLSX file)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the file</returns>
    [HttpPost("gdpr/export")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> GdprToolsExport()
    {
        if (!_gdprSettings.GdprEnabled)
            return await FeatureDisabledProblemAsync();

        var customer = await _workContext.GetCurrentCustomerAsync();

        //log
        await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.ExportData, await _localizationService.GetResourceAsync("Gdpr.Exported"));

        //export
        var store = await _storeContext.GetCurrentStoreAsync();
        var bytes = await _exportManager.ExportCustomerGdprInfoToXlsxAsync(customer, store.Id);

        return File(bytes, MimeTypes.TextXlsx, "customerdata.xlsx");
    }

    /// <summary>
    /// Requests the deletion of the current customer account (the store owner is notified)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the GDPR tools model with the result message</returns>
    [HttpPost("gdpr/delete")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(GdprToolsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> GdprToolsDelete()
    {
        if (!_gdprSettings.GdprEnabled)
            return await FeatureDisabledProblemAsync();

        var customer = await _workContext.GetCurrentCustomerAsync();

        //log
        await _gdprService.InsertLogAsync(customer, 0, GdprRequestType.DeleteCustomer, await _localizationService.GetResourceAsync("Gdpr.DeleteRequested"));

        await _workflowMessageService.SendDeleteCustomerRequestStoreOwnerNotificationAsync(customer, _localizationSettings.DefaultAdminLanguageId);

        var model = await _customerModelFactory.PrepareGdprToolsModelAsync();
        model.Result = await _localizationService.GetResourceAsync("Gdpr.DeleteRequested.Success");

        return Ok(model);
    }

    #endregion

    #region Gift card balance

    /// <summary>
    /// Checks the balance of a gift card
    /// </summary>
    /// <param name="model">Gift card code</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the model with the formatted balance ("result") or an error message ("message")</returns>
    [HttpPost("gift-card-balance")]
    [ProducesResponseType(typeof(CheckGiftCardBalanceModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> CheckGiftCardBalance(CheckGiftCardBalanceModel model)
    {
        if (!_customerSettings.AllowCustomersToCheckGiftCardBalance)
            return await FeatureDisabledProblemAsync();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var giftCard = (await _giftCardService.GetAllGiftCardsAsync(giftCardCouponCode: model.GiftCardCode)).FirstOrDefault();
        if (giftCard != null && await _giftCardService.IsGiftCardValidAsync(giftCard))
        {
            var remainingAmount = await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(await _giftCardService.GetGiftCardRemainingAmountAsync(giftCard),
                await _workContext.GetWorkingCurrencyAsync());
            model.Result = await _priceFormatter.FormatPriceAsync(remainingAmount, true, false);
        }
        else
            model.Message = await _localizationService.GetResourceAsync("CheckGiftCardBalance.GiftCardCouponCode.Invalid");

        return Ok(model);
    }

    #endregion

    #region Downloadable products

    /// <summary>
    /// Gets the downloadable products purchased by the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the downloadable products</returns>
    /// <remarks>
    /// The files are downloaded by GET downloads/order-item/{orderItemGuid} and GET downloads/license/{orderItemGuid}
    /// </remarks>
    [HttpGet("downloadable-products")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerDownloadableProductsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> DownloadableProducts()
    {
        if (_customerSettings.HideDownloadableProductsTab)
            return await FeatureDisabledProblemAsync();

        return Ok(await _customerModelFactory.PrepareCustomerDownloadableProductsModelAsync());
    }

    /// <summary>
    /// Gets the user agreement of a downloadable product; the agreement has to be accepted before the download (GET downloads/order-item/{orderItemGuid}?agree=true)
    /// </summary>
    /// <param name="orderItemGuid">Order item GUID</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the user agreement</returns>
    [HttpGet("user-agreement/{orderItemGuid:guid}")]
    [ProducesResponseType(typeof(UserAgreementModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UserAgreement(Guid orderItemGuid)
    {
        var orderItem = await _orderService.GetOrderItemByGuidAsync(orderItemGuid);
        if (orderItem == null)
            return NotFoundProblem();

        var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
        if (product == null || !product.HasUserAgreement)
            return NotFoundProblem();

        return Ok(await _customerModelFactory.PrepareUserAgreementModelAsync(orderItem, product));
    }

    #endregion
}

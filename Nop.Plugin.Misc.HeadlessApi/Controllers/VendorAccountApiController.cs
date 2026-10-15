using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Services.Attributes;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Seo;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Vendors;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the vendor account endpoints of the current customer (apply for a vendor account, edit the vendor info)
/// </summary>
/// <remarks>
/// The changing endpoints accept multipart/form-data with the same fields as the storefront form:
/// "Name", "Email", "Description", the custom vendor attributes ("vendor_attribute_{id}") and the picture ("uploadedFile").
/// CAPTCHA is not supported by the API.
/// </remarks>
[HeadlessApiRoute("vendor")]
public class VendorAccountApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly IAttributeParser<VendorAttribute, VendorAttributeValue> _vendorAttributeParser;
    protected readonly ICustomerService _customerService;
    protected readonly IDownloadService _downloadService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly IHtmlFormatter _htmlFormatter;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPictureService _pictureService;
    protected readonly IUrlRecordService _urlRecordService;
    protected readonly IVendorModelFactory _vendorModelFactory;
    protected readonly IVendorService _vendorService;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly LocalizationSettings _localizationSettings;
    protected readonly VendorSettings _vendorSettings;

    #endregion

    #region Ctor

    public VendorAccountApiController(IAttributeParser<VendorAttribute, VendorAttributeValue> vendorAttributeParser,
        ICustomerService customerService,
        IDownloadService downloadService,
        IGenericAttributeService genericAttributeService,
        IHtmlFormatter htmlFormatter,
        ILocalizationService localizationService,
        IPictureService pictureService,
        IUrlRecordService urlRecordService,
        IVendorModelFactory vendorModelFactory,
        IVendorService vendorService,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings,
        VendorSettings vendorSettings)
    {
        _vendorAttributeParser = vendorAttributeParser;
        _customerService = customerService;
        _downloadService = downloadService;
        _genericAttributeService = genericAttributeService;
        _htmlFormatter = htmlFormatter;
        _localizationService = localizationService;
        _pictureService = pictureService;
        _urlRecordService = urlRecordService;
        _vendorModelFactory = vendorModelFactory;
        _vendorService = vendorService;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
        _vendorSettings = vendorSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Update the SEO file name of the vendor picture
    /// </summary>
    /// <param name="vendor">Vendor</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task UpdatePictureSeoNamesAsync(Vendor vendor)
    {
        var picture = await _pictureService.GetPictureByIdAsync(vendor.PictureId);
        if (picture != null)
            await _pictureService.SetSeoFilenameAsync(picture.Id, await _pictureService.GetPictureSeNameAsync(vendor.Name));
    }

    /// <summary>
    /// Parse the custom vendor attributes of the posted form and validate them
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the attributes in XML format</returns>
    protected virtual async Task<string> ParseVendorAttributesAsync()
    {
        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : FormCollection.Empty;
        var vendorAttributesXml = await _vendorAttributeParser.ParseCustomAttributesAsync(form, $"{NopVendorDefaults.VendorAttributePrefix}{{0}}");

        foreach (var warning in await _vendorAttributeParser.GetAttributeWarningsAsync(vendorAttributesXml))
            ModelState.AddModelError(string.Empty, warning);

        return vendorAttributesXml;
    }

    /// <summary>
    /// Insert the uploaded vendor picture
    /// </summary>
    /// <param name="uploadedFile">Uploaded file</param>
    /// <param name="errorResourceKey">Resource key of the error message</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the picture or null if no (valid) file is uploaded</returns>
    protected virtual async Task<Picture> InsertPictureAsync(IFormFile uploadedFile, string errorResourceKey)
    {
        if (uploadedFile == null || string.IsNullOrEmpty(uploadedFile.FileName))
            return null;

        try
        {
            var contentType = uploadedFile.ContentType.ToLowerInvariant();
            if (!contentType.StartsWith("image/") || contentType.StartsWith("image/svg"))
            {
                ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync(errorResourceKey));
                return null;
            }

            var vendorPictureBinary = await _downloadService.GetDownloadBitsAsync(uploadedFile);

            return await _pictureService.InsertPictureAsync(vendorPictureBinary, contentType, null);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync(errorResourceKey));
            return null;
        }
    }

    /// <summary>
    /// Check whether the current customer can edit the vendor info
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the vendor and the problem result if the info can't be edited</returns>
    protected virtual async Task<(Vendor vendor, IActionResult problem)> GetEditableVendorAsync()
    {
        var vendor = await _workContext.GetCurrentVendorAsync();
        if (vendor == null || !_vendorSettings.AllowVendorsToEditInfo)
            return (null, ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled")));

        return (vendor, null);
    }

    #endregion

    #region Apply for vendor account

    /// <summary>
    /// Gets the form to apply for a vendor account
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form</returns>
    [HttpGet("apply")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(ApplyVendorModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ApplyVendor()
    {
        if (!_vendorSettings.AllowCustomersToApplyForVendorAccount)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        return Ok(await _vendorModelFactory.PrepareApplyVendorModelAsync(new ApplyVendorModel(), true, false, null));
    }

    /// <summary>
    /// Applies for a vendor account (multipart/form-data); the vendor is created inactive and the store owner is notified
    /// </summary>
    /// <param name="model">Vendor name, email and description</param>
    /// <param name="uploadedFile">Vendor picture (optional)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form with the result message</returns>
    [HttpPost("apply")]
    [RequireRegisteredCustomer]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApplyVendorModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ApplyVendorSubmit([FromForm] ApplyVendorModel model, IFormFile uploadedFile)
    {
        if (!_vendorSettings.AllowCustomersToApplyForVendorAccount)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsAdminAsync(customer))
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Vendors.ApplyAccount.IsAdmin"));

        //vendor attributes
        var vendorAttributesXml = await ParseVendorAttributesAsync();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var picture = await InsertPictureAsync(uploadedFile, "Vendors.ApplyAccount.Picture.ErrorMessage");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var description = _htmlFormatter.FormatText(model.Description);

        //disabled by default
        var vendor = new Vendor
        {
            Name = model.Name,
            Email = model.Email,
            //some default settings
            PageSize = 6,
            AllowCustomersToSelectPageSize = true,
            PageSizeOptions = _vendorSettings.DefaultVendorPageSizeOptions,
            PictureId = picture?.Id ?? 0,
            Description = WebUtility.HtmlEncode(description)
        };
        await _vendorService.InsertVendorAsync(vendor);

        //search engine name (the same as vendor name)
        var seName = await _urlRecordService.ValidateSeNameAsync(vendor, vendor.Name, vendor.Name, true);
        await _urlRecordService.SaveSlugAsync(vendor, seName, 0);

        //associate to the current customer
        //but a store owner will have to manually add this customer role to "Vendors" role
        //if he wants to grant access to admin area
        customer.VendorId = vendor.Id;
        await _customerService.UpdateCustomerAsync(customer);

        //update picture seo file name
        await UpdatePictureSeoNamesAsync(vendor);

        //save vendor attributes
        await _genericAttributeService.SaveAttributeAsync(vendor, NopVendorDefaults.VendorAttributes, vendorAttributesXml);

        //notify store owner here (email)
        await _workflowMessageService.SendNewVendorAccountApplyStoreOwnerNotificationAsync(customer, vendor, _localizationSettings.DefaultAdminLanguageId);

        model = await _vendorModelFactory.PrepareApplyVendorModelAsync(model, false, true, vendorAttributesXml);
        model.DisableFormInput = true;
        model.Result = await _localizationService.GetResourceAsync("Vendors.ApplyAccount.Submitted");

        return Ok(model);
    }

    #endregion

    #region Vendor info

    /// <summary>
    /// Gets the vendor info of the current customer (only for vendors if "Allow vendors to edit info" is enabled)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the vendor info</returns>
    [HttpGet("info")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(VendorInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Info()
    {
        var (_, problem) = await GetEditableVendorAsync();
        if (problem != null)
            return problem;

        return Ok(await _vendorModelFactory.PrepareVendorInfoModelAsync(new VendorInfoModel(), false));
    }

    /// <summary>
    /// Updates the vendor info of the current customer (multipart/form-data); an uploaded picture replaces the current one
    /// </summary>
    /// <param name="model">Vendor name, email and description</param>
    /// <param name="uploadedFile">Vendor picture (optional)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated vendor info</returns>
    [HttpPut("info")]
    [RequireRegisteredCustomer]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(VendorInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> UpdateInfo([FromForm] VendorInfoModel model, IFormFile uploadedFile)
    {
        var (vendor, problem) = await GetEditableVendorAsync();
        if (problem != null)
            return problem;

        //vendor attributes
        var vendorAttributesXml = await ParseVendorAttributesAsync();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var picture = await InsertPictureAsync(uploadedFile, "Account.VendorInfo.Picture.ErrorMessage");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var prevPicture = await _pictureService.GetPictureByIdAsync(vendor.PictureId);
        var description = _htmlFormatter.FormatText(model.Description);

        vendor.Name = model.Name;
        vendor.Email = model.Email;
        vendor.Description = WebUtility.HtmlEncode(description);

        if (picture != null)
        {
            vendor.PictureId = picture.Id;

            if (prevPicture != null)
                await _pictureService.DeletePictureAsync(prevPicture);
        }

        //update picture seo file name
        await UpdatePictureSeoNamesAsync(vendor);

        await _vendorService.UpdateVendorAsync(vendor);

        //save vendor attributes
        await _genericAttributeService.SaveAttributeAsync(vendor, NopVendorDefaults.VendorAttributes, vendorAttributesXml);

        //notifications
        if (_vendorSettings.NotifyStoreOwnerAboutVendorInformationChange)
            await _workflowMessageService.SendVendorInformationChangeStoreOwnerNotificationAsync(vendor, _localizationSettings.DefaultAdminLanguageId);

        return Ok(await _vendorModelFactory.PrepareVendorInfoModelAsync(new VendorInfoModel(), false));
    }

    /// <summary>
    /// Removes the picture of the vendor of the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated vendor info</returns>
    [HttpDelete("info/picture")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(VendorInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> RemovePicture()
    {
        var (vendor, problem) = await GetEditableVendorAsync();
        if (problem != null)
            return problem;

        var picture = await _pictureService.GetPictureByIdAsync(vendor.PictureId);
        if (picture != null)
            await _pictureService.DeletePictureAsync(picture);

        vendor.PictureId = 0;
        await _vendorService.UpdateVendorAsync(vendor);

        //notifications
        if (_vendorSettings.NotifyStoreOwnerAboutVendorInformationChange)
            await _workflowMessageService.SendVendorInformationChangeStoreOwnerNotificationAsync(vendor, _localizationSettings.DefaultAdminLanguageId);

        return Ok(await _vendorModelFactory.PrepareVendorInfoModelAsync(new VendorInfoModel(), false));
    }

    #endregion
}

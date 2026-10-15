using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Attributes;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Common;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the contact endpoints (contact us, contact vendor)
/// </summary>
/// <remarks>
/// CAPTCHA is not supported by the API, so it's not validated even if enabled for the contact forms (DisplayCaptcha is always false)
/// </remarks>
[HeadlessApiRoute]
public class ContactApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CommonSettings _commonSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly IAttributeService<ContactFormAttribute, ContactFormAttributeValue> _contactFormAttributeService;
    protected readonly ICommonModelFactory _commonModelFactory;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly IHtmlFormatter _htmlFormatter;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPermissionService _permissionService;
    protected readonly IVendorService _vendorService;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly VendorSettings _vendorSettings;

    #endregion

    #region Ctor

    public ContactApiController(CommonSettings commonSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        IAttributeService<ContactFormAttribute, ContactFormAttributeValue> contactFormAttributeService,
        ICommonModelFactory commonModelFactory,
        ICustomerActivityService customerActivityService,
        IHtmlFormatter htmlFormatter,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        IVendorService vendorService,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        VendorSettings vendorSettings)
    {
        _commonSettings = commonSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _contactFormAttributeService = contactFormAttributeService;
        _commonModelFactory = commonModelFactory;
        _customerActivityService = customerActivityService;
        _htmlFormatter = htmlFormatter;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _vendorService = vendorService;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _vendorSettings = vendorSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether the current customer can navigate the public store
    /// </summary>
    /// <remarks>
    /// The contact us page is available even when the store is closed, so the store access filter is ignored and only the navigation is checked here
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the navigation is allowed</returns>
    protected virtual async Task<IActionResult> CheckNavigationAllowedAsync()
    {
        if (await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.PUBLIC_STORE_ALLOW_NAVIGATION))
            return null;

        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.NavigationNotAllowed"));
    }

    /// <summary>
    /// Check whether customers can contact the vendor
    /// </summary>
    /// <param name="vendor">Vendor</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the vendor can be contacted</returns>
    protected virtual async Task<IActionResult> CheckVendorContactAllowedAsync(Vendor vendor)
    {
        if (!_vendorSettings.AllowCustomersToContactVendors)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ContactVendor.Disabled"));

        if (!_headlessApiCatalogService.IsVendorAvailable(vendor))
            return NotFoundProblem();

        return null;
    }

    /// <summary>
    /// Parse the custom contact form attributes selected in the model (like ParseCustomContactFormAttributesAsync of the storefront)
    /// </summary>
    /// <remarks>
    /// The selected values are passed in ContactFormAttributes of the model: the identifiers of the selected values are marked with IsPreSelected
    /// (dropdown list, radio list, checkboxes), the entered text is passed in DefaultValue (textbox, multiline textbox)
    /// </remarks>
    /// <param name="model">Contact us model</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the attribute names, values and errors</returns>
    protected virtual async Task<IList<(string Name, string Value, string Error)>> ParseCustomContactFormAttributesAsync(ContactUsModel model)
    {
        var result = new List<(string Name, string Value, string Error)>();

        var attributes = await _contactFormAttributeService.GetAllAttributesAsync();
        foreach (var attribute in attributes)
        {
            var attributeModel = model.ContactFormAttributes?.FirstOrDefault(item => item.Id == attribute.Id);
            var selectedValueIds = attributeModel?.Values?
                .Where(valueModel => valueModel.IsPreSelected)
                .Select(valueModel => valueModel.Id)
                .ToList() ?? new List<int>();
            var inputValue = string.Empty;

            switch (attribute.AttributeControlType)
            {
                case AttributeControlType.DropdownList:
                case AttributeControlType.RadioList:
                case AttributeControlType.Checkboxes:
                    {
                        //only one value can be selected in dropdown and radio lists
                        if (attribute.AttributeControlType != AttributeControlType.Checkboxes)
                            selectedValueIds = selectedValueIds.Take(1).ToList();

                        var attributeValues = await _contactFormAttributeService.GetAttributeValuesAsync(attribute.Id);
                        var selectedValues = attributeValues
                            .Where(value => selectedValueIds.Contains(value.Id))
                            .Select(value => value.Name)
                            .ToList();
                        inputValue = string.Join(", ", selectedValues);
                    }
                    break;

                case AttributeControlType.TextBox:
                case AttributeControlType.MultilineTextbox:
                    {
                        if (!string.IsNullOrWhiteSpace(attributeModel?.DefaultValue))
                            inputValue = WebUtility.HtmlEncode(attributeModel.DefaultValue.Trim());
                    }
                    break;

                //not supported attributes
                default:
                    continue;
            }

            var notFoundWarning = string.Empty;
            if (attribute.IsRequired && string.IsNullOrEmpty(inputValue))
            {
                notFoundWarning = string.Format(await _localizationService.GetResourceAsync("ContactUs.SelectAttribute"),
                    await _localizationService.GetLocalizedAsync(attribute, entity => entity.Name));
            }

            result.Add((attribute.Name, inputValue, notFoundWarning));
        }

        return result;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the contact us form (prefilled with the customer data, incl. the custom contact form attributes)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the contact us form</returns>
    [HttpGet("contact-us")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(ContactUsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ContactUs()
    {
        if (await CheckNavigationAllowedAsync() is IActionResult problem)
            return problem;

        var model = await _commonModelFactory.PrepareContactUsModelAsync(new ContactUsModel(), false);
        model.DisplayCaptcha = false;

        return Ok(model);
    }

    /// <summary>
    /// Sends the contact us enquiry to the store owner
    /// </summary>
    /// <param name="model">Contact us model (Email, FullName, Subject if enabled, Enquiry, selected ContactFormAttributes)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the model with the result message</returns>
    [HttpPost("contact-us")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(ContactUsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> ContactUsSend(ContactUsModel model)
    {
        if (await CheckNavigationAllowedAsync() is IActionResult problem)
            return problem;

        var customFields = await ParseCustomContactFormAttributesAsync(model);
        foreach (var (_, _, error) in customFields.Where(field => !string.IsNullOrEmpty(field.Error)))
            ModelState.AddModelError(string.Empty, error);

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var subject = _commonSettings.SubjectFieldOnContactUsForm ? model.Subject : null;
        var body = _htmlFormatter.FormatText(model.Enquiry);

        await _workflowMessageService.SendContactUsMessageAsync((await _workContext.GetWorkingLanguageAsync()).Id,
            model.Email, model.FullName, subject, body, customFields.ToDictionary(field => field.Name, field => field.Value));

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.ContactUs",
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.ContactUs"));

        model.SuccessfullySent = true;
        model.Result = await _localizationService.GetResourceAsync("ContactUs.YourEnquiryHasBeenSent");
        model.DisplayCaptcha = false;

        return Ok(model);
    }

    /// <summary>
    /// Gets the contact vendor form (prefilled with the customer data)
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the contact vendor form</returns>
    [HttpGet("vendors/{vendorId:int}/contact")]
    [ProducesResponseType(typeof(ContactVendorModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ContactVendor(int vendorId)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (await CheckVendorContactAllowedAsync(vendor) is IActionResult problem)
            return problem;

        var model = await _commonModelFactory.PrepareContactVendorModelAsync(new ContactVendorModel(), vendor, false);
        model.DisplayCaptcha = false;

        return Ok(model);
    }

    /// <summary>
    /// Sends the enquiry to the vendor
    /// </summary>
    /// <param name="vendorId">Vendor identifier</param>
    /// <param name="model">Contact vendor model (Email, FullName, Subject if enabled, Enquiry)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the model with the result message</returns>
    [HttpPost("vendors/{vendorId:int}/contact")]
    [ProducesResponseType(typeof(ContactVendorModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ContactVendorSend(int vendorId, ContactVendorModel model)
    {
        var vendor = await _vendorService.GetVendorByIdAsync(vendorId);
        if (await CheckVendorContactAllowedAsync(vendor) is IActionResult problem)
            return problem;

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        model = await _commonModelFactory.PrepareContactVendorModelAsync(model, vendor, true);

        var subject = _commonSettings.SubjectFieldOnContactUsForm ? model.Subject : null;
        var body = _htmlFormatter.FormatText(model.Enquiry);

        await _workflowMessageService.SendContactVendorMessageAsync(vendor, (await _workContext.GetWorkingLanguageAsync()).Id,
            model.Email, model.FullName, subject, body);

        model.SuccessfullySent = true;
        model.Result = await _localizationService.GetResourceAsync("ContactVendor.YourEnquiryHasBeenSent");
        model.DisplayCaptcha = false;

        return Ok(model);
    }

    #endregion
}

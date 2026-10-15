using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;
using Nop.Services.Attributes;
using Nop.Services.Common;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to manage the checkout attributes of the current customer
/// </summary>
/// <remarks>
/// The attributes are passed with the same keys as the storefront form ("checkout_attribute_{id}"),
/// so the parsing corresponds to ShoppingCartController.ParseAndSaveCheckoutAttributesAsync of the storefront
/// </remarks>
public class HeadlessApiCheckoutAttributeService
{
    #region Fields

    protected readonly IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeParser;
    protected readonly IAttributeService<CheckoutAttribute, CheckoutAttributeValue> _checkoutAttributeService;
    protected readonly IDownloadService _downloadService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INopFileProvider _fileProvider;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStaticCacheManager _staticCacheManager;
    protected readonly IStoreContext _storeContext;
    protected readonly IStoreMappingService _storeMappingService;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public HeadlessApiCheckoutAttributeService(IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeParser,
        IAttributeService<CheckoutAttribute, CheckoutAttributeValue> checkoutAttributeService,
        IDownloadService downloadService,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        INopFileProvider fileProvider,
        IShoppingCartService shoppingCartService,
        IStaticCacheManager staticCacheManager,
        IStoreContext storeContext,
        IStoreMappingService storeMappingService,
        IWorkContext workContext)
    {
        _checkoutAttributeParser = checkoutAttributeParser;
        _checkoutAttributeService = checkoutAttributeService;
        _downloadService = downloadService;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _fileProvider = fileProvider;
        _shoppingCartService = shoppingCartService;
        _staticCacheManager = staticCacheManager;
        _storeContext = storeContext;
        _storeMappingService = storeMappingService;
        _workContext = workContext;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Convert the attributes to a form collection with the storefront keys
    /// </summary>
    /// <param name="attributes">Attributes</param>
    /// <returns>Form collection</returns>
    protected virtual IFormCollection PrepareForm(IDictionary<string, string> attributes)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var (key, value) in attributes ?? new Dictionary<string, string>())
            values[key] = value;

        return new FormCollection(values);
    }

    /// <summary>
    /// Get the checkout attributes available for the cart
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the checkout attributes</returns>
    protected virtual async Task<IList<CheckoutAttribute>> GetAvailableAttributesAsync(IList<ShoppingCartItem> cart)
    {
        var excludeShippableAttributes = !await _shoppingCartService.ShoppingCartRequiresShippingAsync(cart);
        var store = await _storeContext.GetCurrentStoreAsync();

        return await _checkoutAttributeService.GetAllAttributesAsync(_staticCacheManager, _storeMappingService, store.Id, excludeShippableAttributes);
    }

    /// <summary>
    /// Add the selected attribute value, if the value belongs to the attribute
    /// </summary>
    /// <param name="attributesXml">Attributes in XML format</param>
    /// <param name="attribute">Checkout attribute</param>
    /// <param name="value">Selected value identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated attributes in XML format</returns>
    protected virtual async Task<string> AddAttributeValueAsync(string attributesXml, CheckoutAttribute attribute, string value)
    {
        if (!int.TryParse(value?.Trim(), out var selectedValueId) || selectedValueId <= 0)
            return attributesXml;

        var attributeValues = await _checkoutAttributeService.GetAttributeValuesAsync(attribute.Id);
        if (attributeValues.All(attributeValue => attributeValue.Id != selectedValueId))
            return attributesXml;

        return _checkoutAttributeParser.AddAttribute(attributesXml, attribute, selectedValueId.ToString());
    }

    #endregion

    #region Methods

    /// <summary>
    /// Parses the checkout attributes passed with the storefront keys ("checkout_attribute_{id}") to XML
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <param name="attributes">Attributes, e.g. "checkout_attribute_1": "5", "checkout_attribute_2": "6,7", "checkout_attribute_3": "text",
    /// "checkout_attribute_4_day"/"_month"/"_year" for date pickers, "checkout_attribute_5": "download GUID" for file uploads</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the checkout attributes in XML format</returns>
    public virtual async Task<string> ParseCheckoutAttributesAsync(IList<ShoppingCartItem> cart, IDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var form = PrepareForm(attributes);
        var attributesXml = string.Empty;
        var checkoutAttributes = await GetAvailableAttributesAsync(cart);
        foreach (var attribute in checkoutAttributes)
        {
            var controlId = $"checkout_attribute_{attribute.Id}";
            switch (attribute.AttributeControlType)
            {
                case AttributeControlType.DropdownList:
                case AttributeControlType.RadioList:
                case AttributeControlType.ColorSquares:
                case AttributeControlType.ImageSquares:
                    {
                        var ctrlAttributes = form[controlId];
                        if (!StringValues.IsNullOrEmpty(ctrlAttributes))
                            attributesXml = await AddAttributeValueAsync(attributesXml, attribute, ctrlAttributes.ToString());
                    }
                    break;

                case AttributeControlType.Checkboxes:
                    {
                        var cblAttributes = form[controlId];
                        if (!StringValues.IsNullOrEmpty(cblAttributes))
                        {
                            foreach (var item in cblAttributes.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                                attributesXml = await AddAttributeValueAsync(attributesXml, attribute, item);
                        }
                    }
                    break;

                case AttributeControlType.ReadonlyCheckboxes:
                    {
                        //load read-only (already server-side selected) values
                        var attributeValues = await _checkoutAttributeService.GetAttributeValuesAsync(attribute.Id);
                        foreach (var selectedAttributeId in attributeValues.Where(value => value.IsPreSelected).Select(value => value.Id).ToList())
                            attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, selectedAttributeId.ToString());
                    }
                    break;

                case AttributeControlType.TextBox:
                case AttributeControlType.MultilineTextbox:
                    {
                        var ctrlAttributes = form[controlId];
                        if (!StringValues.IsNullOrEmpty(ctrlAttributes))
                        {
                            var enteredText = ctrlAttributes.ToString().Trim();
                            attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, enteredText);
                        }
                    }
                    break;

                case AttributeControlType.Datepicker:
                    {
                        var day = form[controlId + "_day"];
                        var month = form[controlId + "_month"];
                        var year = form[controlId + "_year"];
                        DateTime? selectedDate = null;
                        try
                        {
                            selectedDate = new DateTime(int.Parse(year), int.Parse(month), int.Parse(day));
                        }
                        catch
                        {
                            // ignored
                        }

                        //the storefront saves the date formatted with the current culture
                        if (selectedDate.HasValue)
                            attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, selectedDate.Value.ToString("D", CultureInfo.CurrentCulture));
                    }
                    break;

                case AttributeControlType.FileUpload:
                    {
                        _ = Guid.TryParse(form[controlId], out var downloadGuid);
                        var download = await _downloadService.GetDownloadByGuidAsync(downloadGuid);
                        if (download != null)
                            attributesXml = _checkoutAttributeParser.AddAttribute(attributesXml, attribute, download.DownloadGuid.ToString());
                    }
                    break;

                default:
                    break;
            }
        }

        //validate conditional attributes (if specified)
        foreach (var attribute in checkoutAttributes)
        {
            var conditionMet = await _checkoutAttributeParser.IsConditionMetAsync(attribute.ConditionAttributeXml, attributesXml);
            if (conditionMet.HasValue && !conditionMet.Value)
                attributesXml = _checkoutAttributeParser.RemoveAttribute(attributesXml, attribute.Id);
        }

        return attributesXml;
    }

    /// <summary>
    /// Parses the checkout attributes passed with the storefront keys and saves them for the current customer
    /// (like ShoppingCartController.ParseAndSaveCheckoutAttributesAsync of the storefront)
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <param name="attributes">Attributes (see <see cref="ParseCheckoutAttributesAsync"/>); attributes not passed are removed</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the saved checkout attributes in XML format</returns>
    public virtual async Task<string> SaveCheckoutAttributesAsync(IList<ShoppingCartItem> cart, IDictionary<string, string> attributes)
    {
        var attributesXml = await ParseCheckoutAttributesAsync(cart, attributes);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();
        await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.CheckoutAttributes, attributesXml, store.Id);

        return attributesXml;
    }

    /// <summary>
    /// Gets the checkout attributes saved for the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the checkout attributes in XML format</returns>
    public virtual async Task<string> GetSavedCheckoutAttributesAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var store = await _storeContext.GetCurrentStoreAsync();

        return await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.CheckoutAttributes, store.Id);
    }

    /// <summary>
    /// Gets the warnings of the shopping cart including the validation of the saved checkout attributes (e.g. required attributes),
    /// like the storefront does before starting the checkout
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the warnings (empty if the checkout can be started)</returns>
    public virtual async Task<IList<string>> GetCheckoutAttributeWarningsAsync(IList<ShoppingCartItem> cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var attributesXml = await GetSavedCheckoutAttributesAsync();

        return await _shoppingCartService.GetShoppingCartWarningsAsync(cart, attributesXml, true);
    }

    /// <summary>
    /// Gets the state of the conditional checkout attributes for the saved attributes (like CheckoutAttributeChange of the storefront)
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the enabled and disabled attribute identifiers</returns>
    public virtual async Task<CheckoutAttributeConditionsModel> GetAttributeConditionsAsync(IList<ShoppingCartItem> cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var attributesXml = await GetSavedCheckoutAttributesAsync();
        var model = new CheckoutAttributeConditionsModel();
        foreach (var attribute in await GetAvailableAttributesAsync(cart))
        {
            var conditionMet = await _checkoutAttributeParser.IsConditionMetAsync(attribute.ConditionAttributeXml, attributesXml);
            if (!conditionMet.HasValue)
                continue;

            if (conditionMet.Value)
                model.EnabledAttributeIds.Add(attribute.Id);
            else
                model.DisabledAttributeIds.Add(attribute.Id);
        }

        return model;
    }

    /// <summary>
    /// Uploads a file for a checkout attribute of the "File upload" type (like UploadFileCheckoutAttribute of the storefront)
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <param name="attributeId">Checkout attribute identifier</param>
    /// <param name="file">Uploaded file</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the created download (null on error) and the error</returns>
    public virtual async Task<(Download download, string error)> UploadFileAsync(IList<ShoppingCartItem> cart, int attributeId, IFormFile file)
    {
        ArgumentNullException.ThrowIfNull(cart);

        var attribute = (await GetAvailableAttributesAsync(cart)).FirstOrDefault(item => item.Id == attributeId);
        if (attribute == null || attribute.AttributeControlType != AttributeControlType.FileUpload)
            return (null, await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.CheckoutAttributes.NotFileUpload"));

        if (file == null || file.Length == 0)
            return (null, await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Upload.NoFile"));

        //remove path (passed in IE)
        var fileName = _fileProvider.GetFileName(file.FileName);
        var fileExtension = _fileProvider.GetFileExtension(fileName);
        if (!string.IsNullOrEmpty(fileExtension))
            fileExtension = fileExtension.ToLowerInvariant();

        //the storefront validates the allowed extensions on the client side only, so we check them here
        if (!string.IsNullOrEmpty(attribute.ValidationFileAllowedExtensions))
        {
            var allowedExtensions = attribute.ValidationFileAllowedExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.TrimStart('.').ToLowerInvariant())
                .ToList();
            if (!allowedExtensions.Contains(fileExtension?.TrimStart('.') ?? string.Empty))
            {
                return (null, string.Format(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Upload.ExtensionNotAllowed"),
                    string.Join(", ", allowedExtensions)));
            }
        }

        var fileBinary = await _downloadService.GetDownloadBitsAsync(file);
        if (attribute.ValidationFileMaximumSize.HasValue)
        {
            //compare in bytes
            var maxFileSizeBytes = attribute.ValidationFileMaximumSize.Value * 1024;
            if (fileBinary.Length > maxFileSizeBytes)
                return (null, string.Format(await _localizationService.GetResourceAsync("ShoppingCart.MaximumUploadedFileSize"), attribute.ValidationFileMaximumSize.Value));
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

        return (download, string.Empty);
    }

    #endregion
}

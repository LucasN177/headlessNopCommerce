using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Menus;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Tax;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Common;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Common;
using Nop.Services.Localization;
using Nop.Services.Security;
using Nop.Services.Seo;
using Nop.Services.Topics;
using Nop.Web.Factories;
using Nop.Web.Models.Menus;
using Nop.Web.Models.Sitemap;
using Nop.Web.Models.Topics;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the common endpoints (layout, URL resolving, menus, topics, sitemap)
/// </summary>
[HeadlessApiRoute]
public class CommonApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly BlogSettings _blogSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly ICatalogModelFactory _catalogModelFactory;
    protected readonly ICommonModelFactory _commonModelFactory;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IMenuModelFactory _menuModelFactory;
    protected readonly IPermissionService _permissionService;
    protected readonly ISitemapModelFactory _sitemapModelFactory;
    protected readonly IStoreContext _storeContext;
    protected readonly ITopicModelFactory _topicModelFactory;
    protected readonly ITopicService _topicService;
    protected readonly IUrlRecordService _urlRecordService;
    protected readonly IWorkContext _workContext;
    protected readonly ShoppingCartSettings _shoppingCartSettings;
    protected readonly SitemapSettings _sitemapSettings;
    protected readonly StoreInformationSettings _storeInformationSettings;
    protected readonly TaxSettings _taxSettings;
    protected readonly VendorSettings _vendorSettings;

    #endregion

    #region Ctor

    public CommonApiController(BlogSettings blogSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        ICatalogModelFactory catalogModelFactory,
        ICommonModelFactory commonModelFactory,
        IGenericAttributeService genericAttributeService,
        ILocalizationService localizationService,
        IMenuModelFactory menuModelFactory,
        IPermissionService permissionService,
        ISitemapModelFactory sitemapModelFactory,
        IStoreContext storeContext,
        ITopicModelFactory topicModelFactory,
        ITopicService topicService,
        IUrlRecordService urlRecordService,
        IWorkContext workContext,
        ShoppingCartSettings shoppingCartSettings,
        SitemapSettings sitemapSettings,
        StoreInformationSettings storeInformationSettings,
        TaxSettings taxSettings,
        VendorSettings vendorSettings)
    {
        _blogSettings = blogSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _catalogModelFactory = catalogModelFactory;
        _commonModelFactory = commonModelFactory;
        _genericAttributeService = genericAttributeService;
        _localizationService = localizationService;
        _menuModelFactory = menuModelFactory;
        _permissionService = permissionService;
        _sitemapModelFactory = sitemapModelFactory;
        _storeContext = storeContext;
        _topicModelFactory = topicModelFactory;
        _topicService = topicService;
        _urlRecordService = urlRecordService;
        _workContext = workContext;
        _shoppingCartSettings = shoppingCartSettings;
        _sitemapSettings = sitemapSettings;
        _storeInformationSettings = storeInformationSettings;
        _taxSettings = taxSettings;
        _vendorSettings = vendorSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether to display the EU cookie law warning to the current customer (like the EuCookieLaw view component)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether to display the warning</returns>
    protected virtual async Task<bool> DisplayEuCookieLawWarningAsync()
    {
        if (!_storeInformationSettings.DisplayEuCookieLawWarning)
            return false;

        //ignore search engines because some pages could be indexed with the EU cookie as description
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (customer.IsSearchEngineAccount())
            return false;

        //already accepted?
        var store = await _storeContext.GetCurrentStoreAsync();

        return !await _genericAttributeService.GetAttributeAsync<bool>(customer, NopCustomerDefaults.EuCookieLawAcceptedAttribute, store.Id);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Resolves an SEO friendly URL (slug) to the entity it belongs to
    /// </summary>
    /// <param name="slug">Slug, e.g. "apple-iphone-16"</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the entity info</returns>
    [HttpGet("common/url/resolve")]
    [ProducesResponseType(typeof(UrlResolveModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> ResolveUrl([FromQuery] string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return NotFoundProblem();

        var urlRecord = await _urlRecordService.GetBySlugAsync(slug.Trim().Trim('/'));
        if (urlRecord == null)
            return NotFoundProblem();

        var model = new UrlResolveModel
        {
            EntityName = urlRecord.EntityName,
            EntityId = urlRecord.EntityId,
            LanguageId = urlRecord.LanguageId
        };

        //the slug is no longer active, so the frontend should redirect to the current one
        if (!urlRecord.IsActive)
        {
            var activeSlug = await _urlRecordService.GetActiveSlugAsync(urlRecord.EntityId, urlRecord.EntityName, urlRecord.LanguageId);
            if (string.IsNullOrEmpty(activeSlug))
                return NotFoundProblem();

            model.RedirectToSlug = activeSlug;
        }

        return Ok(model);
    }

    /// <summary>
    /// Gets the menus of the passed type
    /// </summary>
    /// <param name="menuType">Menu type (Main, Footer)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the menus</returns>
    [HttpGet("common/menus/{menuType}")]
    [ProducesResponseType(typeof(IList<MenuModel>), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Menus(MenuType menuType)
    {
        return Ok(await _menuModelFactory.PrepareMenuModelsAsync(menuType));
    }

    /// <summary>
    /// Gets a topic (content page) by identifier
    /// </summary>
    /// <param name="topicId">Topic identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the topic</returns>
    [HttpGet("common/topics/{topicId:int}")]
    [ProducesResponseType(typeof(TopicModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Topic(int topicId)
    {
        var topic = await _topicService.GetTopicByIdAsync(topicId);
        if (!await _headlessApiCatalogService.IsTopicAvailableAsync(topic))
            return NotFoundProblem();

        //the body of password protected topics is not included
        return Ok(await _topicModelFactory.PrepareTopicModelAsync(topic));
    }

    /// <summary>
    /// Gets a topic (content page) by system name, e.g. "ShippingInfo", "PrivacyInfo", "ConditionsOfUse"
    /// </summary>
    /// <param name="systemName">Topic system name</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the topic</returns>
    [HttpGet("common/topics/{systemName}")]
    [ProducesResponseType(typeof(TopicModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> TopicBySystemName(string systemName)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var topic = await _topicService.GetTopicBySystemNameAsync(systemName, store.Id);
        if (!await _headlessApiCatalogService.IsTopicAvailableAsync(topic))
            return NotFoundProblem();

        return Ok(await _topicModelFactory.PrepareTopicModelAsync(topic));
    }

    /// <summary>
    /// Gets the data of the common layout (logo, header links, footer, social links, favicon, selectors, search box, store state)
    /// </summary>
    /// <remarks>
    /// Available even when the store is closed or navigation is not allowed, so the frontend can render the layout of the login page
    /// </remarks>
    /// <returns>A task that represents the asynchronous operation; the task result contains the layout data</returns>
    [HttpGet("common/layout")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(typeof(LayoutModel), StatusCodes.Status200OK)]
    public virtual async Task<IActionResult> Layout()
    {
        var model = new LayoutModel
        {
            Logo = await _commonModelFactory.PrepareLogoModelAsync(),
            HeaderLinks = await _commonModelFactory.PrepareHeaderLinksModelAsync(),
            Footer = await _commonModelFactory.PrepareFooterModelAsync(),
            Social = await _commonModelFactory.PrepareSocialModelAsync(),
            FaviconAndAppIcons = await _commonModelFactory.PrepareFaviconAndAppIconsModelAsync(),
            LanguageSelector = await _commonModelFactory.PrepareLanguageSelectorModelAsync(),
            CurrencySelector = await _commonModelFactory.PrepareCurrencySelectorModelAsync(),
            TaxTypeSelector = _taxSettings.AllowCustomersToSelectTaxDisplayType
                ? await _commonModelFactory.PrepareTaxTypeSelectorModelAsync()
                : null,
            SearchBox = await _catalogModelFactory.PrepareSearchBoxModelAsync(),
            StoreClosed = _storeInformationSettings.StoreClosed,
            CanAccessClosedStore = await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ACCESS_CLOSED_STORE),
            NavigationAllowed = await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.PUBLIC_STORE_ALLOW_NAVIGATION),
            DisplayEuCookieLawWarning = await DisplayEuCookieLawWarningAsync(),
            MiniShoppingCartEnabled = _shoppingCartSettings.MiniShoppingCartEnabled
                && await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART),
            BlogEnabled = _blogSettings.Enabled,
            SitemapEnabled = _sitemapSettings.SitemapEnabled,
            AllowCustomersToContactVendors = _vendorSettings.AllowCustomersToContactVendors
        };

        return Ok(model);
    }

    /// <summary>
    /// Stores that the current customer has accepted the EU cookie law warning
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the empty result</returns>
    [HttpPost("common/eu-cookie-law/accept")]
    [HeadlessApiStoreAccess(ignore: true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> AcceptEuCookieLaw()
    {
        if (!_storeInformationSettings.DisplayEuCookieLawWarning)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Common.EuCookieLawDisabled"));

        var store = await _storeContext.GetCurrentStoreAsync();
        await _genericAttributeService.SaveAttributeAsync(await _workContext.GetCurrentCustomerAsync(), NopCustomerDefaults.EuCookieLawAcceptedAttribute, true, store.Id);

        return NoContent();
    }

    /// <summary>
    /// Gets the sitemap page (general pages, categories, manufacturers, products, topics, ...); the item URLs are storefront URLs
    /// </summary>
    /// <param name="pageModel">Paging (PageNumber, starting with 1)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the sitemap</returns>
    [HttpGet("common/sitemap")]
    [ProducesResponseType(typeof(SitemapModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Sitemap([FromQuery] SitemapPageModel pageModel)
    {
        if (!_sitemapSettings.SitemapEnabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Common.SitemapDisabled"));

        return Ok(await _sitemapModelFactory.PrepareSitemapModelAsync(pageModel ?? new SitemapPageModel()));
    }

    #endregion
}

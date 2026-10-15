using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Http;
using Nop.Plugin.Misc.HeadlessApi.Models.ShoppingCart;
using Nop.Plugin.Misc.HeadlessApi.Models.Wishlist;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the wishlist endpoints (default and custom wishlists, shared wishlists, email a friend)
/// </summary>
/// <remarks>
/// All changing endpoints return the updated wishlist, so the frontend doesn't need to reload it.
/// The available custom wishlists of the customer are part of the wishlist model ("customWishlistItems").
/// CAPTCHA is not supported by the API.
/// </remarks>
[HeadlessApiRoute("wishlist")]
public class WishlistApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly HeadlessApiShoppingCartService _headlessApiShoppingCartService;
    protected readonly ICustomerService _customerService;
    protected readonly ICustomWishlistService _customWishlistService;
    protected readonly IHtmlFormatter _htmlFormatter;
    protected readonly ILocalizationService _localizationService;
    protected readonly IPermissionService _permissionService;
    protected readonly IProductService _productService;
    protected readonly IShoppingCartModelFactory _shoppingCartModelFactory;
    protected readonly IShoppingCartService _shoppingCartService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWebHelper _webHelper;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly ShoppingCartSettings _shoppingCartSettings;

    #endregion

    #region Ctor

    public WishlistApiController(HeadlessApiShoppingCartService headlessApiShoppingCartService,
        ICustomerService customerService,
        ICustomWishlistService customWishlistService,
        IHtmlFormatter htmlFormatter,
        ILocalizationService localizationService,
        IPermissionService permissionService,
        IProductService productService,
        IShoppingCartModelFactory shoppingCartModelFactory,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        IWebHelper webHelper,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        ShoppingCartSettings shoppingCartSettings)
    {
        _headlessApiShoppingCartService = headlessApiShoppingCartService;
        _customerService = customerService;
        _customWishlistService = customWishlistService;
        _htmlFormatter = htmlFormatter;
        _localizationService = localizationService;
        _permissionService = permissionService;
        _productService = productService;
        _shoppingCartModelFactory = shoppingCartModelFactory;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _webHelper = webHelper;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _shoppingCartSettings = shoppingCartSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Prepare the wishlist model of the current customer
    /// </summary>
    /// <param name="customWishlistId">Custom wishlist identifier; null for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the wishlist model</returns>
    protected virtual async Task<WishlistModel> PrepareWishlistModelAsync(int? customWishlistId = null)
    {
        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist, customWishlistId);

        return await _shoppingCartModelFactory.PrepareWishlistModelAsync(new WishlistModel(), cart, true, customWishlistId);
    }

    /// <summary>
    /// Gets an item of any wishlist (default or custom) of the current customer
    /// </summary>
    /// <param name="itemId">Wishlist item identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the item or null if it doesn't belong to the current customer</returns>
    protected virtual async Task<ShoppingCartItem> GetWishlistItemAsync(int itemId)
    {
        //0 loads the items of all wishlists
        var items = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist, 0);

        return items.FirstOrDefault(item => item.Id == itemId);
    }

    /// <summary>
    /// Gets a custom wishlist of the current customer
    /// </summary>
    /// <param name="listId">Custom wishlist identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the custom wishlist or null if it doesn't belong to the current customer</returns>
    protected virtual async Task<CustomWishlist> GetCustomWishlistAsync(int listId)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        var wishlist = await _customWishlistService.GetCustomWishlistByIdAsync(listId);

        return wishlist?.CustomerId == customer.Id ? wishlist : null;
    }

    /// <summary>
    /// Check whether the wishlist is enabled for the current customer
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the wishlist is enabled</returns>
    protected virtual async Task<IActionResult> CheckWishlistEnabledAsync()
    {
        if (await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_WISHLIST))
            return null;

        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Wishlist.Disabled"));
    }

    /// <summary>
    /// Check whether the current customer can manage custom wishlists
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if custom wishlists can be used</returns>
    protected virtual async Task<IActionResult> CheckCustomWishlistsAllowedAsync()
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        if (await _customerService.IsGuestAsync(await _workContext.GetCurrentCustomerAsync()))
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Wishlist.MultipleWishlistNotForGuest"));

        if (!_shoppingCartSettings.AllowMultipleWishlist)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Wishlist.NotAllowMultipleWishlist"));

        return null;
    }

    /// <summary>
    /// Check whether a custom wishlist with the same name already exists
    /// </summary>
    /// <param name="customerId">Customer identifier</param>
    /// <param name="wishlistName">Wishlist name</param>
    /// <param name="selectedWishlists">Custom wishlists of the customer; pass null to load them</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the error message or empty string if the name is unique</returns>
    protected virtual async Task<string> CheckDuplicateWishlistNameAsync(int customerId, string wishlistName, IList<CustomWishlist> selectedWishlists = null)
    {
        var currentWishlists = selectedWishlists ?? await _customWishlistService.GetAllCustomWishlistsAsync(customerId);
        if (currentWishlists.Any(wishlist => string.Equals(wishlist.Name, wishlistName, StringComparison.InvariantCultureIgnoreCase)))
            return await _localizationService.GetResourceAsync("Wishlist.DuplicateName");

        return string.Empty;
    }

    /// <summary>
    /// Move the newest item of a product from the default wishlist to a custom wishlist
    /// </summary>
    /// <param name="productId">Product identifier</param>
    /// <param name="customWishlistId">Custom wishlist identifier</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    protected virtual async Task MoveProductToCustomWishlistAsync(int productId, int customWishlistId)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (product == null)
            return;

        var shoppingCartItem = (await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist))
            .Where(item => item.ProductId == product.Id && item.CustomWishlistId == null)
            .OrderByDescending(item => item.UpdatedOnUtc)
            .FirstOrDefault();

        if (shoppingCartItem != null)
            await _shoppingCartService.MoveItemToCustomWishlistAsync(shoppingCartItem.Id, customWishlistId);
    }

    /// <summary>
    /// Prepare the URL of a shared wishlist sent by "Email a friend"
    /// </summary>
    /// <param name="customer">Customer who shares the wishlist</param>
    /// <param name="customWishlistId">Custom wishlist identifier; null for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the URL</returns>
    /// <remarks>
    /// The storefront URL is used like Nop.Web does; the frontend page has to call GET wishlist/shared/{customerGuid}?listId=
    /// </remarks>
    protected virtual Task<string> PrepareSharedWishlistUrlAsync(Customer customer, int? customWishlistId)
    {
        return Task.FromResult(Url.RouteUrl(NopRouteNames.General.WISHLIST,
            new { customerGuid = customer.CustomerGuid, list = customWishlistId },
            _webHelper.GetCurrentRequestProtocol()));
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the wishlist of the current customer
    /// </summary>
    /// <param name="listId">Custom wishlist identifier; empty for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the wishlist</returns>
    [HttpGet]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Get([FromQuery] int? listId = null)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        return Ok(await PrepareWishlistModelAsync(listId));
    }

    /// <summary>
    /// Gets the wishlist shared by another customer (read-only)
    /// </summary>
    /// <param name="customerGuid">GUID of the customer who shared the wishlist</param>
    /// <param name="listId">Custom wishlist identifier; empty for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the wishlist</returns>
    [HttpGet("shared/{customerGuid:guid}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Shared(Guid customerGuid, [FromQuery] int? listId = null)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var customer = await _customerService.GetCustomerByGuidAsync(customerGuid);
        if (customer == null)
            return NotFoundProblem();

        var store = await _storeContext.GetCurrentStoreAsync();
        var cart = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.Wishlist, store.Id, customWishlistId: listId);

        return Ok(await _shoppingCartModelFactory.PrepareWishlistModelAsync(new WishlistModel(), cart, false, listId));
    }

    /// <summary>
    /// Adds a product to the wishlist
    /// </summary>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated wishlist</returns>
    [HttpPost("items")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> AddItem(AddToCartRequest request)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var warnings = await _headlessApiShoppingCartService.AddToCartAsync(ShoppingCartType.Wishlist, request);
        if (warnings.Any())
            return WarningsProblem(warnings);

        return Ok(await PrepareWishlistModelAsync());
    }

    /// <summary>
    /// Changes the quantity of a wishlist item; the item is removed if the quantity is 0
    /// </summary>
    /// <param name="itemId">Wishlist item identifier</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated wishlist</returns>
    [HttpPut("items/{itemId:int}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> UpdateItem(int itemId, UpdateCartItemRequest request)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var item = await GetWishlistItemAsync(itemId);
        if (item == null)
            return NotFoundProblem();

        var warnings = await _headlessApiShoppingCartService.UpdateCartItemAsync(item, request.Quantity);
        if (warnings.Any())
            return WarningsProblem(warnings);

        return Ok(await PrepareWishlistModelAsync(item.CustomWishlistId));
    }

    /// <summary>
    /// Removes an item from the wishlist
    /// </summary>
    /// <param name="itemId">Wishlist item identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated wishlist</returns>
    [HttpDelete("items/{itemId:int}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> DeleteItem(int itemId)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var item = await GetWishlistItemAsync(itemId);
        if (item == null)
            return NotFoundProblem();

        await _headlessApiShoppingCartService.DeleteCartItemAsync(item);

        return Ok(await PrepareWishlistModelAsync(item.CustomWishlistId));
    }

    /// <summary>
    /// Moves a wishlist item to the shopping cart
    /// </summary>
    /// <param name="itemId">Wishlist item identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the updated wishlist</returns>
    [HttpPost("items/{itemId:int}/move-to-cart")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> MoveToCart(int itemId)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART))
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.Disabled"));

        var item = await GetWishlistItemAsync(itemId);
        if (item == null)
            return NotFoundProblem();

        var warnings = await _headlessApiShoppingCartService.MoveToCartAsync(item);
        if (warnings.Any())
            return WarningsProblem(warnings);

        return Ok(await PrepareWishlistModelAsync(item.CustomWishlistId));
    }

    /// <summary>
    /// Moves a wishlist item to another wishlist of the current customer
    /// </summary>
    /// <param name="itemId">Wishlist item identifier</param>
    /// <param name="request">Target wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the target wishlist</returns>
    [HttpPost("items/{itemId:int}/move")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> MoveToCustomWishlist(int itemId, MoveWishlistItemRequest request)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var item = await GetWishlistItemAsync(itemId);
        if (item == null)
            return NotFoundProblem();

        var listId = request.ListId > 0 ? request.ListId : null;
        if (listId.HasValue)
        {
            var wishlist = await GetCustomWishlistAsync(listId.Value);
            if (wishlist == null)
                return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.NotFound"));

            await _shoppingCartService.MoveItemToCustomWishlistAsync(item.Id, wishlist.Id);
        }
        else
            await _shoppingCartService.MoveItemToCustomWishlistAsync(item.Id);

        return Ok(await PrepareWishlistModelAsync(listId));
    }

    /// <summary>
    /// Adds wishlist items to the shopping cart; items of the own wishlist are removed from it if configured ("Move items from wishlist to cart")
    /// </summary>
    /// <param name="request">Items to add and the wishlist they belong to (own or shared)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the number of added items, the warnings and the updated wishlist</returns>
    [HttpPost("add-to-cart")]
    [ProducesResponseType(typeof(AddWishlistItemsToCartResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> AddItemsToCart(AddWishlistItemsToCartRequest request)
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.PublicStore.ENABLE_SHOPPING_CART))
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.ShoppingCart.Disabled"));

        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var customer = await _workContext.GetCurrentCustomerAsync();
        var pageCustomer = request.CustomerGuid.HasValue
            ? await _customerService.GetCustomerByGuidAsync(request.CustomerGuid.Value)
            : customer;
        if (pageCustomer == null)
            return NotFoundProblem();

        var isOwnWishlist = pageCustomer.Id == customer.Id;
        var store = await _storeContext.GetCurrentStoreAsync();
        var pageCart = await _shoppingCartService.GetShoppingCartAsync(pageCustomer, ShoppingCartType.Wishlist, store.Id, customWishlistId: request.ListId);

        var allWarnings = new List<string>();
        var countOfAddedItems = 0;
        var allIdsToAdd = request.ItemIds ?? new List<int>();
        foreach (var sci in pageCart.Where(item => allIdsToAdd.Contains(item.Id)))
        {
            var product = await _productService.GetProductByIdAsync(sci.ProductId);
            var warnings = await _shoppingCartService.AddToCartAsync(customer, product, ShoppingCartType.ShoppingCart, store.Id,
                sci.AttributesXml, sci.CustomerEnteredPrice,
                sci.RentalStartDateUtc, sci.RentalEndDateUtc, sci.Quantity, true);

            if (!warnings.Any())
            {
                countOfAddedItems++;

                //remove the item from the own wishlist
                if (_shoppingCartSettings.MoveItemsFromWishlistToCart && isOwnWishlist)
                    await _shoppingCartService.DeleteShoppingCartItemAsync(sci);
            }

            allWarnings.AddRange(warnings);
        }

        if (countOfAddedItems == 0)
            return WarningsProblem([await _localizationService.GetResourceAsync("Wishlist.AddToCart.NoAddedItems"), .. allWarnings]);

        var cart = await _shoppingCartService.GetShoppingCartAsync(pageCustomer, ShoppingCartType.Wishlist, store.Id, customWishlistId: request.ListId);

        return Ok(new AddWishlistItemsToCartResultModel
        {
            AddedItemsCount = countOfAddedItems,
            Warnings = allWarnings,
            Message = allWarnings.Any() ? await _localizationService.GetResourceAsync("Wishlist.AddToCart.Error") : null,
            Wishlist = await _shoppingCartModelFactory.PrepareWishlistModelAsync(new WishlistModel(), cart, isOwnWishlist, request.ListId)
        });
    }

    #endregion

    #region Custom wishlists

    /// <summary>
    /// Creates a custom wishlist; optionally moves a product from the default wishlist to it
    /// </summary>
    /// <param name="request">Wishlist name and optional product</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the new wishlist</returns>
    [HttpPost("lists")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> AddWishlist(CustomWishlistRequest request)
    {
        if (await CheckCustomWishlistsAllowedAsync() is IActionResult problem)
            return problem;

        var normalizedName = request.Name?.Trim();
        if (string.IsNullOrEmpty(normalizedName))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Wishlist.NameRequired")]);

        var customer = await _workContext.GetCurrentCustomerAsync();
        var currentWishlists = await _customWishlistService.GetAllCustomWishlistsAsync(customer.Id);

        //check if a wishlist with the same name already exists
        var errorMessage = await CheckDuplicateWishlistNameAsync(customer.Id, normalizedName, currentWishlists);
        if (!string.IsNullOrEmpty(errorMessage))
            return WarningsProblem([errorMessage]);

        //check if customer has reached the maximum number of custom wishlists allowed
        var maximumNumberOfCustomWishlist = _shoppingCartSettings.MaximumNumberOfCustomWishlist;
        if (currentWishlists.Count >= maximumNumberOfCustomWishlist)
            return WarningsProblem([string.Format(await _localizationService.GetResourceAsync("Wishlist.MaximumNumberReached"), maximumNumberOfCustomWishlist)]);

        var customWishlist = new CustomWishlist
        {
            CustomerId = customer.Id,
            Name = normalizedName,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _customWishlistService.AddCustomWishlistAsync(customWishlist);

        if (request.ProductId.HasValue)
            await MoveProductToCustomWishlistAsync(request.ProductId.Value, customWishlist.Id);

        return Ok(await PrepareWishlistModelAsync(customWishlist.Id));
    }

    /// <summary>
    /// Renames a custom wishlist
    /// </summary>
    /// <param name="listId">Custom wishlist identifier</param>
    /// <param name="request">New name</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the renamed wishlist</returns>
    [HttpPut("lists/{listId:int}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> RenameWishlist(int listId, CustomWishlistRequest request)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var wishlist = await GetCustomWishlistAsync(listId);
        if (wishlist == null)
            return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.NotFound"));

        var normalizedName = request.Name?.Trim();
        if (string.IsNullOrEmpty(normalizedName))
            return WarningsProblem([await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Wishlist.NameRequired")]);

        //check if a wishlist with the same name already exists
        var errorMessage = await CheckDuplicateWishlistNameAsync(wishlist.CustomerId, normalizedName);
        if (!string.IsNullOrEmpty(errorMessage))
            return WarningsProblem([errorMessage]);

        wishlist.Name = normalizedName;
        await _customWishlistService.UpdateCustomWishlistAsync(wishlist);

        return Ok(await PrepareWishlistModelAsync(wishlist.Id));
    }

    /// <summary>
    /// Deletes a custom wishlist including its items
    /// </summary>
    /// <param name="listId">Custom wishlist identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the default wishlist</returns>
    [HttpDelete("lists/{listId:int}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> DeleteWishlist(int listId)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        var wishlist = await GetCustomWishlistAsync(listId);
        if (wishlist == null)
            return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.NotFound"));

        //delete all shopping cart items associated with this wishlist
        foreach (var item in await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist, wishlist.Id))
            await _shoppingCartService.DeleteShoppingCartItemAsync(item);

        //delete the wishlist itself
        await _customWishlistService.RemoveCustomWishlistAsync(wishlist.Id);

        return Ok(await PrepareWishlistModelAsync());
    }

    /// <summary>
    /// Moves a product from the default wishlist to a custom wishlist (e.g. right after adding it to the wishlist)
    /// </summary>
    /// <param name="listId">Custom wishlist identifier</param>
    /// <param name="productId">Product identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the custom wishlist</returns>
    [HttpPost("lists/{listId:int}/products/{productId:int}")]
    [ProducesResponseType(typeof(WishlistModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> MoveProductToCustomWishlist(int listId, int productId)
    {
        if (await CheckCustomWishlistsAllowedAsync() is IActionResult problem)
            return problem;

        var wishlist = await GetCustomWishlistAsync(listId);
        if (wishlist == null)
            return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.NotFound"));

        await MoveProductToCustomWishlistAsync(productId, wishlist.Id);

        return Ok(await PrepareWishlistModelAsync(wishlist.Id));
    }

    #endregion

    #region Email a friend

    /// <summary>
    /// Gets the "Email a friend" form of a wishlist of the current customer
    /// </summary>
    /// <param name="listId">Custom wishlist identifier; empty for the default wishlist</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form</returns>
    [HttpGet("email")]
    [ProducesResponseType(typeof(WishlistEmailAFriendModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> EmailWishlist([FromQuery] int? listId = null)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        if (!_shoppingCartSettings.EmailWishlistEnabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist, listId);
        if (!cart.Any())
            return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.CartIsEmpty"));

        return Ok(await _shoppingCartModelFactory.PrepareWishlistEmailAFriendModelAsync(new WishlistEmailAFriendModel(), false, listId));
    }

    /// <summary>
    /// Sends a link to a wishlist of the current customer to a friend
    /// </summary>
    /// <param name="model">Friend's email, own email, personal message and the wishlist ("listId")</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the form with the result message</returns>
    [HttpPost("email")]
    [ProducesResponseType(typeof(WishlistEmailAFriendModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> EmailWishlistSend(WishlistEmailAFriendModel model)
    {
        if (await CheckWishlistEnabledAsync() is IActionResult problem)
            return problem;

        if (!_shoppingCartSettings.EmailWishlistEnabled)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.FeatureDisabled"));

        var cart = await _headlessApiShoppingCartService.GetCartAsync(ShoppingCartType.Wishlist, model.ListId);
        if (!cart.Any())
            return NotFoundProblem(await _localizationService.GetResourceAsync("Wishlist.CartIsEmpty"));

        //check whether the current customer is guest and is allowed to email wishlist
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsGuestAsync(customer) && !_shoppingCartSettings.AllowAnonymousUsersToEmailWishlist)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Wishlist.EmailAFriend.OnlyRegisteredUsers"));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await _workflowMessageService.SendWishlistEmailAFriendMessageAsync(customer,
            (await _workContext.GetWorkingLanguageAsync()).Id,
            model.YourEmailAddress,
            model.FriendEmail,
            _htmlFormatter.FormatText(model.PersonalMessage),
            await PrepareSharedWishlistUrlAsync(customer, model.ListId));

        model = await _shoppingCartModelFactory.PrepareWishlistEmailAFriendModelAsync(model, true, model.ListId);
        model.SuccessfullySent = true;
        model.Result = await _localizationService.GetResourceAsync("Wishlist.EmailAFriend.SuccessfullySent");

        return Ok(model);
    }

    #endregion
}

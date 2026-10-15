using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Plugin.Misc.HeadlessApi.Filters;
using Nop.Plugin.Misc.HeadlessApi.Models.Products;
using Nop.Plugin.Misc.HeadlessApi.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Web.Factories;
using Nop.Web.Models.Catalog;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the product review endpoints (the reviews of a product are available via GET products/{productId}/reviews)
/// </summary>
[HeadlessApiRoute("products")]
public class ProductReviewsApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly CatalogSettings _catalogSettings;
    protected readonly HeadlessApiCatalogService _headlessApiCatalogService;
    protected readonly ICustomerService _customerService;
    protected readonly ILocalizationService _localizationService;
    protected readonly IProductModelFactory _productModelFactory;
    protected readonly IProductReviewService _productReviewService;
    protected readonly IProductService _productService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;

    #endregion

    #region Ctor

    public ProductReviewsApiController(CatalogSettings catalogSettings,
        HeadlessApiCatalogService headlessApiCatalogService,
        ICustomerService customerService,
        ILocalizationService localizationService,
        IProductModelFactory productModelFactory,
        IProductReviewService productReviewService,
        IProductService productService,
        IStoreContext storeContext,
        IWorkContext workContext)
    {
        _catalogSettings = catalogSettings;
        _headlessApiCatalogService = headlessApiCatalogService;
        _customerService = customerService;
        _localizationService = localizationService;
        _productModelFactory = productModelFactory;
        _productReviewService = productReviewService;
        _productService = productService;
        _storeContext = storeContext;
        _workContext = workContext;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Adds a product review
    /// </summary>
    /// <remarks>
    /// Request body: { "addProductReview": { "title": "...", "reviewText": "...", "rating": 5 },
    /// "addAdditionalProductReviewList": [ { "reviewTypeId": 1, "rating": 4 } ] }.
    /// CAPTCHA is not supported by the API, so the CAPTCHA setting "Show on product reviews page" is ignored
    /// </remarks>
    /// <param name="productId">Product identifier</param>
    /// <param name="model">Product reviews model containing the review to add</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the result and the updated reviews</returns>
    [HttpPost("{productId:int}/reviews")]
    [ProducesResponseType(typeof(AddProductReviewResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> AddReview(int productId, ProductReviewsModel model)
    {
        var product = await _productService.GetProductByIdAsync(productId);
        if (!await _headlessApiCatalogService.IsProductAvailableAsync(product) || !product.Published || !product.AllowCustomerReviews)
            return NotFoundProblem();

        var store = await _storeContext.GetCurrentStoreAsync();
        if (!await _productReviewService.CanAddReviewAsync(product.Id, _catalogSettings.ShowProductReviewsPerStore ? store.Id : 0))
            return WarningsProblem([await _localizationService.GetResourceAsync("Reviews.AlreadyAddedProductReviews")]);

        foreach (var error in await _productReviewService.ValidateProductReviewAvailabilityAsync(product))
            ModelState.AddModelError(string.Empty, error);

        if (model?.AddProductReview == null)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Reviews.Fields.ReviewText.Required"));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        //save review
        var rating = model.AddProductReview.Rating;
        if (rating is < 1 or > 5)
            rating = _catalogSettings.DefaultProductRatingValue;

        var isApproved = !_catalogSettings.ProductReviewsMustBeApproved;
        var customer = await _workContext.GetCurrentCustomerAsync();

        var productReview = new ProductReview
        {
            ProductId = product.Id,
            CustomerId = customer.Id,
            Title = model.AddProductReview.Title,
            ReviewText = model.AddProductReview.ReviewText,
            Rating = rating,
            HelpfulYesTotal = 0,
            HelpfulNoTotal = 0,
            IsApproved = isApproved,
            CreatedOnUtc = DateTime.UtcNow,
            StoreId = store.Id
        };

        var productReviewReviewTypeMappings = (model.AddAdditionalProductReviewList ?? [])
            .Select(additionalReview => new ProductReviewReviewTypeMapping
            {
                ReviewTypeId = additionalReview.ReviewTypeId,
                Rating = additionalReview.Rating
            }).ToList();

        //the service updates the totals, notifies the store owner, logs the activity and raises the event
        await _productReviewService.InsertProductReviewAsync(productReview, productReviewReviewTypeMappings);

        return Ok(new AddProductReviewResultModel
        {
            ProductReviewId = productReview.Id,
            IsApproved = isApproved,
            Message = await _localizationService.GetResourceAsync(isApproved ? "Reviews.SuccessfullyAdded" : "Reviews.SeeAfterApproving"),
            ProductReviews = await _productModelFactory.PrepareProductReviewsModelAsync(product)
        });
    }

    /// <summary>
    /// Votes for the helpfulness of a product review
    /// </summary>
    /// <param name="reviewId">Product review identifier</param>
    /// <param name="request">Request</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the result and the new totals</returns>
    [HttpPost("reviews/{reviewId:int}/helpfulness")]
    [ProducesResponseType(typeof(ReviewHelpfulnessResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SetHelpfulness(int reviewId, ReviewHelpfulnessRequest request)
    {
        var productReview = await _productReviewService.GetProductReviewByIdAsync(reviewId);
        if (productReview == null)
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsGuestAsync(customer) && !_catalogSettings.AllowAnonymousUsersToReviewProduct)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Reviews.Helpfulness.OnlyRegistered"));

        //customers aren't allowed to vote for their own reviews
        if (productReview.CustomerId == customer.Id)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Reviews.Helpfulness.YourOwnReview"));

        await _productReviewService.SetProductReviewHelpfulnessAsync(productReview, request?.WasHelpful ?? false);

        //new totals
        await _productReviewService.UpdateProductReviewHelpfulnessTotalsAsync(productReview);

        return Ok(new ReviewHelpfulnessResultModel
        {
            ProductReviewId = productReview.Id,
            Result = await _localizationService.GetResourceAsync("Reviews.Helpfulness.SuccessfullyVoted"),
            TotalYes = productReview.HelpfulYesTotal,
            TotalNo = productReview.HelpfulNoTotal
        });
    }

    /// <summary>
    /// Gets the product reviews written by the current customer
    /// </summary>
    /// <param name="page">Page number (starting from 1)</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the product reviews</returns>
    [HttpGet("reviews/mine")]
    [RequireRegisteredCustomer]
    [ProducesResponseType(typeof(CustomerProductReviewsModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> CustomerReviews([FromQuery] int? page = null)
    {
        if (!_catalogSettings.ShowProductReviewsTabOnAccountPage)
            return NotFoundProblem();

        return Ok(await _productModelFactory.PrepareCustomerProductReviewsModelAsync(page));
    }

    #endregion
}

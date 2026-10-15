using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Localization;
using Nop.Core.Events;
using Nop.Plugin.Misc.HeadlessApi.Models.Blog;
using Nop.Services.Blogs;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Stores;
using Nop.Web.Factories;
using Nop.Web.Models.Blogs;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the blog endpoints
/// </summary>
/// <remarks>
/// CAPTCHA is not supported by the API, so it's not validated for blog comments even if enabled
/// </remarks>
[HeadlessApiRoute("blog")]
public class BlogApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly BlogSettings _blogSettings;
    protected readonly IBlogModelFactory _blogModelFactory;
    protected readonly IBlogService _blogService;
    protected readonly ICustomerActivityService _customerActivityService;
    protected readonly ICustomerService _customerService;
    protected readonly IEventPublisher _eventPublisher;
    protected readonly ILocalizationService _localizationService;
    protected readonly IStoreContext _storeContext;
    protected readonly IStoreMappingService _storeMappingService;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;
    protected readonly LocalizationSettings _localizationSettings;

    #endregion

    #region Ctor

    public BlogApiController(BlogSettings blogSettings,
        IBlogModelFactory blogModelFactory,
        IBlogService blogService,
        ICustomerActivityService customerActivityService,
        ICustomerService customerService,
        IEventPublisher eventPublisher,
        ILocalizationService localizationService,
        IStoreContext storeContext,
        IStoreMappingService storeMappingService,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService,
        LocalizationSettings localizationSettings)
    {
        _blogSettings = blogSettings;
        _blogModelFactory = blogModelFactory;
        _blogService = blogService;
        _customerActivityService = customerActivityService;
        _customerService = customerService;
        _eventPublisher = eventPublisher;
        _localizationService = localizationService;
        _storeContext = storeContext;
        _storeMappingService = storeMappingService;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
        _localizationSettings = localizationSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Check whether the blog is enabled
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the problem result or null if the blog is enabled</returns>
    protected virtual async Task<IActionResult> CheckBlogEnabledAsync()
    {
        if (_blogSettings.Enabled)
            return null;

        return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Blog.Disabled"));
    }

    /// <summary>
    /// Check whether the blog post is available for the current customer (availability dates, store mapping)
    /// </summary>
    /// <remarks>
    /// The admin preview of unavailable blog posts is not supported
    /// </remarks>
    /// <param name="blogPost">Blog post</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the value indicating whether the blog post is available</returns>
    protected virtual async Task<bool> IsBlogPostAvailableAsync(BlogPost blogPost)
    {
        return blogPost != null
            && _blogService.BlogPostIsAvailable(blogPost)
            && await _storeMappingService.AuthorizeAsync(blogPost);
    }

    /// <summary>
    /// Prepare the blog post model incl. comments
    /// </summary>
    /// <param name="blogPost">Blog post</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the blog post model</returns>
    protected virtual async Task<BlogPostModel> PrepareBlogPostModelAsync(BlogPost blogPost)
    {
        var model = new BlogPostModel();
        await _blogModelFactory.PrepareBlogPostModelAsync(model, blogPost, true);

        //CAPTCHA is not supported
        if (model.AddNewComment != null)
            model.AddNewComment.DisplayCaptcha = false;

        return model;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the blog posts of the working language
    /// </summary>
    /// <param name="command">Paging and filtering: PageNumber (starting with 1), PageSize (default "Posts page size" setting),
    /// Tag (posts with the tag) or Month (posts of the month, format "yyyy-M", e.g. "2026-10")</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the blog posts</returns>
    [HttpGet]
    [ProducesResponseType(typeof(BlogPostListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> List([FromQuery] BlogPagingFilteringModel command)
    {
        if (await CheckBlogEnabledAsync() is IActionResult problem)
            return problem;

        command ??= new BlogPagingFilteringModel();

        //the storefront parser throws an exception on invalid values, so validate the month here
        if (!string.IsNullOrEmpty(command.Month)
            && !DateTime.TryParseExact(command.Month, new[] { "yyyy-M", "yyyy-MM" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            ModelState.AddModelError(nameof(command.Month), await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Blog.WrongMonth"));
            return ValidationProblem(ModelState);
        }

        return Ok(await _blogModelFactory.PrepareBlogPostListModelAsync(command));
    }

    /// <summary>
    /// Gets a blog post incl. approved comments
    /// </summary>
    /// <param name="blogPostId">Blog post identifier</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the blog post</returns>
    [HttpGet("{blogPostId:int}")]
    [ProducesResponseType(typeof(BlogPostModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> Post(int blogPostId)
    {
        if (await CheckBlogEnabledAsync() is IActionResult problem)
            return problem;

        var blogPost = await _blogService.GetBlogPostByIdAsync(blogPostId);
        if (!await IsBlogPostAvailableAsync(blogPost))
            return NotFoundProblem();

        return Ok(await PrepareBlogPostModelAsync(blogPost));
    }

    /// <summary>
    /// Adds a comment to a blog post
    /// </summary>
    /// <param name="blogPostId">Blog post identifier</param>
    /// <param name="model">Blog post model; only AddNewComment.CommentText is used</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the result and the updated blog post</returns>
    [HttpPost("{blogPostId:int}/comments")]
    [ProducesResponseType(typeof(BlogCommentResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> AddComment(int blogPostId, BlogPostModel model)
    {
        if (await CheckBlogEnabledAsync() is IActionResult problem)
            return problem;

        var blogPost = await _blogService.GetBlogPostByIdAsync(blogPostId);
        if (!await IsBlogPostAvailableAsync(blogPost))
            return NotFoundProblem();

        if (!blogPost.AllowComments)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Plugins.Misc.HeadlessApi.Blog.CommentsNotAllowed"));

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (await _customerService.IsGuestAsync(customer) && !_blogSettings.AllowNotRegisteredUsersToLeaveComments)
            return ForbiddenProblem(await _localizationService.GetResourceAsync("Blog.Comments.OnlyRegisteredUsersLeaveComments"));

        //the validator checks the text only if the comment is passed
        if (string.IsNullOrWhiteSpace(model.AddNewComment?.CommentText) && ModelState.IsValid)
            ModelState.AddModelError(string.Empty, await _localizationService.GetResourceAsync("Blog.Comments.CommentText.Required"));

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var store = await _storeContext.GetCurrentStoreAsync();
        var comment = new BlogComment
        {
            BlogPostId = blogPost.Id,
            CustomerId = customer.Id,
            CommentText = model.AddNewComment.CommentText,
            IsApproved = !_blogSettings.BlogCommentsMustBeApproved,
            StoreId = store.Id,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _blogService.InsertBlogCommentAsync(comment);

        //notify a store owner
        if (_blogSettings.NotifyAboutNewBlogComments)
            await _workflowMessageService.SendBlogCommentStoreOwnerNotificationMessageAsync(comment, _localizationSettings.DefaultAdminLanguageId);

        //activity log
        await _customerActivityService.InsertActivityAsync("PublicStore.AddBlogComment",
            await _localizationService.GetResourceAsync("ActivityLog.PublicStore.AddBlogComment"), comment);

        //raise event
        if (comment.IsApproved)
            await _eventPublisher.PublishAsync(new BlogCommentApprovedEvent(comment));

        return Ok(new BlogCommentResultModel
        {
            IsApproved = comment.IsApproved,
            Result = comment.IsApproved
                ? await _localizationService.GetResourceAsync("Blog.Comments.SuccessfullyAdded")
                : await _localizationService.GetResourceAsync("Blog.Comments.SeeAfterApproving"),
            BlogPost = await PrepareBlogPostModelAsync(blogPost)
        });
    }

    /// <summary>
    /// Gets the most popular blog tags of the working language (like the BlogTags view component)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the tags</returns>
    [HttpGet("tags")]
    [ProducesResponseType(typeof(BlogPostTagListModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Tags()
    {
        if (await CheckBlogEnabledAsync() is IActionResult problem)
            return problem;

        return Ok(await _blogModelFactory.PrepareBlogPostTagListModelAsync());
    }

    /// <summary>
    /// Gets the years and months with blog posts of the working language (like the BlogMonths view component)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the years with months</returns>
    [HttpGet("months")]
    [ProducesResponseType(typeof(IList<BlogPostYearModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public virtual async Task<IActionResult> Months()
    {
        if (await CheckBlogEnabledAsync() is IActionResult problem)
            return problem;

        return Ok(await _blogModelFactory.PrepareBlogPostYearModelAsync());
    }

    #endregion
}

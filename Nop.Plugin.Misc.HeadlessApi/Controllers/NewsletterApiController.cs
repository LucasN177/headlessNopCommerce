using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.HeadlessApi.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Messages;
using Nop.Plugin.Misc.HeadlessApi.Models.Newsletter;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Factories;
using Nop.Web.Models.Newsletter;

namespace Nop.Plugin.Misc.HeadlessApi.Controllers;

/// <summary>
/// Represents the newsletter endpoints (subscribe/unsubscribe box and the confirmation of the email link)
/// </summary>
/// <remarks>
/// The newsletter subscriptions of registered customers are managed by PUT account/info. CAPTCHA is not supported by the API.
/// </remarks>
[HeadlessApiRoute("newsletter")]
public class NewsletterApiController : BaseHeadlessApiController
{
    #region Fields

    protected readonly ICustomerService _customerService;
    protected readonly ILanguageService _languageService;
    protected readonly ILocalizationService _localizationService;
    protected readonly INewsLetterModelFactory _newsletterModelFactory;
    protected readonly INewsLetterSubscriptionService _newsLetterSubscriptionService;
    protected readonly INewsLetterSubscriptionTypeService _newsLetterSubscriptionTypeService;
    protected readonly IStoreContext _storeContext;
    protected readonly IWorkContext _workContext;
    protected readonly IWorkflowMessageService _workflowMessageService;

    #endregion

    #region Ctor

    public NewsletterApiController(ICustomerService customerService,
        ILanguageService languageService,
        ILocalizationService localizationService,
        INewsLetterModelFactory newsletterModelFactory,
        INewsLetterSubscriptionService newsLetterSubscriptionService,
        INewsLetterSubscriptionTypeService newsLetterSubscriptionTypeService,
        IStoreContext storeContext,
        IWorkContext workContext,
        IWorkflowMessageService workflowMessageService)
    {
        _customerService = customerService;
        _languageService = languageService;
        _localizationService = localizationService;
        _newsletterModelFactory = newsletterModelFactory;
        _newsLetterSubscriptionService = newsLetterSubscriptionService;
        _newsLetterSubscriptionTypeService = newsLetterSubscriptionTypeService;
        _storeContext = storeContext;
        _workContext = workContext;
        _workflowMessageService = workflowMessageService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Subscribes an email to the newsletter (all subscription types ticked by default) or unsubscribes it;
    /// in both cases an email with a confirmation link is sent
    /// </summary>
    /// <param name="request">Email and whether to subscribe or unsubscribe</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the message to display</returns>
    /// <remarks>
    /// The email link has to point to a frontend page that calls POST newsletter/activation
    /// </remarks>
    [HttpPost("subscribe")]
    [ProducesResponseType(typeof(NewsletterSubscribeResultModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public virtual async Task<IActionResult> Subscribe(NewsletterSubscribeRequest request)
    {
        //validate email
        if (!CommonHelper.IsValidEmail(request.Email))
            return WarningsProblem([await _localizationService.GetResourceAsync("Newsletter.Email.Wrong")]);

        //get current subscriptions
        var email = request.Email.Trim();
        var store = await _storeContext.GetCurrentStoreAsync();
        var currentSubscriptions = await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByEmailAsync(email, storeId: store.Id);

        if (request.Subscribe)
        {
            //try to subscribe a customer for all 'ticked by default' subscriptions in the current store
            var subscriptionTypes = (await _newsLetterSubscriptionTypeService.GetAllNewsLetterSubscriptionTypesAsync(store.Id))
                .Where(type => type.TickedByDefault)
                .ToList();

            var language = await _workContext.GetWorkingLanguageAsync();
            var subscriptionGuid = currentSubscriptions.FirstOrDefault()?.NewsLetterSubscriptionGuid ?? Guid.NewGuid();
            NewsLetterSubscription firstNewSubscription = null;

            foreach (var type in subscriptionTypes)
            {
                var existingSubscription = currentSubscriptions.FirstOrDefault(subscription => subscription.TypeId == type.Id);
                if (existingSubscription is not null)
                {
                    //update language if not set yet
                    if (existingSubscription.LanguageId == 0)
                    {
                        existingSubscription.LanguageId = language.Id;
                        await _newsLetterSubscriptionService.UpdateNewsLetterSubscriptionAsync(existingSubscription);
                    }

                    continue;
                }

                var newSubscription = new NewsLetterSubscription
                {
                    NewsLetterSubscriptionGuid = subscriptionGuid,
                    Email = email,
                    Active = false,
                    TypeId = type.Id,
                    StoreId = store.Id,
                    LanguageId = language.Id,
                    CreatedOnUtc = DateTime.UtcNow
                };
                await _newsLetterSubscriptionService.InsertNewsLetterSubscriptionAsync(newSubscription);
                firstNewSubscription ??= newSubscription;
            }

            //send an activation message
            if (firstNewSubscription is not null)
                await _workflowMessageService.SendNewsLetterSubscriptionActivationMessageAsync(firstNewSubscription);
        }
        else
        {
            //send a deactivation message
            if (currentSubscriptions.FirstOrDefault(item => item.Active) is NewsLetterSubscription subscription)
                await _workflowMessageService.SendNewsLetterSubscriptionDeactivationMessageAsync(subscription);
        }

        return Ok(new NewsletterSubscribeResultModel
        {
            Message = request.Subscribe
                ? await _localizationService.GetResourceAsync("Newsletter.SubscribeEmailSent")
                : await _localizationService.GetResourceAsync("Newsletter.UnsubscribeEmailSent")
        });
    }

    /// <summary>
    /// Activates or deletes the newsletter subscriptions of the email link
    /// </summary>
    /// <param name="request">Token and whether to activate or delete the subscriptions</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the result message</returns>
    [HttpPost("activation")]
    [ProducesResponseType(typeof(SubscriptionActivationModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public virtual async Task<IActionResult> SubscriptionActivation(NewsletterActivationRequest request)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        var subscriptions = (await _newsLetterSubscriptionService.GetNewsLetterSubscriptionsByGuidAsync(request.Token))
            .Where(subscription => subscription.StoreId == store.Id)
            .ToList();
        if (!subscriptions.Any())
            return NotFoundProblem();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isRegistered = await _customerService.IsRegisteredAsync(customer);

        foreach (var subscription in subscriptions)
        {
            if (request.Active)
            {
                //activate subscription if inactive
                if (!subscription.Active)
                {
                    subscription.Active = true;
                    await _newsLetterSubscriptionService.UpdateNewsLetterSubscriptionAsync(subscription);
                }

                //set language by subscription language (only for guests, as registered customers have the language set)
                if (!isRegistered && await _languageService.GetLanguageByIdAsync(subscription.LanguageId) is { } language)
                    await _workContext.SetWorkingLanguageAsync(language);
            }
            else
            {
                //delete inactive subscription
                await _newsLetterSubscriptionService.DeleteNewsLetterSubscriptionAsync(subscription);
            }
        }

        return Ok(await _newsletterModelFactory.PrepareSubscriptionActivationModelAsync(request.Active));
    }

    #endregion
}

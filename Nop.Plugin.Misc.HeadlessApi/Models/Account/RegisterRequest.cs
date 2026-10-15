using Nop.Web.Models.Customer;

namespace Nop.Plugin.Misc.HeadlessApi.Models.Account;

/// <summary>
/// Represents a registration request; it's the registration form of the storefront extended by the custom form fields
/// </summary>
public record RegisterRequest : RegisterModel
{
    /// <summary>
    /// Gets or sets the custom customer attributes and GDPR consents, using the same keys as the storefront form, e.g.
    /// "customer_attribute_1": "5" (value id), "customer_attribute_2": "6,7" (checkboxes), "customer_attribute_3": "text",
    /// "consent2": "on" (accepted GDPR consent; "true" is accepted as well)
    /// </summary>
    /// <remarks>
    /// GDPR consents can also be accepted by setting "accepted" of the items in "gdprConsents"
    /// </remarks>
    public Dictionary<string, string> Attributes { get; set; } = new();
}

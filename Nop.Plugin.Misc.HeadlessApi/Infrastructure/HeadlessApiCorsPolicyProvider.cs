using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Nop.Plugin.Misc.HeadlessApi.Infrastructure;

/// <summary>
/// Represents a CORS policy provider that builds the API policy from the plugin settings,
/// so that changes of the allowed origins take effect without restarting the application
/// </summary>
public class HeadlessApiCorsPolicyProvider : ICorsPolicyProvider
{
    #region Fields

    protected readonly HeadlessApiSettings _headlessApiSettings;

    #endregion

    #region Ctor

    public HeadlessApiCorsPolicyProvider(HeadlessApiSettings headlessApiSettings)
    {
        _headlessApiSettings = headlessApiSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a CORS policy with the given name
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <param name="policyName">Policy name</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the policy or null if no policy is applied</returns>
    public virtual Task<CorsPolicy> GetPolicyAsync(HttpContext context, string policyName)
    {
        if (!string.Equals(policyName, HeadlessApiDefaults.CorsPolicyName, StringComparison.Ordinal) || !_headlessApiSettings.Enabled)
            return Task.FromResult<CorsPolicy>(null);

        var origins = (_headlessApiSettings.AllowedOrigins ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .ToArray();

        if (!origins.Any())
            return Task.FromResult<CorsPolicy>(null);

        var policy = new CorsPolicyBuilder()
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .Build();

        return Task.FromResult(policy);
    }

    #endregion
}

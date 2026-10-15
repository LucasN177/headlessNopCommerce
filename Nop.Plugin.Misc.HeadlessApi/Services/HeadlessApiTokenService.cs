using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.HeadlessApi.Models.Auth;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;

namespace Nop.Plugin.Misc.HeadlessApi.Services;

/// <summary>
/// Represents the service to issue and validate API tokens (JWT)
/// </summary>
/// <remarks>
/// Tokens are issued for guests as well as for registered customers, so that the frontend
/// always works with a stable customer (shopping cart, wishlist, working language and currency).
/// Each token contains the customer security stamp; changing the stamp revokes all tokens of the customer.
/// </remarks>
public class HeadlessApiTokenService
{
    #region Fields

    protected readonly CustomerSettings _customerSettings;
    protected readonly HeadlessApiSettings _headlessApiSettings;
    protected readonly ICustomerService _customerService;
    protected readonly IGenericAttributeService _genericAttributeService;
    protected readonly ISettingService _settingService;

    #endregion

    #region Ctor

    public HeadlessApiTokenService(CustomerSettings customerSettings,
        HeadlessApiSettings headlessApiSettings,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ISettingService settingService)
    {
        _customerSettings = customerSettings;
        _headlessApiSettings = headlessApiSettings;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _settingService = settingService;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Gets the key to sign and validate tokens, generates a new one if not exists yet
    /// </summary>
    /// <returns>A task that represents the asynchronous operation; the task result contains the security key</returns>
    protected virtual async Task<SecurityKey> GetSigningKeyAsync()
    {
        if (string.IsNullOrEmpty(_headlessApiSettings.TokenSigningKey))
        {
            _headlessApiSettings.TokenSigningKey = GenerateSigningKey();
            await _settingService.SaveSettingAsync(_headlessApiSettings, settings => settings.TokenSigningKey);
        }

        return new SymmetricSecurityKey(Convert.FromBase64String(_headlessApiSettings.TokenSigningKey));
    }

    /// <summary>
    /// Gets the security stamp of the customer, generates a new one if not exists yet
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the security stamp</returns>
    protected virtual async Task<string> GetSecurityStampAsync(Customer customer)
    {
        var securityStamp = await _genericAttributeService.GetAttributeAsync<string>(customer, HeadlessApiDefaults.SecurityStampAttribute);
        if (!string.IsNullOrEmpty(securityStamp))
            return securityStamp;

        securityStamp = Guid.NewGuid().ToString("N");
        await _genericAttributeService.SaveAttributeAsync(customer, HeadlessApiDefaults.SecurityStampAttribute, securityStamp);

        return securityStamp;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Generates a new random key to sign tokens
    /// </summary>
    /// <returns>Key as Base64 string</returns>
    public static string GenerateSigningKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    /// <summary>
    /// Issues a token for the passed customer
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the token and its expiration date (UTC)</returns>
    public virtual async Task<(string Token, DateTime ExpiresOnUtc)> GenerateTokenAsync(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var now = DateTime.UtcNow;
        var expiresOnUtc = now.AddDays(Math.Max(_headlessApiSettings.TokenLifetimeDays, 1));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = HeadlessApiDefaults.TokenIssuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresOnUtc,
            SigningCredentials = new SigningCredentials(await GetSigningKeyAsync(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = customer.CustomerGuid.ToString(),
                [HeadlessApiDefaults.SecurityStampClaim] = await GetSecurityStampAsync(customer)
            }
        };

        return (new JsonWebTokenHandler().CreateToken(tokenDescriptor), expiresOnUtc);
    }

    /// <summary>
    /// Issues a token for the passed customer and prepares the token model returned to the client
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the token model</returns>
    public virtual async Task<TokenModel> PrepareTokenModelAsync(Customer customer)
    {
        var (token, expiresOnUtc) = await GenerateTokenAsync(customer);

        return new TokenModel
        {
            AccessToken = token,
            ExpiresOnUtc = expiresOnUtc,
            CustomerGuid = customer.CustomerGuid,
            IsRegistered = await _customerService.IsRegisteredAsync(customer)
        };
    }

    /// <summary>
    /// Validates the passed token and gets the customer it was issued for
    /// </summary>
    /// <param name="token">Token</param>
    /// <returns>A task that represents the asynchronous operation; the task result contains the customer or null if the token is not valid</returns>
    public virtual async Task<Customer> GetCustomerByTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return null;

        var validationResult = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = HeadlessApiDefaults.TokenIssuer,
            ValidateAudience = false,
            IssuerSigningKey = await GetSigningKeyAsync(),
            ClockSkew = TimeSpan.FromMinutes(1)
        });
        if (!validationResult.IsValid || validationResult.SecurityToken is not JsonWebToken jsonWebToken)
            return null;

        if (!Guid.TryParse(jsonWebToken.Subject, out var customerGuid))
            return null;

        var customer = await _customerService.GetCustomerByGuidAsync(customerGuid);
        if (customer == null || customer.Deleted || !customer.Active || customer.RequireReLogin)
            return null;

        //whether the token has been revoked
        if (!jsonWebToken.TryGetPayloadValue<string>(HeadlessApiDefaults.SecurityStampClaim, out var securityStamp)
            || !string.Equals(securityStamp, await GetSecurityStampAsync(customer), StringComparison.Ordinal))
        {
            return null;
        }

        //require a customer to re-login after password changing (same behavior as the cookie authentication)
        if (_customerSettings.RequiredReLoginAfterPasswordChange && await _customerService.IsRegisteredAsync(customer))
        {
            var customerPassword = await _customerService.GetCurrentPasswordAsync(customer.Id);
            if (customerPassword != null && customerPassword.CreatedOnUtc > jsonWebToken.IssuedAt.AddSeconds(1))
                return null;
        }

        return customer;
    }

    /// <summary>
    /// Revokes all tokens issued for the passed customer
    /// </summary>
    /// <param name="customer">Customer</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task RevokeTokensAsync(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        await _genericAttributeService.SaveAttributeAsync(customer, HeadlessApiDefaults.SecurityStampAttribute, Guid.NewGuid().ToString("N"));
    }

    #endregion
}

using Azure.Core;

namespace TeamsMeetingBot.Interfaces;

/// <summary>
/// Service for managing authentication and credentials for Microsoft Graph API
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Gets a token credential for authenticating with Microsoft Graph API
    /// </summary>
    /// <returns>Token credential configured with client credentials flow</returns>
    TokenCredential GetTokenCredential();
    
    /// <summary>
    /// Gets the configured client ID
    /// </summary>
    string GetClientId();
    
    /// <summary>
    /// Gets the configured tenant ID
    /// </summary>
    string GetTenantId();
}

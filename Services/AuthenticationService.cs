using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using TeamsMeetingBot.Interfaces;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Service for managing authentication with Microsoft Entra ID (Azure AD)
/// Implements client credentials flow for bot authentication
/// Supports Azure Key Vault for secure credential storage
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly TokenCredential _tokenCredential;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public AuthenticationService(
        IConfiguration configuration,
        ILogger<AuthenticationService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Check if Azure Key Vault is configured
        var keyVaultUri = _configuration["AzureKeyVault:VaultUri"];
        var useKeyVault = !string.IsNullOrEmpty(keyVaultUri);

        if (useKeyVault && keyVaultUri != null)
        {
            _logger.LogInformation("Using Azure Key Vault for credential storage at {KeyVaultUri}", keyVaultUri);
            
            // Use DefaultAzureCredential for Key Vault access
            // This supports multiple authentication methods in order:
            // 1. Environment variables
            // 2. Managed Identity
            // 3. Visual Studio
            // 4. Azure CLI
            // 5. Azure PowerShell
            var keyVaultCredential = new DefaultAzureCredential();
            var secretClient = new SecretClient(new Uri(keyVaultUri), keyVaultCredential);

            // Retrieve secrets from Key Vault
            _tenantId = GetSecretFromKeyVault(secretClient, "TenantId");
            _clientId = GetSecretFromKeyVault(secretClient, "ClientId");
            _clientSecret = GetSecretFromKeyVault(secretClient, "ClientSecret");
        }
        else
        {
            _logger.LogWarning("Azure Key Vault not configured. Using configuration values directly. " +
                             "This is not recommended for production environments.");
            
            // Fall back to configuration values
            _tenantId = _configuration["MicrosoftEntraId:TenantId"] 
                ?? throw new InvalidOperationException("MicrosoftEntraId:TenantId is not configured");
            _clientId = _configuration["MicrosoftEntraId:ClientId"] 
                ?? throw new InvalidOperationException("MicrosoftEntraId:ClientId is not configured");
            _clientSecret = _configuration["MicrosoftEntraId:ClientSecret"] 
                ?? throw new InvalidOperationException("MicrosoftEntraId:ClientSecret is not configured");
        }

        // Create token credential using client credentials flow
        _tokenCredential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
        
        _logger.LogInformation(
            "Authentication service initialized for tenant {TenantId} with client {ClientId}",
            _tenantId,
            _clientId);
    }

    public TokenCredential GetTokenCredential()
    {
        return _tokenCredential;
    }

    public string GetClientId()
    {
        return _clientId;
    }

    public string GetTenantId()
    {
        return _tenantId;
    }

    private string GetSecretFromKeyVault(SecretClient secretClient, string secretName)
    {
        try
        {
            var secret = secretClient.GetSecret(secretName);
            _logger.LogInformation("Successfully retrieved secret {SecretName} from Key Vault", secretName);
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret {SecretName} from Key Vault", secretName);
            throw new InvalidOperationException(
                $"Failed to retrieve required secret '{secretName}' from Azure Key Vault. " +
                $"Ensure the secret exists and the application has appropriate permissions.", ex);
        }
    }
}

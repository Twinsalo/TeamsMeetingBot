# Authentication Setup Guide

This document provides instructions for configuring Microsoft Entra ID (Azure AD) authentication for the Teams Meeting Bot.

## Prerequisites

- Azure subscription with appropriate permissions to register applications
- Access to Azure Portal (https://portal.azure.com)
- Teams administrator access for granting consent

## Step 1: Register Bot Application in Azure AD

1. Navigate to the [Azure Portal](https://portal.azure.com)
2. Go to **Azure Active Directory** > **App registrations**
3. Click **New registration**
4. Configure the application:
   - **Name**: `Teams Meeting Bot`
   - **Supported account types**: Select based on your requirements
     - Single tenant: Accounts in this organizational directory only
     - Multi-tenant: Accounts in any organizational directory
   - **Redirect URI**: Leave blank for now (not needed for client credentials flow)
5. Click **Register**
6. Note the following values from the **Overview** page:
   - **Application (client) ID**: This is your `ClientId`
   - **Directory (tenant) ID**: This is your `TenantId`

## Step 2: Create Client Secret

1. In your app registration, go to **Certificates & secrets**
2. Click **New client secret**
3. Configure the secret:
   - **Description**: `Teams Meeting Bot Secret`
   - **Expires**: Choose appropriate expiration (recommended: 12-24 months)
4. Click **Add**
5. **IMPORTANT**: Copy the secret **Value** immediately - this is your `ClientSecret`
   - This value will not be shown again
   - Store it securely

## Step 3: Configure API Permissions

The bot requires the following Microsoft Graph API permissions:

### Required Application Permissions

1. In your app registration, go to **API permissions**
2. Click **Add a permission** > **Microsoft Graph** > **Application permissions**
3. Add the following permissions:

   | Permission | Purpose |
   |------------|---------|
   | `OnlineMeetings.Read.All` | Subscribe to meeting transcription streams and access meeting details |
   | `Chat.ReadWrite.All` | Post summaries to meeting chat and send private messages to participants |
   | `User.Read.All` | Read user profile information for participant details |
   | `Calls.AccessMedia.All` | Access call media streams (for transcription) |
   | `Calls.JoinGroupCallsasGuest.All` | Join meetings as a bot participant |

4. Click **Add permissions** for each

### Grant Admin Consent

**CRITICAL**: Application permissions require admin consent.

1. After adding all permissions, click **Grant admin consent for [Your Organization]**
2. Confirm the consent dialog
3. Verify all permissions show a green checkmark under **Status**

## Step 4: Configure Azure Key Vault (Recommended for Production)

For production deployments, store credentials in Azure Key Vault instead of configuration files.

### Create Key Vault

1. In Azure Portal, create a new **Key Vault** resource
2. Note the **Vault URI** (e.g., `https://your-keyvault.vault.azure.net/`) 

### Add Secrets

Add the following secrets to your Key Vault:

| Secret Name | Value |
|-------------|-------|
| `TenantId` | Your Azure AD Tenant ID |
| `ClientId` | Your Application (client) ID |
| `ClientSecret` | Your client secret value |

### Grant Access to Application

1. In Key Vault, go to **Access policies**
2. Click **Add Access Policy**
3. Configure:
   - **Secret permissions**: Get, List
   - **Select principal**: Search for your app registration name
4. Click **Add** then **Save**

### For Managed Identity (Recommended)

If deploying to Azure App Service:

1. Enable **System-assigned managed identity** on your App Service
2. In Key Vault **Access policies**, grant the managed identity:
   - **Secret permissions**: Get, List
3. No credentials needed in configuration - DefaultAzureCredential will use managed identity

## Step 5: Update Application Configuration

### Option A: Using Configuration Files (Development)

Update `appsettings.Development.json`:

```json
{
  "MicrosoftEntraId": {
    "TenantId": "your-tenant-id-here",
    "ClientId": "your-client-id-here",
    "ClientSecret": "your-client-secret-here"
  },
  "AzureKeyVault": {
    "VaultUri": ""
  }
}
```

### Option B: Using Azure Key Vault (Production)

Update `appsettings.json`:

```json
{
  "MicrosoftEntraId": {
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": ""
  },
  "AzureKeyVault": {
    "VaultUri": "https://your-keyvault.vault.azure.net/"
  }
}
```

The application will automatically retrieve credentials from Key Vault when `VaultUri` is configured.

### Option C: Using Environment Variables

Set the following environment variables:

```bash
MicrosoftEntraId__TenantId=your-tenant-id
MicrosoftEntraId__ClientId=your-client-id
MicrosoftEntraId__ClientSecret=your-client-secret
```

Or for Key Vault:

```bash
AzureKeyVault__VaultUri=https://your-keyvault.vault.azure.net/
```

## Step 6: Register Bot in Teams

1. Go to [Teams Developer Portal](https://dev.teams.microsoft.com/)
2. Create a new app or select existing app
3. Configure **App features** > **Bot**
4. Use the same **App ID** (Client ID) from Azure AD registration
5. Configure bot endpoint: `https://your-app-url.azurewebsites.net/api/messages`
6. Enable required scopes:
   - Personal
   - Team
   - GroupChat
7. Save and publish the app

## Security Best Practices

### Credential Storage

- ✅ **DO**: Use Azure Key Vault for production
- ✅ **DO**: Use Managed Identity when possible
- ✅ **DO**: Rotate client secrets regularly (before expiration)
- ❌ **DON'T**: Store secrets in source control
- ❌ **DON'T**: Use the same credentials across environments

### Access Control

- Limit API permissions to only what's required
- Use separate app registrations for dev/test/prod
- Regularly audit app permissions and usage
- Monitor authentication logs in Azure AD

### Secret Rotation

1. Create a new client secret before the old one expires
2. Update Key Vault with new secret
3. Restart application to pick up new secret
4. Verify application is working
5. Delete old secret

## Troubleshooting

### Authentication Errors

**Error**: `AADSTS700016: Application not found in directory`
- Verify the Tenant ID is correct
- Ensure the app is registered in the correct Azure AD tenant

**Error**: `AADSTS7000215: Invalid client secret`
- Verify the Client Secret is correct and not expired
- Check for extra spaces or characters when copying the secret

**Error**: `Insufficient privileges to complete the operation`
- Verify admin consent has been granted for all required permissions
- Check that permissions are **Application** type, not **Delegated**

### Key Vault Access Issues

**Error**: `Failed to retrieve secret from Key Vault`
- Verify the Key Vault URI is correct
- Ensure the application has appropriate access policies
- For Managed Identity, verify it's enabled and has permissions

**Error**: `DefaultAzureCredential failed to retrieve token`
- Check that at least one authentication method is available
- For local development, ensure Azure CLI is installed and logged in
- For Azure hosting, verify Managed Identity is enabled

## Verification

To verify authentication is working:

1. Start the application
2. Check logs for: `Authentication service initialized for tenant {TenantId}`
3. Check logs for: `Graph API service initialized for tenant {TenantId}`
4. Test a Graph API call (e.g., get meeting details)
5. Verify no authentication errors in Application Insights

## Additional Resources

- [Microsoft Graph API Permissions Reference](https://learn.microsoft.com/en-us/graph/permissions-reference)
- [Azure AD App Registration Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
- [Azure Key Vault Documentation](https://learn.microsoft.com/en-us/azure/key-vault/)
- [Teams Bot Authentication](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/authentication/auth-aad-sso-bots)

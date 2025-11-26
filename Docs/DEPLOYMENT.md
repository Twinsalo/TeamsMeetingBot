# Teams Meeting Bot - Deployment Guide

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Azure Resource Requirements](#azure-resource-requirements)
3. [Bot Registration in Teams Developer Portal](#bot-registration-in-teams-developer-portal)
4. [API Permissions and Admin Consent](#api-permissions-and-admin-consent)
5. [Azure Resource Deployment](#azure-resource-deployment)
6. [Application Configuration](#application-configuration)
7. [Deployment Steps](#deployment-steps)
8. [Post-Deployment Verification](#post-deployment-verification)
9. [Troubleshooting](#troubleshooting)
10. [Deployment Checklist](#deployment-checklist)

---

## Prerequisites

Before deploying the Teams Meeting Bot, ensure you have:

- **Azure Subscription** with appropriate permissions to create resources
- **Microsoft 365 Tenant** with Teams enabled
- **Global Administrator** or **Application Administrator** role for API permission consent
- **Azure CLI** installed (version 2.50.0 or later)
- **.NET 10.0 SDK** installed for local builds
- **Visual Studio 2022** or **VS Code** with C# extension
- **Teams Developer Portal** access (https://dev.teams.microsoft.com)
- **PowerShell 7.0+** or **Bash** for deployment scripts

### Required Azure Permissions

- Contributor role on the target resource group
- Permission to create service principals
- Permission to assign roles in Azure AD

---

## Azure Resource Requirements

### Overview

The Teams Meeting Bot requires the following Azure resources:

| Resource | SKU/Tier | Purpose | Estimated Monthly Cost |
|----------|----------|---------|------------------------|
| App Service Plan | P1v2 (Premium) | Host the bot application | ~$75 USD |
| App Service | - | Web application hosting | Included in plan |
| Azure Cosmos DB | Serverless or Provisioned (400 RU/s) | Store meeting summaries | ~$25-50 USD |
| Azure OpenAI | Standard | Generate AI summaries | Pay-per-use (~$50-200 USD) |
| Azure Key Vault | Standard | Secure credential storage | ~$0.03 USD |
| Application Insights | Pay-as-you-go | Monitoring and telemetry | ~$5-20 USD |

**Total Estimated Cost**: $155-345 USD/month (varies based on usage)


### Detailed Resource Specifications

#### 1. App Service Plan

- **Name**: `asp-teams-meeting-bot-{environment}`
- **SKU**: P1v2 (1 Core, 3.5 GB RAM)
- **Operating System**: Windows or Linux
- **Region**: Choose based on data residency requirements
- **Auto-scaling**: Optional (configure based on load)

**Why P1v2?**
- Supports Always On (required for bot responsiveness)
- Sufficient resources for concurrent meeting processing
- Production-grade SLA (99.95%)

#### 2. App Service

- **Name**: `app-teams-meeting-bot-{environment}`
- **Runtime**: .NET 10.0
- **Always On**: Enabled (critical for bot availability)
- **HTTPS Only**: Enabled
- **Managed Identity**: System-assigned (for Key Vault access)

#### 3. Azure Cosmos DB

- **Account Name**: `cosmos-teams-bot-{environment}`
- **API**: Core (SQL)
- **Consistency Level**: Session (default)
- **Database Name**: `MeetingSummaries`
- **Container Name**: `summaries`
- **Partition Key**: `/meetingId`
- **Throughput**: 
  - Development: Serverless
  - Production: Autoscale (400-4000 RU/s)
- **Backup**: Continuous (7-day retention)
- **Geo-Replication**: Optional (for high availability)

**Indexing Policy**:
```json
{
  "indexingMode": "consistent",
  "automatic": true,
  "includedPaths": [
    {
      "path": "/*"
    }
  ],
  "excludedPaths": [
    {
      "path": "/\"_etag\"/?"
    }
  ]
}
```

**TTL Configuration**: Enabled at container level (default: 2592000 seconds = 30 days)


#### 4. Azure OpenAI

- **Account Name**: `openai-teams-bot-{environment}`
- **Region**: East US, West Europe, or other supported regions
- **Deployment Name**: `gpt-4-turbo`
- **Model**: `gpt-4` (version 1106-preview or later)
- **Capacity**: 
  - Development: 10K TPM (Tokens Per Minute)
  - Production: 50K-100K TPM
- **Content Filtering**: Enabled (default settings)

**Rate Limits**:
- Requests per minute: 60 (development), 300+ (production)
- Tokens per minute: Based on capacity allocation

#### 5. Azure Key Vault

- **Name**: `kv-teams-bot-{environment}` (must be globally unique)
- **SKU**: Standard
- **Access Policy**: 
  - App Service Managed Identity: Get, List secrets
  - Deployment Service Principal: All secret permissions
- **Soft Delete**: Enabled (90-day retention)
- **Purge Protection**: Enabled (production only)

**Required Secrets**:
- `BotAppId`: Microsoft App ID from bot registration
- `BotAppPassword`: Microsoft App Password from bot registration
- `CosmosConnectionString`: Cosmos DB connection string
- `OpenAIApiKey`: Azure OpenAI API key

#### 6. Application Insights

- **Name**: `appi-teams-bot-{environment}`
- **Application Type**: ASP.NET
- **Workspace-based**: Yes (linked to Log Analytics Workspace)
- **Sampling**: Adaptive (default)
- **Daily Cap**: 5 GB (development), 10+ GB (production)

---

## Bot Registration in Teams Developer Portal

### Step 1: Create Bot Registration in Azure AD

1. Navigate to **Azure Portal** > **Azure Active Directory** > **App registrations**
2. Click **New registration**
3. Configure the registration:
   - **Name**: `Teams Meeting Bot - {Environment}`
   - **Supported account types**: `Accounts in any organizational directory (Any Azure AD directory - Multitenant)`
   - **Redirect URI**: Leave blank for now
4. Click **Register**
5. Note the **Application (client) ID** - this is your `MicrosoftAppId`


### Step 2: Create Client Secret

1. In the app registration, go to **Certificates & secrets**
2. Click **New client secret**
3. Configure:
   - **Description**: `Teams Bot Secret - {Environment}`
   - **Expires**: 24 months (recommended for production)
4. Click **Add**
5. **IMPORTANT**: Copy the secret **Value** immediately - this is your `MicrosoftAppPassword`
6. Store the secret securely in Azure Key Vault

### Step 3: Register Bot in Teams Developer Portal

1. Navigate to **Teams Developer Portal**: https://dev.teams.microsoft.com
2. Sign in with your Microsoft 365 account
3. Go to **Apps** > **New app**
4. Fill in basic information:
   - **App name**: `Meeting Summary Bot`
   - **Short description**: `Generates AI-powered summaries of Teams meetings`
   - **Long description**: `This bot captures live meeting transcriptions and generates periodic summaries with key topics, decisions, and action items. Late joiners receive automatic catch-up messages.`
   - **Version**: `1.0.0`
   - **Developer information**: Your organization details
   - **App URLs**: 
     - Privacy statement: Your privacy policy URL
     - Terms of use: Your terms URL
5. Click **Save**

### Step 4: Configure Bot in Teams App

1. In the Teams app, go to **App features** > **Bot**
2. Click **Identify your bot** > **Enter a bot ID**
3. Enter the **Application (client) ID** from Step 1
4. Configure bot settings:
   - **Scope**: 
     - ✅ Personal
     - ✅ Team
     - ✅ Group Chat
   - **What can your bot do?**:
     - ✅ Upload and download files
     - ✅ Send and receive messages
5. Add bot commands (optional):
   - Command: `summary`
     - Description: `Get meeting summaries`
     - Scope: Personal, Team, Group Chat
   - Command: `config`
     - Description: `Configure summary settings`
     - Scope: Personal, Team


### Step 5: Configure Messaging Endpoint

1. In Teams Developer Portal, go to **Configure** > **Bot endpoint address**
2. Enter your bot's messaging endpoint:
   - Format: `https://{your-app-service-name}.azurewebsites.net/api/messages`
   - Example: `https://app-teams-meeting-bot-prod.azurewebsites.net/api/messages`
3. Click **Save**

**Note**: You'll need to deploy the App Service first to get the URL, or update this after deployment.

### Step 6: Publish the App

1. Go to **Publish** > **Publish to org**
2. Click **Publish your app**
3. Wait for admin approval (if required)
4. Once approved, the bot will be available in your organization's Teams app store

---

## API Permissions and Admin Consent

### Required Microsoft Graph API Permissions

The bot requires the following **Application permissions** (not delegated):

| Permission | Type | Purpose | Admin Consent Required |
|------------|------|---------|------------------------|
| `OnlineMeetings.Read.All` | Application | Subscribe to meeting events and transcription | Yes |
| `OnlineMeetings.ReadWrite.All` | Application | Access meeting details and participants | Yes |
| `Calls.AccessMedia.All` | Application | Access meeting transcription stream | Yes |
| `Calls.JoinGroupCall.All` | Application | Join meetings as a bot | Yes |
| `Chat.ReadWrite.All` | Application | Post summaries to meeting chat | Yes |
| `User.Read.All` | Application | Read user profiles for participant info | Yes |
| `ChannelMessage.Send` | Application | Send messages to channels | Yes |

### Step-by-Step Permission Configuration

#### Step 1: Add API Permissions

1. Go to **Azure Portal** > **Azure Active Directory** > **App registrations**
2. Select your bot's app registration
3. Go to **API permissions** > **Add a permission**
4. Select **Microsoft Graph** > **Application permissions**
5. Search and add each permission from the table above
6. Click **Add permissions**


#### Step 2: Grant Admin Consent

1. After adding all permissions, click **Grant admin consent for {Your Organization}**
2. Confirm the consent dialog
3. Verify all permissions show "Granted for {Your Organization}" with a green checkmark

**Important**: Admin consent is required for all application permissions. Without it, the bot will not be able to access meeting data.

#### Step 3: Verify Permissions

Check that all permissions are granted:

```
✅ OnlineMeetings.Read.All - Granted
✅ OnlineMeetings.ReadWrite.All - Granted
✅ Calls.AccessMedia.All - Granted
✅ Calls.JoinGroupCall.All - Granted
✅ Chat.ReadWrite.All - Granted
✅ User.Read.All - Granted
✅ ChannelMessage.Send - Granted
```

### Additional Permissions for Webhook Method

If using the webhook transcription method, add:

| Permission | Type | Purpose |
|------------|------|---------|
| `OnlineMeetingTranscript.Read.All` | Application | Read meeting transcripts via webhooks |

---

## Azure Resource Deployment

### Option 1: Azure Portal (Manual Deployment)

#### Step 1: Create Resource Group

1. Navigate to **Azure Portal** > **Resource groups**
2. Click **Create**
3. Configure:
   - **Subscription**: Your subscription
   - **Resource group**: `rg-teams-meeting-bot-{environment}`
   - **Region**: Your preferred region
4. Click **Review + create** > **Create**

#### Step 2: Create Cosmos DB Account

1. Navigate to **Azure Cosmos DB** > **Create**
2. Select **Azure Cosmos DB for NoSQL**
3. Configure:
   - **Resource group**: Select your resource group
   - **Account name**: `cosmos-teams-bot-{environment}`
   - **Location**: Same as resource group
   - **Capacity mode**: Serverless (dev) or Provisioned throughput (prod)
4. Click **Review + create** > **Create**
5. Wait for deployment (5-10 minutes)

#### Step 3: Create Cosmos DB Database and Container

1. Go to your Cosmos DB account
2. Click **Data Explorer** > **New Database**
3. Create database:
   - **Database id**: `MeetingSummaries`
   - **Provision throughput**: Unchecked (if using serverless)
4. Click **OK**
5. Click **New Container**
6. Configure container:
   - **Database id**: Use existing `MeetingSummaries`
   - **Container id**: `summaries`
   - **Partition key**: `/meetingId`
   - **Throughput**: 400 RU/s (if provisioned) or leave default (if serverless)
7. Click **OK**
8. Repeat steps 5-7 to create `configurations` container with partition key `/meetingId`

#### Step 4: Create Azure OpenAI Resource

1. Navigate to **Azure OpenAI** > **Create**
2. Configure:
   - **Resource group**: Select your resource group
   - **Region**: East US, West Europe, or available region
   - **Name**: `openai-teams-bot-{environment}`
   - **Pricing tier**: Standard
3. Click **Review + create** > **Create**
4. After deployment, go to **Azure OpenAI Studio**
5. Create deployment:
   - **Model**: gpt-4
   - **Deployment name**: `gpt-4-turbo`
   - **Version**: Latest available
   - **Capacity**: 10K TPM (dev) or 50K+ TPM (prod)

#### Step 5: Create Key Vault

1. Navigate to **Key Vault** > **Create**
2. Configure:
   - **Resource group**: Select your resource group
   - **Key vault name**: `kv-teams-bot-{environment}`
   - **Region**: Same as resource group
   - **Pricing tier**: Standard
3. Click **Review + create** > **Create**

#### Step 6: Create App Service Plan

1. Navigate to **App Service plans** > **Create**
2. Configure:
   - **Resource group**: Select your resource group
   - **Name**: `asp-teams-meeting-bot-{environment}`
   - **Operating System**: Windows or Linux
   - **Region**: Same as resource group
   - **Pricing tier**: P1v2
3. Click **Review + create** > **Create**

#### Step 7: Create App Service

1. Navigate to **App Services** > **Create**
2. Configure:
   - **Resource group**: Select your resource group
   - **Name**: `app-teams-meeting-bot-{environment}`
   - **Publish**: Code
   - **Runtime stack**: .NET 10
   - **Operating System**: Windows or Linux
   - **Region**: Same as resource group
   - **App Service Plan**: Select the plan created in Step 6
3. Click **Review + create** > **Create**

#### Step 8: Configure App Service

1. Go to your App Service
2. Navigate to **Settings** > **Configuration**
3. Enable **Always On**:
   - Go to **General settings**
   - Set **Always On** to **On**
   - Click **Save**
4. Enable **HTTPS Only**:
   - Go to **General settings**
   - Set **HTTPS Only** to **On**
   - Click **Save**
5. Enable **Managed Identity**:
   - Go to **Identity**
   - Set **System assigned** to **On**
   - Click **Save**
   - Note the **Object (principal) ID**

#### Step 9: Create Application Insights

1. Navigate to **Application Insights** > **Create**
2. Configure:
   - **Resource group**: Select your resource group
   - **Name**: `appi-teams-bot-{environment}`
   - **Region**: Same as resource group
   - **Resource Mode**: Workspace-based
3. Click **Review + create** > **Create**
4. After creation, note the **Connection String**

### Option 2: Azure CLI (Automated Deployment)

Create a deployment script `deploy-azure-resources.sh`:

```bash
#!/bin/bash

# Variables
RESOURCE_GROUP="rg-teams-meeting-bot-prod"
LOCATION="eastus"
COSMOS_ACCOUNT="cosmos-teams-bot-prod"
OPENAI_ACCOUNT="openai-teams-bot-prod"
KEYVAULT_NAME="kv-teams-bot-prod"
APP_SERVICE_PLAN="asp-teams-meeting-bot-prod"
APP_SERVICE="app-teams-meeting-bot-prod"
APP_INSIGHTS="appi-teams-bot-prod"

# Create Resource Group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Cosmos DB
az cosmosdb create \
  --name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --default-consistency-level Session \
  --locations regionName=$LOCATION

# Create Cosmos DB Database
az cosmosdb sql database create \
  --account-name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --name MeetingSummaries

# Create Cosmos DB Containers
az cosmosdb sql container create \
  --account-name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --database-name MeetingSummaries \
  --name summaries \
  --partition-key-path "/meetingId" \
  --throughput 400

az cosmosdb sql container create \
  --account-name $COSMOS_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --database-name MeetingSummaries \
  --name configurations \
  --partition-key-path "/meetingId" \
  --throughput 400

# Create Azure OpenAI
az cognitiveservices account create \
  --name $OPENAI_ACCOUNT \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --kind OpenAI \
  --sku S0

# Create Key Vault
az keyvault create \
  --name $KEYVAULT_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION

# Create App Service Plan
az appservice plan create \
  --name $APP_SERVICE_PLAN \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION \
  --sku P1V2

# Create App Service
az webapp create \
  --name $APP_SERVICE \
  --resource-group $RESOURCE_GROUP \
  --plan $APP_SERVICE_PLAN \
  --runtime "DOTNET|10.0"

# Configure App Service
az webapp config set \
  --name $APP_SERVICE \
  --resource-group $RESOURCE_GROUP \
  --always-on true \
  --http20-enabled true \
  --min-tls-version 1.2

# Enable Managed Identity
az webapp identity assign \
  --name $APP_SERVICE \
  --resource-group $RESOURCE_GROUP

# Create Application Insights
az monitor app-insights component create \
  --app $APP_INSIGHTS \
  --location $LOCATION \
  --resource-group $RESOURCE_GROUP

echo "Azure resources created successfully!"
```

Run the script:

```bash
chmod +x deploy-azure-resources.sh
./deploy-azure-resources.sh
```

---

## Application Configuration

### Step 1: Retrieve Connection Strings and Keys

#### Cosmos DB Connection String

```bash
az cosmosdb keys list \
  --name cosmos-teams-bot-prod \
  --resource-group rg-teams-meeting-bot-prod \
  --type connection-strings \
  --query "connectionStrings[0].connectionString" \
  --output tsv
```

Or via Azure Portal:
1. Go to Cosmos DB account
2. Navigate to **Settings** > **Keys**
3. Copy **PRIMARY CONNECTION STRING**

#### Azure OpenAI Endpoint and Key

```bash
az cognitiveservices account show \
  --name openai-teams-bot-prod \
  --resource-group rg-teams-meeting-bot-prod \
  --query "properties.endpoint" \
  --output tsv

az cognitiveservices account keys list \
  --name openai-teams-bot-prod \
  --resource-group rg-teams-meeting-bot-prod \
  --query "key1" \
  --output tsv
```

Or via Azure Portal:
1. Go to Azure OpenAI resource
2. Navigate to **Keys and Endpoint**
3. Copy **Endpoint** and **Key 1**

#### Application Insights Connection String

```bash
az monitor app-insights component show \
  --app appi-teams-bot-prod \
  --resource-group rg-teams-meeting-bot-prod \
  --query "connectionString" \
  --output tsv
```

Or via Azure Portal:
1. Go to Application Insights resource
2. Navigate to **Overview**
3. Copy **Connection String**

### Step 2: Store Secrets in Key Vault

```bash
# Bot credentials
az keyvault secret set \
  --vault-name kv-teams-bot-prod \
  --name BotAppId \
  --value "your-bot-app-id"

az keyvault secret set \
  --vault-name kv-teams-bot-prod \
  --name BotAppPassword \
  --value "your-bot-app-password"

# Cosmos DB
az keyvault secret set \
  --vault-name kv-teams-bot-prod \
  --name CosmosConnectionString \
  --value "your-cosmos-connection-string"

# Azure OpenAI
az keyvault secret set \
  --vault-name kv-teams-bot-prod \
  --name OpenAIApiKey \
  --value "your-openai-api-key"

# Webhook ClientState (if using webhook method)
az keyvault secret set \
  --vault-name kv-teams-bot-prod \
  --name WebhookClientState \
  --value "your-random-secret-value"
```

### Step 3: Grant App Service Access to Key Vault

```bash
# Get App Service Managed Identity Object ID
IDENTITY_ID=$(az webapp identity show \
  --name app-teams-meeting-bot-prod \
  --resource-group rg-teams-meeting-bot-prod \
  --query principalId \
  --output tsv)

# Grant access to Key Vault
az keyvault set-policy \
  --name kv-teams-bot-prod \
  --object-id $IDENTITY_ID \
  --secret-permissions get list
```

### Step 4: Configure App Service Settings

Create `appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  },
  
  "MicrosoftAppId": "@Microsoft.KeyVault(SecretUri=https://kv-teams-bot-prod.vault.azure.net/secrets/BotAppId/)",
  "MicrosoftAppPassword": "@Microsoft.KeyVault(SecretUri=https://kv-teams-bot-prod.vault.azure.net/secrets/BotAppPassword/)",
  "MicrosoftAppTenantId": "your-tenant-id",
  
  "CosmosDb": {
    "EndpointUrl": "https://cosmos-teams-bot-prod.documents.azure.com:443/",
    "PrimaryKey": "@Microsoft.KeyVault(SecretUri=https://kv-teams-bot-prod.vault.azure.net/secrets/CosmosConnectionString/)",
    "DatabaseName": "MeetingSummaries",
    "ContainerName": "summaries",
    "ConfigContainerName": "configurations"
  },
  
  "AzureOpenAI": {
    "Endpoint": "https://openai-teams-bot-prod.openai.azure.com/",
    "ApiKey": "@Microsoft.KeyVault(SecretUri=https://kv-teams-bot-prod.vault.azure.net/secrets/OpenAIApiKey/)",
    "DeploymentName": "gpt-4-turbo",
    "ApiVersion": "2024-02-15-preview"
  },
  
  "ApplicationInsights": {
    "ConnectionString": "your-app-insights-connection-string"
  },
  
  "SummarySettings": {
    "DefaultIntervalMinutes": 10,
    "AutoPostToChat": true,
    "EnableLateJoinerNotifications": true,
    "RetentionDays": 30,
    "TranscriptionMethod": "Webhook"
  },
  
  "GraphWebhook": {
    "NotificationUrl": "https://app-teams-meeting-bot-prod.azurewebsites.net",
    "ClientState": "@Microsoft.KeyVault(SecretUri=https://kv-teams-bot-prod.vault.azure.net/secrets/WebhookClientState/)"
  }
}
```

---

## Deployment Steps

### Step 1: Build the Application

```bash
# Navigate to project directory
cd TeamsMeetingBot

# Restore dependencies
dotnet restore

# Build in Release mode
dotnet build --configuration Release

# Publish
dotnet publish --configuration Release --output ./publish
```

### Step 2: Deploy to Azure App Service

#### Option A: Using Azure CLI

```bash
# Create deployment package
cd publish
zip -r ../deploy.zip .
cd ..

# Deploy to App Service
az webapp deployment source config-zip \
  --resource-group rg-teams-meeting-bot-prod \
  --name app-teams-meeting-bot-prod \
  --src deploy.zip
```

#### Option B: Using Visual Studio

1. Right-click on the project in Solution Explorer
2. Select **Publish**
3. Choose **Azure** > **Azure App Service (Windows/Linux)**
4. Select your subscription and App Service
5. Click **Publish**

#### Option C: Using GitHub Actions

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to Azure

on:
  push:
    branches: [ main ]

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v2
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: '10.0.x'
    
    - name: Restore dependencies
      run: dotnet restore TeamsMeetingBot/TeamsMeetingBot.csproj
    
    - name: Build
      run: dotnet build TeamsMeetingBot/TeamsMeetingBot.csproj --configuration Release --no-restore
    
    - name: Publish
      run: dotnet publish TeamsMeetingBot/TeamsMeetingBot.csproj --configuration Release --output ./publish
    
    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v2
      with:
        app-name: 'app-teams-meeting-bot-prod'
        publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
        package: ./publish
```

### Step 3: Update Bot Messaging Endpoint

1. Go to **Teams Developer Portal**
2. Navigate to your app > **Configure**
3. Update **Bot endpoint address**:
   - `https://app-teams-meeting-bot-prod.azurewebsites.net/api/messages`
4. Click **Save**

### Step 4: Verify Deployment

1. Navigate to your App Service URL: `https://app-teams-meeting-bot-prod.azurewebsites.net`
2. You should see a response (may be empty or a health check page)
3. Check Application Insights for telemetry data

---

## Post-Deployment Verification

### 1. Health Check

Test the bot endpoint:

```bash
curl https://app-teams-meeting-bot-prod.azurewebsites.net/api/messages
```

Expected response: `405 Method Not Allowed` (this is correct - POST is required)

### 2. Test Bot in Teams

1. Open Microsoft Teams
2. Go to **Apps** > **Built for your org**
3. Find and install **Meeting Summary Bot**
4. Start a test meeting
5. Invite the bot to the meeting
6. Enable transcription
7. Speak for a few minutes
8. Wait for the summary (default: 10 minutes)

### 3. Verify Logs

Check Application Insights:

```kusto
traces
| where timestamp > ago(1h)
| where message contains "Meeting"
| order by timestamp desc
| take 50
```

Look for:
- `Meeting started: {MeetingId}`
- `Transcription subscription started`
- `Summary generated and posted`

### 4. Verify Data Storage

Check Cosmos DB:

1. Go to Cosmos DB account
2. Navigate to **Data Explorer**
3. Expand **MeetingSummaries** > **summaries**
4. Click **Items**
5. Verify summaries are being stored

### 5. Test Webhook (if using webhook method)

```bash
# Test webhook endpoint
curl -X POST https://app-teams-meeting-bot-prod.azurewebsites.net/api/notifications?validationToken=test123
```

Expected response: `test123` (echoed back)

---

## Troubleshooting

### Common Issues

#### Issue 1: Bot Not Responding

**Symptoms**: Bot doesn't respond to messages or meeting events

**Solutions**:
1. Verify App Service is running:
   ```bash
   az webapp show --name app-teams-meeting-bot-prod --resource-group rg-teams-meeting-bot-prod --query state
   ```
2. Check Always On is enabled
3. Verify messaging endpoint in Teams Developer Portal
4. Check Application Insights for errors

#### Issue 2: Authentication Errors

**Symptoms**: 401 Unauthorized errors in logs

**Solutions**:
1. Verify `MicrosoftAppId` and `MicrosoftAppPassword` are correct
2. Check API permissions are granted with admin consent
3. Verify tenant ID is correct
4. Test credentials:
   ```bash
   curl -X POST https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token \
     -d "client_id={app-id}" \
     -d "client_secret={app-password}" \
     -d "scope=https://graph.microsoft.com/.default" \
     -d "grant_type=client_credentials"
   ```

#### Issue 3: No Transcriptions Received

**Symptoms**: Bot starts but no transcription segments are captured

**Solutions**:
1. Verify transcription is enabled in the meeting
2. Check API permissions include `Calls.AccessMedia.All`
3. Verify meeting has active speakers
4. Check logs for transcription errors:
   ```kusto
   traces
   | where message contains "transcription"
   | order by timestamp desc
   ```

#### Issue 4: Cosmos DB Connection Errors

**Symptoms**: Errors saving summaries

**Solutions**:
1. Verify Cosmos DB connection string is correct
2. Check firewall rules allow App Service IP
3. Verify database and containers exist
4. Test connection:
   ```bash
   az cosmosdb sql database show \
     --account-name cosmos-teams-bot-prod \
     --resource-group rg-teams-meeting-bot-prod \
     --name MeetingSummaries
   ```

#### Issue 5: Azure OpenAI Errors

**Symptoms**: Summary generation fails

**Solutions**:
1. Verify Azure OpenAI endpoint and key
2. Check deployment name matches configuration
3. Verify quota and rate limits
4. Check model availability in your region

#### Issue 6: Webhook Subscriptions Failing

**Symptoms**: Webhook method not receiving notifications

**Solutions**:
1. Verify `NotificationUrl` is publicly accessible
2. Check SSL certificate is valid
3. Verify `ClientState` matches configuration
4. Test webhook endpoint:
   ```bash
   curl -X POST https://app-teams-meeting-bot-prod.azurewebsites.net/api/notifications?validationToken=test
   ```
5. Check subscription status in logs

### Diagnostic Commands

```bash
# Check App Service status
az webapp show --name app-teams-meeting-bot-prod --resource-group rg-teams-meeting-bot-prod

# View App Service logs
az webapp log tail --name app-teams-meeting-bot-prod --resource-group rg-teams-meeting-bot-prod

# Check Key Vault access
az keyvault secret show --vault-name kv-teams-bot-prod --name BotAppId

# Test Cosmos DB connectivity
az cosmosdb sql database list --account-name cosmos-teams-bot-prod --resource-group rg-teams-meeting-bot-prod
```

---

## Deployment Checklist

### Pre-Deployment

- [ ] Azure subscription with appropriate permissions
- [ ] Microsoft 365 tenant with Teams enabled
- [ ] Global Administrator access for API consent
- [ ] Azure CLI installed and configured
- [ ] .NET 10.0 SDK installed
- [ ] Bot registered in Azure AD
- [ ] Client secret created and stored securely

### Azure Resources

- [ ] Resource group created
- [ ] Cosmos DB account created
- [ ] Cosmos DB database and containers created
- [ ] Azure OpenAI resource created
- [ ] GPT-4 model deployed
- [ ] Key Vault created
- [ ] App Service Plan created (P1v2)
- [ ] App Service created
- [ ] Application Insights created

### Configuration

- [ ] All secrets stored in Key Vault
- [ ] App Service Managed Identity enabled
- [ ] Key Vault access policy configured
- [ ] App Service Always On enabled
- [ ] App Service HTTPS Only enabled
- [ ] Application settings configured
- [ ] Connection strings verified

### Bot Registration

- [ ] Bot registered in Teams Developer Portal
- [ ] Bot messaging endpoint configured
- [ ] Bot commands added (optional)
- [ ] App published to organization
- [ ] Admin approval obtained (if required)

### API Permissions

- [ ] `OnlineMeetings.Read.All` added and granted
- [ ] `OnlineMeetings.ReadWrite.All` added and granted
- [ ] `Calls.AccessMedia.All` added and granted
- [ ] `Calls.JoinGroupCall.All` added and granted
- [ ] `Chat.ReadWrite.All` added and granted
- [ ] `User.Read.All` added and granted
- [ ] `ChannelMessage.Send` added and granted
- [ ] `OnlineMeetingTranscript.Read.All` added (if using webhook)
- [ ] Admin consent granted for all permissions

### Deployment

- [ ] Application built in Release mode
- [ ] Application published
- [ ] Deployment package created
- [ ] Application deployed to App Service
- [ ] Deployment verified (no errors)
- [ ] Bot messaging endpoint updated

### Post-Deployment Verification

- [ ] Health check endpoint responds
- [ ] Bot responds in Teams
- [ ] Test meeting conducted
- [ ] Transcription captured
- [ ] Summary generated
- [ ] Summary posted to chat
- [ ] Summary stored in Cosmos DB
- [ ] Application Insights receiving telemetry
- [ ] Webhook endpoint validated (if using webhook)
- [ ] Webhook subscription created (if using webhook)

### Monitoring

- [ ] Application Insights alerts configured
- [ ] Log Analytics workspace configured
- [ ] Dashboard created for key metrics
- [ ] Error notifications set up
- [ ] Performance monitoring enabled

### Documentation

- [ ] Deployment documented
- [ ] Configuration documented
- [ ] Troubleshooting guide reviewed
- [ ] Team trained on bot usage
- [ ] Support contacts documented

---

## Next Steps

After successful deployment:

1. **Monitor Performance**: Set up Application Insights dashboards
2. **Configure Alerts**: Create alerts for errors and performance issues
3. **Test Scenarios**: Conduct thorough testing with various meeting types
4. **User Training**: Train users on bot features and commands
5. **Optimize Settings**: Adjust summary intervals and other settings based on feedback
6. **Scale Resources**: Monitor usage and scale Azure resources as needed
7. **Review Costs**: Monitor Azure costs and optimize resource allocation

---

## Additional Resources

- [Microsoft Bot Framework Documentation](https://docs.microsoft.com/en-us/azure/bot-service/)
- [Microsoft Graph API Documentation](https://docs.microsoft.com/en-us/graph/)
- [Azure OpenAI Service Documentation](https://docs.microsoft.com/en-us/azure/cognitive-services/openai/)
- [Teams Developer Portal](https://dev.teams.microsoft.com)
- [Transcription Methods Guide](./TranscriptionMethods.md)
- [Feature Toggle Guide](./FeatureToggleGuide.md)

---

## Support

For issues or questions:
- Check Application Insights logs
- Review troubleshooting section
- Consult additional documentation in the `Docs` folder
- Contact your Azure support team

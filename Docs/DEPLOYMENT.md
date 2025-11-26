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
- **.NET 8.0 SDK** installed for local builds
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
- **Runtime**: .NET 8.0
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


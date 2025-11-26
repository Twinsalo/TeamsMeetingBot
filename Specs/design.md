# Design Document

## Overview

This document outlines the technical design for a Microsoft Teams bot built with .NET Core and C# that captures live meeting transcriptions, generates periodic summaries using AI, and provides catch-up capabilities for late joiners. The bot leverages the Microsoft Bot Framework SDK, Microsoft Graph API, and Azure services to deliver real-time meeting intelligence.

### Technology Stack

- **Runtime**: .NET 10.0
- **Language**: C# 13
- **Bot Framework**: Microsoft Bot Framework SDK 4.x
- **Teams Integration**: Microsoft Graph API
- **AI/ML**: Azure OpenAI Service (GPT-4) for summary generation
- **Storage**: Azure Cosmos DB for summary persistence
- **Hosting**: Azure App Service or Azure Functions
- **Authentication**: Microsoft Entra ID (Azure AD)

## Architecture

### High-Level Architecture

```
┌─────────────────┐
│  Teams Meeting  │
│   (Live Audio)  │
└────────┬────────┘
         │ Transcription Stream
         ▼
┌─────────────────────────────────────────────────────┐
│           Microsoft Teams Platform                   │
│  (Provides Live Transcription via Graph API)        │
└────────┬────────────────────────────────────────────┘
         │ Real-time Events
         ▼
┌─────────────────────────────────────────────────────┐
│              Teams Bot (.NET Core)                   │
│  ┌──────────────────────────────────────────────┐  │
│  │  Bot Controller (ASP.NET Core)               │  │
│  └──────────────┬───────────────────────────────┘  │
│                 │                                    │
│  ┌──────────────▼───────────────────────────────┐  │
│  │  Meeting Event Handler                       │  │
│  │  - Join/Leave Detection                      │  │
│  │  - Transcription Stream Subscription         │  │
│  └──────────────┬───────────────────────────────┘  │
│                 │                                    │
│  ┌──────────────▼───────────────────────────────┐  │
│  │  Transcription Buffer Service                │  │
│  │  - In-Memory Buffer (ConcurrentQueue)        │  │
│  │  - Speaker Attribution                       │  │
│  │  - Timestamp Management                      │  │
│  └──────────────┬───────────────────────────────┘  │
│                 │                                    │
│  ┌──────────────▼───────────────────────────────┐  │
│  │  Summary Generation Service                  │  │
│  │  - Periodic Timer (10 min intervals)         │  │
│  │  - Azure OpenAI Integration                  │  │
│  │  - Key Points Extraction                     │  │
│  └──────────────┬───────────────────────────────┘  │
│                 │                                    │
│  ┌──────────────▼───────────────────────────────┐  │
│  │  Summary Storage Service                     │  │
│  │  - Cosmos DB Client                          │  │
│  │  - Access Control Management                 │  │
│  └──────────────┬───────────────────────────────┘  │
│                 │                                    │
│  ┌──────────────▼───────────────────────────────┐  │
│  │  Late Joiner Service                         │  │
│  │  - Join Event Detection                      │  │
│  │  - Summary Retrieval & Delivery              │  │
│  └──────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
         │                              │
         ▼                              ▼
┌─────────────────┐          ┌──────────────────┐
│  Azure OpenAI   │          │  Azure Cosmos DB │
│   (GPT-4 API)   │          │  (Summary Store) │
└─────────────────┘          └──────────────────┘
```

### Component Interaction Flow

1. **Meeting Start**: Bot subscribes to meeting events via Graph API
2. **Transcription Capture**: Real-time transcription segments buffered in memory
3. **Periodic Summary**: Timer triggers summary generation every 10 minutes
4. **AI Processing**: Buffered transcriptions sent to Azure OpenAI for summarization
5. **Storage**: Generated summary persisted to Cosmos DB with metadata
6. **Chat Posting**: Summary posted to Teams meeting chat
7. **Late Joiner**: On join event, retrieve and send previous summaries via adaptive card

## Components and Interfaces

### 1. Bot Controller

**Purpose**: ASP.NET Core controller handling incoming Teams messages and events

**Implementation**:
```csharp
[Route("api/messages")]
[ApiController]
public class BotController : ControllerBase
{
    private readonly IBotFrameworkHttpAdapter _adapter;
    private readonly IBot _bot;
    
    public BotController(IBotFrameworkHttpAdapter adapter, IBot bot)
    {
        _adapter = adapter;
        _bot = bot;
    }
    
    [HttpPost]
    public async Task PostAsync()
    {
        await _adapter.ProcessAsync(Request, Response, _bot);
    }
}
```

### 2. Meeting Bot Activity Handler

**Purpose**: Core bot logic handling Teams-specific events

**Key Methods**:
- `OnTeamsMeetingStartAsync()`: Initialize transcription subscription
- `OnTeamsMeetingEndAsync()`: Cleanup and finalize
- `OnTeamsMeetingParticipantsJoinAsync()`: Detect late joiners
- `OnMessageActivityAsync()`: Handle bot commands

**Interface**:
```csharp
public interface IMeetingBotHandler
{
    Task OnMeetingStartAsync(ITurnContext<IEventActivity> turnContext, MeetingStartEventDetails meetingStartEventDetails, CancellationToken cancellationToken);
    Task OnMeetingEndAsync(ITurnContext<IEventActivity> turnContext, MeetingEndEventDetails meetingEndEventDetails, CancellationToken cancellationToken);
    Task OnParticipantJoinAsync(ITurnContext<IEventActivity> turnContext, ParticipantJoinEventDetails participantDetails, CancellationToken cancellationToken);
}
```

### 3. Transcription Buffer Service

**Purpose**: Manage in-memory buffer of transcription segments

**Interface**:
```csharp
public interface ITranscriptionBufferService
{
    void AddSegment(string meetingId, TranscriptionSegment segment);
    IEnumerable<TranscriptionSegment> GetSegments(string meetingId, TimeSpan? duration = null);
    void ClearBuffer(string meetingId);
    bool HasSegments(string meetingId);
}

public class TranscriptionSegment
{
    public string Text { get; set; }
    public string SpeakerId { get; set; }
    public string SpeakerName { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
```

**Implementation Details**:
- Uses `ConcurrentDictionary<string, ConcurrentQueue<TranscriptionSegment>>` for thread-safe operations
- Key: meetingId, Value: Queue of segments
- Automatic cleanup after summary generation
- Memory limit: 1000 segments per meeting (approximately 2 hours)

### 4. Graph API Service

**Purpose**: Interface with Microsoft Graph API for transcription and meeting data

**Interface**:
```csharp
public interface IGraphApiService
{
    Task<IAsyncEnumerable<TranscriptionSegment>> SubscribeToTranscriptionAsync(string meetingId, CancellationToken cancellationToken);
    Task<MeetingDetails> GetMeetingDetailsAsync(string meetingId);
    Task<IEnumerable<Participant>> GetMeetingParticipantsAsync(string meetingId);
    Task SendMessageToMeetingChatAsync(string meetingId, string message);
    Task SendPrivateMessageAsync(string userId, AdaptiveCard card);
}
```

**Graph API Endpoints Used**:
- `/communications/calls/{id}/transcription` - Subscribe to live transcription
- `/communications/calls/{id}` - Get meeting details
- `/chats/{id}/messages` - Post summary to chat
- `/users/{id}/chats` - Send private messages to late joiners

### 5. Summary Generation Service

**Purpose**: Generate AI-powered summaries using Azure OpenAI

**Interface**:
```csharp
public interface ISummaryGenerationService
{
    Task<MeetingSummary> GenerateSummaryAsync(IEnumerable<TranscriptionSegment> segments, SummaryOptions options);
}

public class MeetingSummary
{
    public string SummaryId { get; set; }
    public string MeetingId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string Content { get; set; }
    public List<string> KeyTopics { get; set; }
    public List<string> Decisions { get; set; }
    public List<ActionItem> ActionItems { get; set; }
}

public class ActionItem
{
    public string Description { get; set; }
    public string AssignedTo { get; set; }
}
```

**Implementation Details**:
- Uses Azure OpenAI SDK for .NET
- Model: GPT-4 (gpt-4-turbo)
- Prompt engineering for structured output (topics, decisions, action items)
- Token limit management: ~8000 tokens per request
- Retry logic with exponential backoff

**Prompt Template**:
```
Analyze the following meeting transcription and provide:
1. A concise summary (3-5 sentences)
2. Key topics discussed (bullet points)
3. Decisions made (bullet points)
4. Action items with assignees if mentioned

Transcription:
{transcription_text}

Format the response as JSON with fields: summary, keyTopics, decisions, actionItems
```

### 6. Summary Storage Service

**Purpose**: Persist and retrieve summaries from Cosmos DB

**Interface**:
```csharp
public interface ISummaryStorageService
{
    Task<string> SaveSummaryAsync(MeetingSummary summary);
    Task<MeetingSummary> GetSummaryAsync(string summaryId);
    Task<IEnumerable<MeetingSummary>> GetMeetingSummariesAsync(string meetingId, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null);
    Task<IEnumerable<MeetingSummary>> SearchSummariesAsync(string meetingId, string searchQuery);
    Task DeleteSummariesAsync(string meetingId);
}
```

**Cosmos DB Schema**:
```json
{
  "id": "summary-{guid}",
  "meetingId": "meeting-123",
  "partitionKey": "meeting-123",
  "startTime": "2025-11-21T10:00:00Z",
  "endTime": "2025-11-21T10:10:00Z",
  "content": "Summary text...",
  "keyTopics": ["Topic 1", "Topic 2"],
  "decisions": ["Decision 1"],
  "actionItems": [
    {
      "description": "Task description",
      "assignedTo": "John Doe"
    }
  ],
  "participants": ["user1@domain.com", "user2@domain.com"],
  "createdAt": "2025-11-21T10:10:05Z",
  "ttl": 2592000
}
```

**Indexing Strategy**:
- Partition Key: `meetingId` (for efficient queries per meeting)
- Indexed Properties: `startTime`, `endTime`, `createdAt`
- Full-text search on `content`, `keyTopics`, `decisions`
- TTL: Configurable (default 30 days)

### 7. Late Joiner Service

**Purpose**: Detect late joiners and deliver catch-up summaries

**Interface**:
```csharp
public interface ILateJoinerService
{
    Task HandleParticipantJoinAsync(string meetingId, Participant participant, DateTimeOffset meetingStartTime);
}
```

**Implementation Flow**:
1. Detect join event via `OnTeamsMeetingParticipantsJoinAsync`
2. Compare join time with meeting start time
3. If late (>5 minutes), retrieve summaries from storage
4. Format summaries into Adaptive Card
5. Send private message to participant

**Adaptive Card Format**:
```json
{
  "type": "AdaptiveCard",
  "body": [
    {
      "type": "TextBlock",
      "text": "Meeting Catch-Up",
      "weight": "Bolder",
      "size": "Large"
    },
    {
      "type": "TextBlock",
      "text": "You joined late. Here's what you missed:",
      "wrap": true
    },
    {
      "type": "Container",
      "items": [
        {
          "type": "TextBlock",
          "text": "10:00 - 10:10 AM",
          "weight": "Bolder"
        },
        {
          "type": "TextBlock",
          "text": "{summary content}",
          "wrap": true
        }
      ]
    }
  ]
}
```

### 8. Configuration Service

**Purpose**: Manage bot configuration and meeting-specific settings

**Interface**:
```csharp
public interface IConfigurationService
{
    Task<MeetingConfiguration> GetMeetingConfigAsync(string meetingId);
    Task UpdateMeetingConfigAsync(string meetingId, MeetingConfiguration config);
}

public class MeetingConfiguration
{
    public int SummaryIntervalMinutes { get; set; } = 10;
    public bool AutoPostToChat { get; set; } = true;
    public bool EnableLateJoinerNotifications { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public TranscriptionMethod TranscriptionMethod { get; set; } = TranscriptionMethod.Polling;
}

public enum TranscriptionMethod
{
    Polling = 0,   // Traditional polling-based approach
    Webhook = 1    // Microsoft Graph Change Notifications (event-driven)
}
```

### 9. Transcription Strategy Pattern

**Purpose**: Provide pluggable architecture for different transcription retrieval methods

**Interface**:
```csharp
public interface ITranscriptionStrategy
{
    Task StartAsync(string meetingId, CancellationToken cancellationToken);
    Task StopAsync(string meetingId);
    TranscriptionMethod Method { get; }
}
```

**Implementations**:

#### Polling Strategy (Default)
- Periodically polls Graph API for transcription updates
- Simple setup, no public endpoint required
- Suitable for development and low-volume scenarios
- Higher API call volume (~1800 calls/hour per meeting)

#### Webhook Strategy (Production)
- Uses Microsoft Graph Change Notifications
- Event-driven, near real-time updates
- Requires public HTTPS endpoint
- 99% reduction in API calls (~10 calls/hour per meeting)
- Automatic subscription management (create, renew, delete)

**Factory**:
```csharp
public class TranscriptionStrategyFactory
{
    public ITranscriptionStrategy CreateStrategy(TranscriptionMethod method);
}
```

## Data Models

### Core Domain Models

```csharp
// Meeting context
public class MeetingContext
{
    public string MeetingId { get; set; }
    public string TenantId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public List<string> ParticipantIds { get; set; }
    public MeetingConfiguration Configuration { get; set; }
}

// Transcription segment (in-memory only)
public class TranscriptionSegment
{
    public string Text { get; set; }
    public string SpeakerId { get; set; }
    public string SpeakerName { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

// Meeting summary (persisted)
public class MeetingSummary
{
    public string Id { get; set; }
    public string MeetingId { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public string Content { get; set; }
    public List<string> KeyTopics { get; set; }
    public List<string> Decisions { get; set; }
    public List<ActionItem> ActionItems { get; set; }
    public List<string> Participants { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// Participant info
public class Participant
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public DateTimeOffset JoinTime { get; set; }
}
```

## Error Handling

### Error Categories and Strategies

#### 1. Transcription Stream Errors

**Scenarios**:
- Network interruption
- Graph API throttling
- Permission issues

**Handling**:
```csharp
public class TranscriptionStreamHandler
{
    private const int MaxRetries = 3;
    private const int RetryDelaySeconds = 5;
    
    public async Task HandleTranscriptionStreamAsync(string meetingId)
    {
        var retryCount = 0;
        
        while (retryCount < MaxRetries)
        {
            try
            {
                await SubscribeToStreamAsync(meetingId);
                break;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds * Math.Pow(2, retryCount)));
                retryCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcription stream error for meeting {MeetingId}", meetingId);
                await NotifyOrganizerAsync(meetingId, "Transcription temporarily unavailable");
                throw;
            }
        }
    }
}
```

#### 2. Summary Generation Errors

**Scenarios**:
- Azure OpenAI service unavailable
- Token limit exceeded
- Invalid response format

**Handling**:
- Retry once after 30 seconds
- Log error with transcription snippet for debugging
- Notify organizer via private message
- Continue capturing transcriptions

```csharp
public async Task<MeetingSummary> GenerateSummaryWithRetryAsync(IEnumerable<TranscriptionSegment> segments)
{
    try
    {
        return await _openAiService.GenerateSummaryAsync(segments);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Summary generation failed, retrying in 30 seconds");
        await Task.Delay(TimeSpan.FromSeconds(30));
        
        try
        {
            return await _openAiService.GenerateSummaryAsync(segments);
        }
        catch (Exception retryEx)
        {
            _logger.LogError(retryEx, "Summary generation failed after retry");
            await NotifyOrganizerAsync(meetingId, "Summary generation failed");
            throw;
        }
    }
}
```

#### 3. Storage Errors

**Scenarios**:
- Cosmos DB unavailable
- Network timeout
- Quota exceeded

**Handling**:
- Buffer summaries in memory (max 5 minutes)
- Retry with exponential backoff
- Flush buffer when connectivity restored

```csharp
public class ResilientSummaryStorage
{
    private readonly ConcurrentQueue<MeetingSummary> _buffer = new();
    private readonly SemaphoreSlim _flushLock = new(1);
    
    public async Task SaveSummaryAsync(MeetingSummary summary)
    {
        try
        {
            await _cosmosService.SaveAsync(summary);
            await FlushBufferAsync(); // Flush any buffered items
        }
        catch (CosmosException ex)
        {
            _logger.LogWarning(ex, "Storage unavailable, buffering summary");
            _buffer.Enqueue(summary);
            
            if (_buffer.Count > 10)
            {
                _logger.LogError("Buffer overflow, summaries may be lost");
            }
        }
    }
    
    private async Task FlushBufferAsync()
    {
        if (!_buffer.Any()) return;
        
        await _flushLock.WaitAsync();
        try
        {
            while (_buffer.TryDequeue(out var summary))
            {
                await _cosmosService.SaveAsync(summary);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }
}
```

#### 4. Authentication Errors

**Scenarios**:
- Token expiration
- Insufficient permissions
- Tenant restrictions

**Handling**:
- Automatic token refresh via MSAL
- Graceful degradation (disable features requiring elevated permissions)
- Clear error messages to users

### Logging Strategy

**Structured Logging with Serilog**:
```csharp
Log.Information("Meeting {MeetingId} started with {ParticipantCount} participants", 
    meetingId, participantCount);
    
Log.Warning("Summary generation delayed for meeting {MeetingId}, retry attempt {RetryCount}", 
    meetingId, retryCount);
    
Log.Error(ex, "Critical error in meeting {MeetingId}: {ErrorMessage}", 
    meetingId, ex.Message);
```

**Log Sinks**:
- Azure Application Insights (production)
- Console (development)
- File (local debugging)

## Testing Strategy

### Unit Testing

**Framework**: xUnit with Moq

**Coverage Areas**:
- Transcription buffer operations (add, retrieve, clear)
- Summary generation prompt formatting
- Configuration validation
- Data model serialization

**Example**:
```csharp
public class TranscriptionBufferServiceTests
{
    [Fact]
    public void AddSegment_ShouldStoreSegmentInBuffer()
    {
        // Arrange
        var service = new TranscriptionBufferService();
        var segment = new TranscriptionSegment 
        { 
            Text = "Test", 
            Timestamp = DateTimeOffset.UtcNow 
        };
        
        // Act
        service.AddSegment("meeting-1", segment);
        
        // Assert
        var segments = service.GetSegments("meeting-1");
        Assert.Single(segments);
        Assert.Equal("Test", segments.First().Text);
    }
}
```

### Integration Testing

**Framework**: xUnit with TestServer (ASP.NET Core)

**Coverage Areas**:
- Bot controller message handling
- Graph API service with mock responses
- Cosmos DB operations with emulator
- End-to-end summary generation flow

**Example**:
```csharp
public class BotIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PostMessage_ShouldProcessMeetingStartEvent()
    {
        // Arrange
        var client = _factory.CreateClient();
        var activity = CreateMeetingStartActivity();
        
        // Act
        var response = await client.PostAsJsonAsync("/api/messages", activity);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Verify transcription subscription was initiated
    }
}
```

### Manual Testing

**Test Scenarios**:
1. **Happy Path**: Join meeting, verify summaries generated every 10 minutes
2. **Late Joiner**: Join meeting 15 minutes late, verify catch-up message received
3. **Network Interruption**: Disconnect network during meeting, verify reconnection
4. **Configuration Changes**: Update summary interval, verify new interval applied
5. **Search**: Search summaries for specific keywords, verify results

**Test Environment**:
- Teams Developer Portal for bot registration
- Test tenant with sample meetings
- Azure resources in development subscription

### Performance Testing

**Tools**: NBomber or k6

**Scenarios**:
- Concurrent meetings: 100 simultaneous meetings
- High transcription rate: 10 segments/second per meeting
- Summary generation latency: <5 seconds for 10 minutes of transcription
- Storage throughput: 1000 summaries/minute

**Acceptance Criteria**:
- 99th percentile latency <3 seconds for summary retrieval
- Zero data loss during network interruptions
- Memory usage <500MB per meeting

## Security Considerations

### Authentication & Authorization

- **Bot Authentication**: Microsoft App ID and Password (stored in Azure Key Vault)
- **User Authentication**: Microsoft Entra ID (Azure AD) with OAuth 2.0
- **API Permissions**: 
  - `OnlineMeetings.Read.All` (application permission) - Required for both methods
  - `OnlineMeetings.ReadWrite.All` (application permission) - Required for both methods
  - `Calls.AccessMedia.All` (application permission) - Required for both methods
  - `Chat.ReadWrite` (application permission) - Required for both methods
  - `User.Read.All` (application permission) - Required for both methods
  - `OnlineMeetingTranscript.Read.All` (application permission) - **Required only for Webhook method**

### Data Protection

- **Encryption at Rest**: Cosmos DB automatic encryption (AES-256)
- **Encryption in Transit**: TLS 1.2+ for all API calls
- **Access Control**: Row-level security based on meeting participant list
- **Data Retention**: Automatic deletion after configured period (30-365 days)
- **PII Handling**: No storage of raw transcriptions, only summaries

### Compliance

- **GDPR**: Right to deletion implemented via `DeleteSummariesAsync`
- **Data Residency**: Cosmos DB region selection based on tenant location
- **Audit Logging**: All data access logged to Application Insights

## Deployment Architecture

### Azure Resources

```
Resource Group: rg-teams-meeting-bot-prod
├── App Service Plan (P1v2)
│   └── App Service: app-teams-bot-prod
├── Cosmos DB Account: cosmos-teams-bot-prod
│   └── Database: MeetingSummaries
│       └── Container: summaries (partition key: /meetingId)
├── Azure OpenAI: openai-teams-bot-prod
│   └── Deployment: gpt-4-turbo
├── Key Vault: kv-teams-bot-prod
│   ├── Secret: BotAppId
│   ├── Secret: BotAppPassword
│   └── Secret: CosmosConnectionString
└── Application Insights: appi-teams-bot-prod
```

### Configuration

**appsettings.json**:
```json
{
  "MicrosoftAppId": "",
  "MicrosoftAppPassword": "",
  "CosmosDb": {
    "EndpointUrl": "",
    "DatabaseName": "MeetingSummaries",
    "ContainerName": "summaries"
  },
  "AzureOpenAI": {
    "Endpoint": "",
    "DeploymentName": "gpt-4-turbo",
    "ApiVersion": "2024-02-15-preview"
  },
  "SummarySettings": {
    "DefaultIntervalMinutes": 10,
    "MaxBufferSize": 1000,
    "RetentionDays": 30
  }
}
```

### CI/CD Pipeline

**GitHub Actions Workflow**:
1. Build .NET project
2. Run unit tests
3. Run integration tests
4. Publish artifacts
5. Deploy to Azure App Service
6. Run smoke tests

## Scalability Considerations

- **Horizontal Scaling**: App Service can scale to multiple instances
- **Stateless Design**: No in-process state (transcription buffer uses distributed cache for multi-instance)
- **Cosmos DB Autoscale**: Automatic RU scaling based on load
- **OpenAI Rate Limiting**: Queue-based processing with retry logic

## Transcription Methods

The bot supports two methods for retrieving meeting transcriptions, selectable via configuration:

### Polling Method (Default)

**How it works**:
- Bot periodically polls Microsoft Graph API for new transcription content
- Checks for updates every 2-5 seconds
- Parses VTT (WebVTT) format transcripts
- Buffers segments in memory

**Advantages**:
- Simple setup, no public endpoint required
- Works in any environment (development, on-premises)
- No webhook validation needed
- Immediate availability

**Disadvantages**:
- Higher API call volume (~1800 calls/hour per meeting)
- Higher latency (2-5 seconds)
- More resource intensive (continuous polling)

**Use Cases**:
- Development and testing environments
- Deployments without public endpoints
- Low-volume scenarios (<10 concurrent meetings)

### Webhook Method (Recommended for Production)

**How it works**:
- Bot creates Microsoft Graph subscription for transcription events
- Microsoft sends HTTP POST notifications when transcripts are available
- Bot receives notifications at `/api/notifications` endpoint
- Fetches transcript content on-demand
- Automatically renews subscriptions every 45 minutes

**Advantages**:
- Event-driven, near real-time updates (<1 second latency)
- 99% reduction in API calls (~10 calls/hour per meeting)
- Lower resource usage (no continuous polling)
- Better scalability (supports 100+ concurrent meetings)

**Disadvantages**:
- Requires public HTTPS endpoint
- More complex setup (webhook validation, subscription management)
- Additional API permission required (`OnlineMeetingTranscript.Read.All`)

**Use Cases**:
- Production environments
- High-volume scenarios (>10 concurrent meetings)
- Cost optimization requirements
- Real-time update requirements

### Configuration

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"  // or "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://your-bot.azurewebsites.net",
    "ClientState": "your-secret-value"
  }
}
```

### Architecture Comparison

**Polling Flow**:
```
Meeting → Graph API ← Bot (polls every 2-5s) → Buffer → Summary
```

**Webhook Flow**:
```
Meeting → Graph API → Webhook Notification → Bot → Fetch Content → Buffer → Summary
```

## Future Enhancements

1. **Multi-language Support**: Detect language and generate summaries in native language
2. **Custom Prompts**: Allow organizers to customize summary format
3. **Integration with Microsoft Loop**: Post summaries to Loop workspace
4. **Sentiment Analysis**: Track meeting sentiment over time
5. **Speaker Analytics**: Identify dominant speakers and participation patterns
6. **Hybrid Transcription**: Automatic failover from webhook to polling if webhook fails

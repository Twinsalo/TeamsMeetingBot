# Transcription Architecture

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Teams Meeting Bot                                │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │         MeetingBotActivityHandler                          │    │
│  │         (Orchestrates meeting lifecycle)                   │    │
│  └────────────────────┬───────────────────────────────────────┘    │
│                       │                                             │
│                       │ Uses                                        │
│                       ▼                                             │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │      TranscriptionStrategyFactory                          │    │
│  │      (Selects strategy based on configuration)             │    │
│  └────────────────────┬───────────────────────────────────────┘    │
│                       │                                             │
│           ┌───────────┴──────────┐                                  │
│           │                      │                                  │
│           ▼                      ▼                                  │
│  ┌─────────────────┐    ┌─────────────────────┐                    │
│  │    Polling      │    │     Webhook         │                    │
│  │   Strategy      │    │    Strategy         │                    │
│  │                 │    │                     │                    │
│  │ - Polls Graph   │    │ - Creates           │                    │
│  │   API every     │    │   subscription      │                    │
│  │   2-5 seconds   │    │ - Receives          │                    │
│  │ - Buffers       │    │   notifications     │                    │
│  │   segments      │    │ - Fetches content   │                    │
│  └────────┬────────┘    └──────────┬──────────┘                    │
│           │                        │                                │
└───────────┼────────────────────────┼────────────────────────────────┘
            │                        │
            │                        │
            ▼                        ▼
┌───────────────────────┐  ┌─────────────────────────────┐
│  Microsoft Graph API  │  │  Graph Change Notifications │
│                       │  │                             │
│  GET /transcripts     │  │  POST /api/notifications    │
│  (Polling)            │  │  (Webhook)                  │
└───────────────────────┘  └─────────────────────────────┘
```

## Polling Method Flow

```
┌──────────┐
│ Meeting  │
│  Starts  │
└────┬─────┘
     │
     ▼
┌─────────────────────────┐
│ Create Polling Strategy │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Start Background Task   │
│ (Polls every 2-5 sec)   │
└────┬────────────────────┘
     │
     │ Loop
     ▼
┌─────────────────────────┐
│ GET /transcripts        │
│ from Graph API          │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Parse VTT Content       │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Buffer Segments         │
│ in Memory               │
└────┬────────────────────┘
     │
     │ Every 10 min
     ▼
┌─────────────────────────┐
│ Generate Summary        │
└─────────────────────────┘
```

## Webhook Method Flow

```
┌──────────┐
│ Meeting  │
│  Starts  │
└────┬─────┘
     │
     ▼
┌─────────────────────────┐
│ Create Webhook Strategy │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ POST /subscriptions     │
│ to Graph API            │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Microsoft Validates     │
│ Webhook Endpoint        │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Subscription Active     │
│ (60 min expiration)     │
└────┬────────────────────┘
     │
     │ When transcript available
     ▼
┌─────────────────────────┐
│ Microsoft POSTs to      │
│ /api/notifications      │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Validate ClientState    │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Extract Meeting ID &    │
│ Transcript ID           │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ GET /transcripts/{id}   │
│ /content                │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Parse VTT Content       │
└────┬────────────────────┘
     │
     ▼
┌─────────────────────────┐
│ Buffer Segments         │
│ in Memory               │
└────┬────────────────────┘
     │
     │ Every 10 min
     ▼
┌─────────────────────────┐
│ Generate Summary        │
└─────────────────────────┘
```

## Component Interaction

```
┌─────────────────────────────────────────────────────────────┐
│                    Configuration Layer                       │
│                                                              │
│  appsettings.json → ConfigurationService → MeetingConfig    │
│                                                              │
│  TranscriptionMethod: Polling | Webhook                     │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    Strategy Layer                            │
│                                                              │
│  TranscriptionStrategyFactory                               │
│         │                                                    │
│         ├─→ PollingTranscriptionStrategy                    │
│         │      │                                             │
│         │      └─→ GraphApiService                          │
│         │                                                    │
│         └─→ WebhookTranscriptionStrategy                    │
│                │                                             │
│                ├─→ GraphSubscriptionService                 │
│                └─→ GraphWebhookController                   │
└──────────────────────┬──────────────────────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────────────────┐
│                    Processing Layer                          │
│                                                              │
│  TranscriptionBufferService                                 │
│         │                                                    │
│         └─→ In-Memory Buffer (ConcurrentQueue)             │
│                                                              │
│  SummaryGenerationService                                   │
│         │                                                    │
│         └─→ Azure OpenAI (GPT-4)                            │
│                                                              │
│  SummaryStorageService                                      │
│         │                                                    │
│         └─→ Cosmos DB                                       │
└─────────────────────────────────────────────────────────────┘
```

## Data Flow

### Transcription Segment Journey

```
1. Teams Meeting (Live Audio)
   │
   ▼
2. Microsoft Teams Platform (Speech-to-Text)
   │
   ▼
3. Graph API (Transcription Storage)
   │
   ├─→ Polling: Bot polls periodically
   │
   └─→ Webhook: Microsoft pushes notification
   │
   ▼
4. Transcription Strategy (Selected by config)
   │
   ▼
5. TranscriptionBufferService (In-Memory Queue)
   │
   ▼
6. SummaryGenerationService (Every 10 min)
   │
   ▼
7. Azure OpenAI (GPT-4 Processing)
   │
   ▼
8. MeetingSummary Object
   │
   ├─→ SummaryStorageService → Cosmos DB
   │
   └─→ GraphApiService → Teams Chat
```

## Subscription Lifecycle (Webhook Only)

```
┌─────────────────┐
│ Meeting Starts  │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────┐
│ Create Subscription         │
│ - Resource: /transcripts    │
│ - Expiration: +60 min       │
│ - NotificationUrl: /api/... │
└────────┬────────────────────┘
         │
         ▼
┌─────────────────────────────┐
│ Subscription Active         │
│ (ID stored in memory)       │
└────────┬────────────────────┘
         │
         │ Every 45 minutes
         ▼
┌─────────────────────────────┐
│ Renew Subscription          │
│ - Extend expiration +60 min │
└────────┬────────────────────┘
         │
         │ Meeting ends
         ▼
┌─────────────────────────────┐
│ Delete Subscription         │
│ - Clean up resources        │
└─────────────────────────────┘
```

## Error Handling Flow

```
┌─────────────────────┐
│ Transcription Error │
└────────┬────────────┘
         │
         ▼
    ┌────────┐
    │ Retry? │
    └───┬────┘
        │
    ┌───┴───┐
    │       │
    ▼       ▼
  Yes      No
    │       │
    │       └─→ Log Error → Notify Organizer
    │
    ▼
┌─────────────────────┐
│ Wait 5 seconds      │
│ (Requirement 1.3)   │
└────────┬────────────┘
         │
         ▼
┌─────────────────────┐
│ Attempt Reconnect   │
└────────┬────────────┘
         │
         ▼
    ┌────────┐
    │Success?│
    └───┬────┘
        │
    ┌───┴───┐
    │       │
    ▼       ▼
  Yes      No
    │       │
    │       └─→ Continue Retrying (with backoff)
    │
    └─→ Resume Normal Operation
```

## Configuration Decision Tree

```
                    Start
                      │
                      ▼
            ┌──────────────────┐
            │ Need real-time   │
            │ updates?         │
            └────────┬─────────┘
                     │
            ┌────────┴────────┐
            │                 │
           Yes               No
            │                 │
            ▼                 ▼
    ┌──────────────┐   ┌──────────────┐
    │ Have public  │   │ Use Polling  │
    │ endpoint?    │   └──────────────┘
    └──────┬───────┘
           │
    ┌──────┴──────┐
    │             │
   Yes           No
    │             │
    ▼             ▼
┌──────────┐  ┌──────────────┐
│   Use    │  │ Use Polling  │
│ Webhook  │  │ or setup     │
│          │  │ public       │
│          │  │ endpoint     │
└──────────┘  └──────────────┘
```

## Performance Comparison

```
Metric: API Calls per Hour

Polling:  ████████████████████████████████████████ 1800 calls
Webhook:  █ 10 calls

Metric: Average Latency

Polling:  ████ 2-5 seconds
Webhook:  █ <1 second

Metric: Resource Usage (CPU)

Polling:  ████████ Medium
Webhook:  ███ Low

Metric: Setup Complexity

Polling:  ██ Simple
Webhook:  ████████ Moderate
```

## Security Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Security Layers                       │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │ Layer 1: Authentication                        │    │
│  │ - Microsoft Entra ID (Azure AD)                │    │
│  │ - OAuth 2.0 tokens                             │    │
│  │ - Managed Identity (for Azure resources)      │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │ Layer 2: Authorization                         │    │
│  │ - API Permissions (Graph API)                  │    │
│  │ - Admin Consent                                │    │
│  │ - Participant-based access control             │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │ Layer 3: Transport Security                    │    │
│  │ - TLS 1.2+ (all communications)                │    │
│  │ - Valid SSL certificates                       │    │
│  │ - HTTPS only                                   │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │ Layer 4: Data Security                         │    │
│  │ - Encryption at rest (Cosmos DB)               │    │
│  │ - Encryption in transit (TLS)                  │    │
│  │ - No raw transcription storage                 │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │ Layer 5: Webhook Security (Webhook only)      │    │
│  │ - ClientState validation                       │    │
│  │ - Validation token exchange                    │    │
│  │ - Secret management (Key Vault)                │    │
│  └────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

## Scalability Architecture

```
                    Load Balancer
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
    ┌─────────┐    ┌─────────┐    ┌─────────┐
    │ Bot     │    │ Bot     │    │ Bot     │
    │Instance │    │Instance │    │Instance │
    │   #1    │    │   #2    │    │   #3    │
    └────┬────┘    └────┬────┘    └────┬────┘
         │               │               │
         └───────────────┼───────────────┘
                         │
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
    ┌─────────┐    ┌─────────┐    ┌─────────┐
    │ Cosmos  │    │ Azure   │    │  Graph  │
    │   DB    │    │ OpenAI  │    │   API   │
    │(Shared) │    │(Shared) │    │(Shared) │
    └─────────┘    └─────────┘    └─────────┘

Note: Webhook method scales better due to:
- Event-driven architecture
- Lower API call volume
- Reduced resource usage per instance
```

---

## Summary

This architecture provides:

✅ **Flexibility**: Two methods for different scenarios
✅ **Scalability**: Event-driven webhook for high volume
✅ **Reliability**: Automatic retry and reconnection
✅ **Security**: Multiple layers of protection
✅ **Observability**: Comprehensive logging and metrics
✅ **Maintainability**: Clean separation of concerns

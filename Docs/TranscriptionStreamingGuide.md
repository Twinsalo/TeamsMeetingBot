# Microsoft Graph Transcription Streaming Implementation Guide

## Overview

This guide explains how to implement real-time transcription streaming using Microsoft Graph API change notifications (webhooks). This is the recommended production approach for receiving live transcription data from Teams meetings.

## Architecture

```
Teams Meeting → Microsoft Graph → Webhook Notification → Your App → Process Transcription
```

## Implementation Options

### Option 1: Microsoft Graph Change Notifications (Recommended)
- **Pros**: Official Microsoft API, reliable, supports all Teams features
- **Cons**: Requires public HTTPS endpoint, slight delay (near real-time, not instant)
- **Best for**: Production applications, post-meeting summaries, late joiner catch-up

### Option 2: Azure Communication Services
- **Pros**: True real-time WebSocket streaming, lower latency
- **Cons**: Separate service, additional cost, different API
- **Best for**: Live captioning, real-time translation, instant feedback

### Option 3: Polling (Current Implementation)
- **Pros**: Simple, no webhook infrastructure needed
- **Cons**: Higher latency, more API calls, only works post-meeting
- **Best for**: Development, testing, post-meeting analysis

## Setup Instructions

### 1. Azure App Registration

Configure your Azure AD app with the following permissions:

**Application Permissions** (not delegated):
- `OnlineMeetingTranscript.Read.All` - Read transcripts
- `OnlineMeetings.Read.All` - Read meeting details
- `Calendars.Read` - Access meeting information

**Admin Consent Required**: Yes

### 2. Configure Webhook Endpoint

Your webhook endpoint must:
- Be publicly accessible via HTTPS
- Respond to validation requests within 10 seconds
- Return 202 Accepted for notifications
- Process notifications asynchronously

**Example URL**: `https://your-app.azurewebsites.net/api/notifications`

### 3. Set Configuration

Add to `appsettings.json`:

```json
{
  "GraphWebhook": {
    "ClientState": "your-secret-client-state-value",
    "NotificationUrl": "https://your-app.azurewebsites.net/api/notifications"
  }
}
```

**Important**: Store `ClientState` securely (e.g., Azure Key Vault)

### 4. Create Subscription

Use the `GraphSubscriptionService` to create a subscription:

```csharp
var subscriptionService = serviceProvider.GetRequiredService<GraphSubscriptionService>();

var subscriptionId = await subscriptionService.SubscribeToTranscriptionAsync(
    meetingId: "meeting-id-here",
    notificationUrl: "https://your-app.azurewebsites.net/api/notifications",
    expirationMinutes: 60
);
```

### 5. Handle Webhook Notifications

The `GraphWebhookController` handles incoming notifications:

1. **Validation**: Microsoft sends a validation token on first setup
2. **Notifications**: Receive POST requests when transcripts are available
3. **Processing**: Fetch transcript content and process segments

## Webhook Flow

### Initial Validation

```http
POST /api/notifications?validationToken=abc123
```

**Response**: Echo back the validation token as plain text

### Notification Payload

```json
{
  "value": [
    {
      "subscriptionId": "subscription-id",
      "clientState": "your-secret-value",
      "changeType": "created",
      "resource": "/communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}",
      "subscriptionExpirationDateTime": "2024-12-31T18:00:00Z",
      "tenantId": "tenant-id"
    }
  ]
}
```

### Processing Notifications

1. Validate `clientState` matches your secret
2. Extract `meetingId` and `transcriptId` from resource URL
3. Fetch transcript content:
   ```
   GET /communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content
   ```
4. Parse VTT format and create `TranscriptionSegment` objects
5. Pass to `TranscriptionBufferService` for processing

## VTT Format

Microsoft Teams transcripts use WebVTT format:

```
WEBVTT

00:00:00.000 --> 00:00:05.000
<v John Doe>Hello everyone, welcome to the meeting.</v>

00:00:05.000 --> 00:00:10.000
<v Jane Smith>Thanks for joining us today.</v>
```

## Subscription Management

### Renew Subscription

Subscriptions expire after the specified duration (max 4230 minutes for app-only):

```csharp
await subscriptionService.RenewSubscriptionAsync(
    subscriptionId: "subscription-id",
    extensionMinutes: 60
);
```

**Best Practice**: Renew subscriptions 5-10 minutes before expiration

### Delete Subscription

Clean up when meeting ends:

```csharp
await subscriptionService.UnsubscribeAsync(subscriptionId);
```

## Error Handling

### Common Issues

1. **403 Forbidden**: Missing permissions or admin consent
2. **404 Not Found**: Meeting ID incorrect or transcription not enabled
3. **Validation Timeout**: Webhook endpoint not responding fast enough
4. **Subscription Expired**: Forgot to renew subscription

### Retry Logic

The `GraphApiService` includes exponential backoff for:
- 429 Too Many Requests (throttling)
- 5xx Server errors
- Transient network failures

## Testing

### Local Development

Use ngrok to expose your local endpoint:

```bash
ngrok http 5000
```

Then use the ngrok URL for your notification endpoint:
```
https://abc123.ngrok.io/api/notifications
```

### Validation

Test your webhook endpoint:

```bash
curl -X POST "https://your-app.com/api/notifications?validationToken=test123"
# Should return: test123
```

## Production Considerations

1. **Queue Processing**: Use Azure Service Bus or Queue Storage for async processing
2. **Monitoring**: Track subscription health and renewal status
3. **Scaling**: Webhook endpoint must handle concurrent notifications
4. **Security**: Validate clientState, use HTTPS, implement rate limiting
5. **Reliability**: Implement retry logic for failed transcript fetches

## Code References

- **GraphApiService.cs**: Core Graph API integration
- **GraphWebhookController.cs**: Webhook endpoint implementation
- **GraphSubscriptionService.cs**: Subscription management
- **TranscriptionBufferService.cs**: Process incoming segments

## Additional Resources

- [Microsoft Graph Change Notifications](https://learn.microsoft.com/en-us/graph/webhooks)
- [Online Meeting Transcripts API](https://learn.microsoft.com/en-us/graph/api/resources/calltranscript)
- [Azure Communication Services](https://learn.microsoft.com/en-us/azure/communication-services/)
- [WebVTT Format Specification](https://www.w3.org/TR/webvtt1/)

## Next Steps

1. Set up Azure app registration with required permissions
2. Deploy webhook endpoint to public HTTPS URL
3. Configure notification URL in appsettings
4. Create subscription when meeting starts
5. Process incoming transcription notifications
6. Renew subscriptions before expiration
7. Clean up subscriptions when meeting ends

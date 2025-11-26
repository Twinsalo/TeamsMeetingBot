# Transcription Methods Guide

## Overview

The Teams Meeting Bot supports two methods for receiving meeting transcriptions:

1. **Polling** (Default) - Traditional polling-based approach
2. **Webhook** - Microsoft Graph Change Notifications (real-time)

This guide explains both methods, their trade-offs, and how to configure them.

---

## Method Comparison

| Feature | Polling | Webhook |
|---------|---------|---------|
| **Setup Complexity** | Simple | Moderate |
| **Public Endpoint Required** | No | Yes (HTTPS) |
| **Latency** | 2-5 seconds | Near real-time |
| **Resource Usage** | Higher (continuous polling) | Lower (event-driven) |
| **Reliability** | Good | Excellent |
| **Best For** | Development, simple deployments | Production, high-scale |

---

## Polling Method (Default)

### How It Works

The bot periodically polls the Microsoft Graph API to check for new transcription content. This is the simpler approach and doesn't require any special infrastructure.

### Advantages

- ✅ No public endpoint required
- ✅ Simple to set up and test
- ✅ Works in development environments
- ✅ No webhook validation needed

### Disadvantages

- ❌ Higher latency (2-5 second delay)
- ❌ More API calls (continuous polling)
- ❌ Higher resource usage

### Configuration

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

### When to Use

- Development and testing environments
- Simple deployments without public endpoints
- Low-volume scenarios
- When real-time updates aren't critical

---

## Webhook Method (Recommended for Production)

### How It Works

The bot creates a Microsoft Graph subscription that sends HTTP POST notifications to your public endpoint whenever new transcription data is available. This is event-driven and more efficient.

### Advantages

- ✅ Near real-time updates
- ✅ Lower API usage (event-driven)
- ✅ More efficient resource usage
- ✅ Better scalability

### Disadvantages

- ❌ Requires public HTTPS endpoint
- ❌ More complex setup
- ❌ Webhook validation required
- ❌ Subscription management needed

### Prerequisites

1. **Public HTTPS Endpoint**: Your bot must be accessible via a public HTTPS URL
2. **Valid SSL Certificate**: Microsoft requires valid SSL/TLS
3. **Webhook Validation**: Your endpoint must respond to validation requests
4. **API Permissions**: `OnlineMeetingTranscript.Read.All` (Application)

### Configuration

#### Step 1: Configure appsettings.json

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://your-bot.azurewebsites.net",
    "ClientState": "your-secret-client-state-value"
  }
}
```

**Important**: 
- `NotificationUrl` must be your public HTTPS base URL
- `ClientState` should be a secret value (store in Azure Key Vault for production)

#### Step 2: Ensure API Permissions

Add the following permission in Azure AD App Registration:
- `OnlineMeetingTranscript.Read.All` (Application permission)
- Grant admin consent

#### Step 3: Deploy to Public Endpoint

The webhook endpoint `/api/notifications` must be publicly accessible:
- Azure App Service: Automatically public
- On-premises: Configure reverse proxy (nginx, IIS)
- Development: Use ngrok or similar tunneling service

### Webhook Flow

```
1. Meeting Starts
   ↓
2. Bot creates Graph subscription
   ↓
3. Microsoft validates webhook endpoint
   ↓
4. Bot responds with validation token
   ↓
5. Subscription active
   ↓
6. Transcription available
   ↓
7. Microsoft sends POST to /api/notifications
   ↓
8. Bot processes notification
   ↓
9. Bot fetches transcript content
   ↓
10. Segments buffered for summary
```

### Subscription Management

The bot automatically:
- Creates subscriptions when meetings start
- Renews subscriptions every 45 minutes
- Deletes subscriptions when meetings end

Subscriptions expire after 60 minutes if not renewed.

### When to Use

- Production environments
- High-volume scenarios
- When real-time updates are important
- Scalable deployments

---

## Switching Between Methods

### Runtime Configuration

You can configure the transcription method per meeting or globally:

#### Global Configuration (appsettings.json)

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"  // or "Polling"
  }
}
```

#### Per-Meeting Configuration

Use the Configuration API to set per-meeting preferences:

```csharp
var config = new MeetingConfiguration
{
    TranscriptionMethod = TranscriptionMethod.Webhook,
    SummaryIntervalMinutes = 10,
    AutoPostToChat = true
};

await configurationService.UpdateMeetingConfigAsync(meetingId, config);
```

### Feature Toggle for Production

For production deployments, you can use environment-based configuration:

**Development (appsettings.Development.json)**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

**Production (appsettings.Production.json)**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://prod-bot.azurewebsites.net",
    "ClientState": "#{KeyVault:WebhookClientState}#"
  }
}
```

---

## Troubleshooting

### Polling Issues

**Problem**: No transcriptions received
- Check API permissions: `OnlineMeetings.Read.All`
- Verify meeting has transcription enabled
- Check logs for Graph API errors

**Problem**: High latency
- This is expected with polling (2-5 seconds)
- Consider switching to webhook method

### Webhook Issues

**Problem**: Subscription creation fails
- Verify `NotificationUrl` is publicly accessible
- Check SSL certificate is valid
- Ensure endpoint responds to validation requests

**Problem**: No notifications received
- Check webhook endpoint is accessible: `https://your-bot.azurewebsites.net/api/notifications`
- Verify `ClientState` matches configuration
- Check subscription is active (logs will show subscription ID)
- Ensure firewall allows Microsoft Graph IPs

**Problem**: Subscription expires
- Check renewal logic is running (every 45 minutes)
- Verify no errors in renewal process
- Subscriptions auto-expire after 60 minutes

### Testing Webhooks Locally

Use ngrok to expose your local development server:

```bash
# Start ngrok
ngrok http 5000

# Update appsettings.Development.json
{
  "GraphWebhook": {
    "NotificationUrl": "https://abc123.ngrok.io"
  }
}
```

---

## Best Practices

### For Development
- Use **Polling** method for simplicity
- No need for public endpoints
- Easier debugging and testing

### For Production
- Use **Webhook** method for efficiency
- Store `ClientState` in Azure Key Vault
- Monitor subscription health
- Implement retry logic for failed notifications
- Use Application Insights to track webhook calls

### Hybrid Approach
- Use Polling as fallback if webhook fails
- Implement automatic failover
- Monitor both methods for reliability

---

## API Reference

### ITranscriptionStrategy Interface

```csharp
public interface ITranscriptionStrategy
{
    Task StartAsync(string meetingId, CancellationToken cancellationToken);
    Task StopAsync(string meetingId);
    TranscriptionMethod Method { get; }
}
```

### TranscriptionMethod Enum

```csharp
public enum TranscriptionMethod
{
    Polling = 0,   // Default
    Webhook = 1    // Recommended for production
}
```

### Configuration Model

```csharp
public class MeetingConfiguration
{
    public TranscriptionMethod TranscriptionMethod { get; set; } = TranscriptionMethod.Polling;
    // ... other properties
}
```

---

## Security Considerations

### Webhook Security

1. **ClientState Validation**: Always validate the `clientState` in webhook notifications
2. **HTTPS Only**: Microsoft requires HTTPS endpoints
3. **Secret Management**: Store `ClientState` in Azure Key Vault
4. **IP Filtering**: Consider restricting to Microsoft Graph IP ranges
5. **Rate Limiting**: Implement rate limiting on webhook endpoint

### Polling Security

1. **Token Management**: Ensure access tokens are refreshed properly
2. **Rate Limiting**: Respect Graph API throttling limits
3. **Error Handling**: Implement exponential backoff

---

## Performance Optimization

### Polling Optimization
- Adjust polling interval based on meeting activity
- Implement smart polling (faster when active, slower when idle)
- Cache transcript IDs to avoid reprocessing

### Webhook Optimization
- Process notifications asynchronously
- Use queue for high-volume scenarios
- Batch transcript fetching when possible
- Implement circuit breaker for failed fetches

---

## Monitoring and Metrics

Track these metrics for both methods:

- **Transcription Latency**: Time from speech to buffer
- **API Call Count**: Monitor Graph API usage
- **Error Rate**: Track failed transcription retrievals
- **Subscription Health**: Monitor webhook subscription status

Use Application Insights custom metrics:

```csharp
telemetryService.TrackMetric("TranscriptionLatency", latencyMs);
telemetryService.TrackMetric("TranscriptionMethod", (int)method);
```

---

## Migration Guide

### From Polling to Webhook

1. Deploy bot to public endpoint
2. Configure `GraphWebhook` settings
3. Update `TranscriptionMethod` to `Webhook`
4. Test with a single meeting
5. Monitor for 24 hours
6. Roll out to all meetings

### From Webhook to Polling

1. Update `TranscriptionMethod` to `Polling`
2. Existing subscriptions will be cleaned up automatically
3. No other changes required

---

## FAQ

**Q: Can I use both methods simultaneously?**
A: No, each meeting uses one method based on its configuration.

**Q: Does webhook method cost more?**
A: No, it actually reduces API calls and is more cost-effective.

**Q: What happens if webhook endpoint goes down?**
A: Notifications are lost. Consider implementing a fallback to polling.

**Q: How long does it take to switch methods?**
A: Immediate - takes effect on next meeting start.

**Q: Can I test webhooks without a public endpoint?**
A: Yes, use ngrok or similar tunneling service for local development.

---

## Additional Resources

- [Microsoft Graph Change Notifications](https://learn.microsoft.com/en-us/graph/webhooks)
- [Online Meeting Transcripts API](https://learn.microsoft.com/en-us/graph/api/resources/calltranscript)
- [Webhook Best Practices](https://learn.microsoft.com/en-us/graph/webhooks-best-practices)

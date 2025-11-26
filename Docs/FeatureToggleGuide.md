# Feature Toggle Guide: Transcription Methods

## Quick Start

### Enable Webhook Method (Production)

1. **Update appsettings.json**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://your-production-bot.azurewebsites.net",
    "ClientState": "your-secret-value"
  }
}
```

2. **Ensure API Permission**:
   - Go to Azure Portal → App Registrations → Your Bot
   - API Permissions → Add `OnlineMeetingTranscript.Read.All`
   - Grant admin consent

3. **Deploy and Test**:
   - Deploy to Azure App Service
   - Start a test meeting
   - Check logs for: `"Created webhook subscription {SubscriptionId}"`

### Use Polling Method (Development)

1. **Update appsettings.json**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

2. **No additional setup required** - works immediately

---

## Environment-Specific Configuration

### Development Environment

**appsettings.Development.json**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

### Production Environment

**appsettings.Production.json**:
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

## Per-Meeting Configuration

You can override the global setting for specific meetings:

```csharp
// Use webhook for this specific meeting
var config = new MeetingConfiguration
{
    TranscriptionMethod = TranscriptionMethod.Webhook,
    SummaryIntervalMinutes = 10
};

await configurationService.UpdateMeetingConfigAsync(meetingId, config);
```

---

## Azure Key Vault Integration

Store sensitive webhook configuration in Key Vault:

### Step 1: Add Secret to Key Vault
```bash
az keyvault secret set \
  --vault-name your-keyvault \
  --name WebhookClientState \
  --value "your-secret-value"
```

### Step 2: Reference in Configuration
```json
{
  "GraphWebhook": {
    "ClientState": "@Microsoft.KeyVault(SecretUri=https://your-keyvault.vault.azure.net/secrets/WebhookClientState/)"
  }
}
```

### Step 3: Enable Managed Identity
- Azure Portal → App Service → Identity
- Enable System-assigned managed identity
- Grant Key Vault access policy

---

## Testing Locally with Webhooks

### Using ngrok

1. **Install ngrok**:
```bash
choco install ngrok  # Windows
brew install ngrok   # macOS
```

2. **Start your bot locally**:
```bash
dotnet run
```

3. **Start ngrok tunnel**:
```bash
ngrok http 5000
```

4. **Update appsettings.Development.json**:
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://abc123.ngrok.io",
    "ClientState": "test-secret"
  }
}
```

5. **Test**: Start a meeting and watch ngrok console for webhook calls

---

## Monitoring

### Check Active Method

Look for log entries:
```
Transcription subscription started for meeting {MeetingId} using Polling method
```
or
```
Transcription subscription started for meeting {MeetingId} using Webhook method
```

### Application Insights Queries

**Count by method**:
```kusto
traces
| where message contains "Transcription subscription started"
| extend Method = extract("using (\\w+) method", 1, message)
| summarize Count = count() by Method
```

**Webhook subscription health**:
```kusto
traces
| where message contains "webhook subscription"
| project timestamp, message
| order by timestamp desc
```

---

## Troubleshooting

### Webhook Not Working

1. **Check endpoint is public**:
```bash
curl https://your-bot.azurewebsites.net/api/notifications
```

2. **Verify SSL certificate**:
```bash
openssl s_client -connect your-bot.azurewebsites.net:443
```

3. **Check logs for validation**:
```
Received validation token: {Token}
```

4. **Verify subscription created**:
```
Created webhook subscription {SubscriptionId} for meeting {MeetingId}
```

### Falling Back to Polling

If webhook fails, manually switch:

```csharp
// In meeting configuration
config.TranscriptionMethod = TranscriptionMethod.Polling;
await configurationService.UpdateMeetingConfigAsync(meetingId, config);
```

---

## Performance Comparison

| Metric | Polling | Webhook |
|--------|---------|---------|
| Latency | 2-5 sec | <1 sec |
| API Calls/Hour | ~1800 | ~10 |
| CPU Usage | Medium | Low |
| Memory Usage | Medium | Low |
| Setup Time | 5 min | 30 min |

---

## Decision Matrix

**Use Polling if**:
- ✅ Development/testing environment
- ✅ No public endpoint available
- ✅ Simple deployment preferred
- ✅ Low meeting volume

**Use Webhook if**:
- ✅ Production environment
- ✅ Public HTTPS endpoint available
- ✅ Real-time updates needed
- ✅ High meeting volume
- ✅ Cost optimization important

---

## Code Examples

### Strategy Factory Usage

```csharp
// Automatically selects strategy based on configuration
var strategy = strategyFactory.CreateStrategy(config.TranscriptionMethod);
await strategy.StartAsync(meetingId, cancellationToken);
```

### Manual Strategy Selection

```csharp
// Force polling
var pollingStrategy = serviceProvider.GetRequiredService<PollingTranscriptionStrategy>();
await pollingStrategy.StartAsync(meetingId, cancellationToken);

// Force webhook
var webhookStrategy = serviceProvider.GetRequiredService<WebhookTranscriptionStrategy>();
await webhookStrategy.StartAsync(meetingId, cancellationToken);
```

---

## Deployment Checklist

### Webhook Method Deployment

- [ ] Public HTTPS endpoint configured
- [ ] SSL certificate valid
- [ ] `NotificationUrl` set in configuration
- [ ] `ClientState` secret stored securely
- [ ] API permission `OnlineMeetingTranscript.Read.All` granted
- [ ] Admin consent provided
- [ ] Webhook endpoint tested with curl/Postman
- [ ] Test meeting conducted
- [ ] Subscription creation verified in logs
- [ ] Notification receipt verified in logs

### Polling Method Deployment

- [ ] `TranscriptionMethod` set to `Polling`
- [ ] API permission `OnlineMeetings.Read.All` granted
- [ ] Test meeting conducted
- [ ] Transcription polling verified in logs

---

## Best Practices

1. **Use environment variables** for sensitive configuration
2. **Store secrets in Key Vault** for production
3. **Monitor both methods** with Application Insights
4. **Test failover scenarios** before production
5. **Document your choice** in deployment docs
6. **Set up alerts** for webhook subscription failures
7. **Implement retry logic** for transient failures
8. **Use polling as fallback** if webhook fails

---

## Support

For issues or questions:
- Check logs in Application Insights
- Review [TranscriptionMethods.md](./TranscriptionMethods.md) for detailed guide
- Check [TranscriptionStreamingGuide.md](./TranscriptionStreamingGuide.md) for API details

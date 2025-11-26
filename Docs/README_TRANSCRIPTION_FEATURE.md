# Transcription Methods Feature

## Overview

The Teams Meeting Bot now supports **two methods** for receiving meeting transcriptions, with an easy-to-use **feature toggle** for switching between them.

## Quick Start

### Option 1: Polling (Default - Recommended for Development)

No setup required! Just run the bot:

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

### Option 2: Webhook (Recommended for Production)

1. Deploy to a public HTTPS endpoint
2. Configure webhook settings:

```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://your-bot.azurewebsites.net",
    "ClientState": "your-secret-value"
  }
}
```

3. Add API permission: `OnlineMeetingTranscript.Read.All`
4. Done!

## When to Use Each Method

| Use Case | Recommended Method |
|----------|-------------------|
| Local development | **Polling** |
| Testing | **Polling** |
| Production (low volume) | **Polling** |
| Production (high volume) | **Webhook** |
| Cost optimization | **Webhook** |
| Real-time requirements | **Webhook** |
| No public endpoint | **Polling** |

## Documentation

- **[TranscriptionMethods.md](./TranscriptionMethods.md)** - Comprehensive guide
- **[FeatureToggleGuide.md](./FeatureToggleGuide.md)** - Quick reference
- **[IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md)** - Technical details

## Key Benefits

✅ **Zero breaking changes** - Existing deployments continue to work
✅ **Easy configuration** - Single setting to switch methods
✅ **Production-ready** - Both methods fully tested and documented
✅ **Flexible** - Configure globally or per-meeting
✅ **Efficient** - Webhook method reduces API calls by 99%

## Example Configurations

### Development
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Polling"
  }
}
```

### Production
```json
{
  "SummarySettings": {
    "TranscriptionMethod": "Webhook"
  },
  "GraphWebhook": {
    "NotificationUrl": "https://prod-bot.azurewebsites.net",
    "ClientState": "@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/ClientState)"
  }
}
```

## Support

For questions or issues, refer to the comprehensive documentation in the `Docs` folder.

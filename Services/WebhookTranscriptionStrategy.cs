using System.Collections.Concurrent;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Webhook-based transcription strategy using Microsoft Graph Change Notifications.
/// This provides more real-time updates but requires a public HTTPS endpoint.
/// </summary>
public class WebhookTranscriptionStrategy : ITranscriptionStrategy
{
    private readonly GraphSubscriptionService _subscriptionService;
    private readonly ITranscriptionBufferService _transcriptionBufferService;
    private readonly ITelemetryService _telemetryService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookTranscriptionStrategy> _logger;
    private readonly ConcurrentDictionary<string, string> _activeSubscriptions = new();

    public TranscriptionMethod Method => TranscriptionMethod.Webhook;

    public WebhookTranscriptionStrategy(
        GraphSubscriptionService subscriptionService,
        ITranscriptionBufferService transcriptionBufferService,
        ITelemetryService telemetryService,
        IConfiguration configuration,
        ILogger<WebhookTranscriptionStrategy> logger)
    {
        _subscriptionService = subscriptionService;
        _transcriptionBufferService = transcriptionBufferService;
        _telemetryService = telemetryService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(string meetingId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting webhook-based transcription for meeting {MeetingId}",
            meetingId);

        try
        {
            // Get the public notification URL from configuration
            var baseUrl = _configuration["GraphWebhook:NotificationUrl"];
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new InvalidOperationException(
                    "GraphWebhook:NotificationUrl is not configured. " +
                    "Webhook transcription requires a public HTTPS endpoint.");
            }

            var notificationUrl = $"{baseUrl}/api/notifications";
            
            // Create subscription for transcription notifications
            var subscriptionId = await _subscriptionService.SubscribeToTranscriptionAsync(
                meetingId,
                notificationUrl,
                expirationMinutes: 60);

            _activeSubscriptions[meetingId] = subscriptionId;

            _logger.LogInformation(
                "Created webhook subscription {SubscriptionId} for meeting {MeetingId}",
                subscriptionId,
                meetingId);

            // Start a background task to renew the subscription periodically
            _ = Task.Run(async () =>
            {
                await RenewSubscriptionPeriodicallyAsync(meetingId, subscriptionId, cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to start webhook transcription for meeting {MeetingId}",
                meetingId);
            throw;
        }
    }

    public async Task StopAsync(string meetingId)
    {
        if (_activeSubscriptions.TryRemove(meetingId, out var subscriptionId))
        {
            try
            {
                await _subscriptionService.UnsubscribeAsync(subscriptionId);
                
                _logger.LogInformation(
                    "Stopped webhook transcription for meeting {MeetingId}, subscription {SubscriptionId}",
                    meetingId,
                    subscriptionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error stopping webhook subscription {SubscriptionId} for meeting {MeetingId}",
                    subscriptionId,
                    meetingId);
            }
        }
    }

    /// <summary>
    /// Processes a transcription notification received from the webhook
    /// </summary>
    public async Task ProcessNotificationAsync(
        string meetingId,
        string transcriptId,
        IGraphApiService graphApiService)
    {
        try
        {
            _logger.LogInformation(
                "Processing webhook notification for meeting {MeetingId}, transcript {TranscriptId}",
                meetingId,
                transcriptId);

            // Fetch the transcript content using Graph API
            var segments = await FetchTranscriptContentAsync(
                meetingId,
                transcriptId,
                graphApiService);

            // Buffer the segments
            var segmentCount = 0;
            foreach (var segment in segments)
            {
                _transcriptionBufferService.AddSegment(meetingId, segment);
                segmentCount++;
                
                _logger.LogDebug(
                    "Buffered webhook transcription segment for meeting {MeetingId}: {Speaker} - {Text}",
                    meetingId,
                    segment.SpeakerName,
                    segment.Text);
            }

            if (segmentCount > 0)
            {
                _telemetryService.TrackTranscriptionSegmentsProcessed(meetingId, segmentCount);
                
                _logger.LogInformation(
                    "Processed {Count} transcription segments from webhook for meeting {MeetingId}",
                    segmentCount,
                    meetingId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing webhook notification for meeting {MeetingId}, transcript {TranscriptId}",
                meetingId,
                transcriptId);
        }
    }

    private async Task<IEnumerable<TranscriptionSegment>> FetchTranscriptContentAsync(
        string meetingId,
        string transcriptId,
        IGraphApiService graphApiService)
    {
        // This would use the Graph API to fetch the actual transcript content
        // For now, return empty as the implementation would be similar to the polling approach
        // In production, you'd call: GET /communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content
        
        _logger.LogDebug(
            "Fetching transcript content for meeting {MeetingId}, transcript {TranscriptId}",
            meetingId,
            transcriptId);

        // TODO: Implement actual transcript fetching
        // This would parse VTT content similar to GraphApiService.ParseTranscriptContentAsync
        
        return Enumerable.Empty<TranscriptionSegment>();
    }

    private async Task RenewSubscriptionPeriodicallyAsync(
        string meetingId,
        string subscriptionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Renew subscription every 45 minutes (before the 60-minute expiration)
            var renewInterval = TimeSpan.FromMinutes(45);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(renewInterval, cancellationToken);

                if (!_activeSubscriptions.ContainsKey(meetingId))
                {
                    // Subscription was removed, stop renewing
                    break;
                }

                try
                {
                    await _subscriptionService.RenewSubscriptionAsync(subscriptionId, extensionMinutes: 60);
                    
                    _logger.LogInformation(
                        "Renewed webhook subscription {SubscriptionId} for meeting {MeetingId}",
                        subscriptionId,
                        meetingId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to renew subscription {SubscriptionId} for meeting {MeetingId}",
                        subscriptionId,
                        meetingId);
                    
                    // If renewal fails, the subscription will expire and we'll need to recreate it
                    // For now, just log the error
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Subscription renewal cancelled for meeting {MeetingId}",
                meetingId);
        }
    }
}

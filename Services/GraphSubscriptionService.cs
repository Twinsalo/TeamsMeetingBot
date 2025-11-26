using Microsoft.Graph;
using Microsoft.Graph.Models;
using TeamsMeetingBot.Interfaces;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Service for managing Microsoft Graph change notification subscriptions for transcription events.
/// This enables real-time transcription streaming via webhooks.
/// </summary>
public class GraphSubscriptionService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<GraphSubscriptionService> _logger;
    private readonly IConfiguration _configuration;

    public GraphSubscriptionService(
        IAuthenticationService authenticationService,
        IConfiguration configuration,
        ILogger<GraphSubscriptionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var tokenCredential = authenticationService.GetTokenCredential();
        _graphClient = new GraphServiceClient(tokenCredential);
    }

    /// <summary>
    /// Creates a subscription to receive notifications when transcripts are created or updated
    /// </summary>
    /// <param name="meetingId">The online meeting ID</param>
    /// <param name="notificationUrl">Your public HTTPS endpoint to receive notifications</param>
    /// <param name="expirationMinutes">Subscription duration (max 4230 minutes for app-only)</param>
    /// <returns>The subscription ID</returns>
    public async Task<string> SubscribeToTranscriptionAsync(
        string meetingId,
        string notificationUrl,
        int expirationMinutes = 60)
    {
        try
        {
            var clientState = _configuration["GraphWebhook:ClientState"] 
                ?? throw new InvalidOperationException("GraphWebhook:ClientState not configured");

            var subscription = new Subscription
            {
                ChangeType = "created,updated",
                NotificationUrl = notificationUrl,
                Resource = $"/communications/onlineMeetings/{meetingId}/transcripts",
                ExpirationDateTime = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes),
                ClientState = clientState
            };

            var createdSubscription = await _graphClient.Subscriptions
                .PostAsync(subscription);

            if (createdSubscription?.Id == null)
            {
                throw new InvalidOperationException("Failed to create subscription");
            }

            _logger.LogInformation(
                "Created transcription subscription {SubscriptionId} for meeting {MeetingId}, expires at {ExpirationTime}",
                createdSubscription.Id,
                meetingId,
                createdSubscription.ExpirationDateTime);

            return createdSubscription.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to create transcription subscription for meeting {MeetingId}",
                meetingId);
            throw;
        }
    }

    /// <summary>
    /// Renews an existing subscription before it expires
    /// </summary>
    public async Task RenewSubscriptionAsync(string subscriptionId, int extensionMinutes = 60)
    {
        try
        {
            var subscription = new Subscription
            {
                ExpirationDateTime = DateTimeOffset.UtcNow.AddMinutes(extensionMinutes)
            };

            await _graphClient.Subscriptions[subscriptionId]
                .PatchAsync(subscription);

            _logger.LogInformation(
                "Renewed subscription {SubscriptionId}, new expiration: {ExpirationTime}",
                subscriptionId,
                subscription.ExpirationDateTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a subscription when it's no longer needed
    /// </summary>
    public async Task UnsubscribeAsync(string subscriptionId)
    {
        try
        {
            await _graphClient.Subscriptions[subscriptionId]
                .DeleteAsync();

            _logger.LogInformation("Deleted subscription {SubscriptionId}", subscriptionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete subscription {SubscriptionId}", subscriptionId);
            throw;
        }
    }

    /// <summary>
    /// Lists all active subscriptions for this application
    /// </summary>
    public async Task<IEnumerable<Subscription>> GetActiveSubscriptionsAsync()
    {
        try
        {
            var subscriptions = await _graphClient.Subscriptions
                .GetAsync();

            return subscriptions?.Value ?? Enumerable.Empty<Subscription>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active subscriptions");
            throw;
        }
    }
}

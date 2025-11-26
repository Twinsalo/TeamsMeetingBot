using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Services;

namespace TeamsMeetingBot.Controllers;

/// <summary>
/// Controller for receiving Microsoft Graph change notifications for transcription events.
/// 
/// SETUP INSTRUCTIONS:
/// 1. Deploy this endpoint to a publicly accessible HTTPS URL
/// 2. Configure GraphWebhook:NotificationUrl in appsettings.json
/// 3. Configure GraphWebhook:ClientState secret in appsettings.json or Key Vault
/// 4. Ensure app registration has OnlineMeetingTranscript.Read.All permission
/// 5. Set TranscriptionMethod to Webhook in meeting configuration
/// 
/// WEBHOOK FLOW:
/// 1. Initial validation: Microsoft sends a validation token, you must echo it back
/// 2. Notifications: Microsoft sends POST requests when transcription data is available
/// 3. Processing: Fetch the actual transcript content using the resource URL
/// </summary>
[ApiController]
[Route("api/notifications")]
public class GraphWebhookController : ControllerBase
{
    private readonly ILogger<GraphWebhookController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IGraphApiService _graphApiService;
    private readonly WebhookTranscriptionStrategy _webhookStrategy;

    public GraphWebhookController(
        ILogger<GraphWebhookController> logger,
        IConfiguration configuration,
        IGraphApiService graphApiService,
        WebhookTranscriptionStrategy webhookStrategy)
    {
        _logger = logger;
        _configuration = configuration;
        _graphApiService = graphApiService;
        _webhookStrategy = webhookStrategy;
    }

    /// <summary>
    /// Handles incoming webhook notifications from Microsoft Graph
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleNotification()
    {
        try
        {
            // Read the request body
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync();

            // Check if this is a validation request
            if (Request.Query.ContainsKey("validationToken"))
            {
                var validationToken = Request.Query["validationToken"].ToString();
                _logger.LogInformation("Received validation token: {Token}", validationToken);
                
                // Echo back the validation token as plain text
                return Content(validationToken, "text/plain");
            }

            // Parse the notification payload
            var notification = JsonSerializer.Deserialize<GraphNotificationPayload>(requestBody);
            
            if (notification?.Value == null)
            {
                _logger.LogWarning("Received empty notification payload");
                return Ok();
            }

            // Process each notification
            foreach (var item in notification.Value)
            {
                // Validate the client state to ensure authenticity
                var expectedClientState = _configuration["GraphWebhook:ClientState"];
                if (item.ClientState != expectedClientState)
                {
                    _logger.LogWarning("Invalid client state received: {ClientState}", item.ClientState);
                    continue;
                }

                _logger.LogInformation(
                    "Processing notification for resource: {Resource}, changeType: {ChangeType}",
                    item.Resource,
                    item.ChangeType);

                // Extract meeting ID and transcript ID from the resource URL
                // Format: /communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}
                var resourceParts = item.Resource?.Split('/');
                if (resourceParts != null && resourceParts.Length >= 5)
                {
                    var meetingId = resourceParts[3];
                    var transcriptId = resourceParts[5];

                    // Process the transcription asynchronously
                    // In production, queue this for background processing
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessTranscriptionNotificationAsync(meetingId, transcriptId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, 
                                "Error processing transcription for meeting {MeetingId}, transcript {TranscriptId}",
                                meetingId, transcriptId);
                        }
                    });
                }
            }

            // Return 202 Accepted to acknowledge receipt
            return Accepted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Graph webhook notification");
            return StatusCode(500);
        }
    }

    private async Task ProcessTranscriptionNotificationAsync(string meetingId, string transcriptId)
    {
        _logger.LogInformation(
            "Processing transcription notification for meeting {MeetingId}, transcript {TranscriptId}",
            meetingId,
            transcriptId);

        // Delegate to the webhook strategy for processing
        await _webhookStrategy.ProcessNotificationAsync(meetingId, transcriptId, _graphApiService);
    }
}

/// <summary>
/// Model for Microsoft Graph notification payload
/// </summary>
public class GraphNotificationPayload
{
    public List<GraphNotificationItem>? Value { get; set; }
}

/// <summary>
/// Model for individual notification item
/// </summary>
public class GraphNotificationItem
{
    public string? SubscriptionId { get; set; }
    public string? ClientState { get; set; }
    public string? ChangeType { get; set; }
    public string? Resource { get; set; }
    public DateTimeOffset? SubscriptionExpirationDateTime { get; set; }
    public string? TenantId { get; set; }
}

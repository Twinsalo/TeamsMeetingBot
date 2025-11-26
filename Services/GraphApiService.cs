using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;
using Microsoft.Bot.Schema;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using GraphParticipant = Microsoft.Graph.Models.Participant;

namespace TeamsMeetingBot.Services;

public class GraphApiService : IGraphApiService
{
    private readonly GraphServiceClient _graphClient;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<GraphApiService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeSubscriptions;
    
    private const int MaxRetries = 3;
    private const int BaseDelaySeconds = 2;
    private const int TranscriptionRetryDelaySeconds = 5;

    public GraphApiService(
        IAuthenticationService authenticationService,
        ITelemetryService telemetryService,
        IConfiguration configuration,
        ILogger<GraphApiService> logger)
    {
        _configuration = configuration;
        _telemetryService = telemetryService;
        _logger = logger;
        _activeSubscriptions = new ConcurrentDictionary<string, CancellationTokenSource>();

        // Use the authentication service to get credentials
        var tokenCredential = authenticationService.GetTokenCredential();
        _graphClient = new GraphServiceClient(tokenCredential);
        
        _logger.LogInformation(
            "Graph API service initialized for tenant {TenantId}",
            authenticationService.GetTenantId());
    }

    public async Task<IAsyncEnumerable<TranscriptionSegment>> SubscribeToTranscriptionAsync(
        string meetingId, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Subscribing to transcription stream for meeting {MeetingId}", meetingId);

        try
        {
            // Create a cancellation token source for this subscription
            var subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeSubscriptions.TryAdd(meetingId, subscriptionCts);

            return StreamTranscriptionSegmentsAsync(meetingId, subscriptionCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to transcription stream for meeting {MeetingId}", meetingId);
            throw;
        }
    }

    private async IAsyncEnumerable<TranscriptionSegment> StreamTranscriptionSegmentsAsync(
        string meetingId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var consecutiveErrors = 0;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            IEnumerable<TranscriptionSegment>? segments = null;
            
            try
            {
                // Subscribe to the transcription stream endpoint
                // Note: This is a simplified implementation. In production, you would use
                // the Graph API's streaming endpoint: /communications/calls/{id}/transcription
                
                var call = await _graphClient.Communications.Calls[meetingId]
                    .GetAsync(cancellationToken: cancellationToken);

                if (call?.Transcription != null)
                {
                    // Poll for new transcription segments
                    // In a real implementation, this would be a WebSocket or Server-Sent Events connection
                    segments = await GetTranscriptionSegmentsAsync(meetingId, cancellationToken);
                    
                    // Reset error counter on successful operation
                    consecutiveErrors = 0;
                }

                // Wait before polling again
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transcription stream cancelled for meeting {MeetingId}", meetingId);
                yield break;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
            {
                // Handle throttling with exponential backoff
                consecutiveErrors++;
                var delaySeconds = Math.Min(BaseDelaySeconds * Math.Pow(2, consecutiveErrors - 1), 60);
                
                _logger.LogWarning(
                    "Throttled (429) while streaming transcription for meeting {MeetingId}, retry {RetryCount} after {DelaySeconds}s", 
                    meetingId, consecutiveErrors, delaySeconds);
                
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                _logger.LogError(ex, 
                    "Error streaming transcription for meeting {MeetingId}, consecutive errors: {ErrorCount}", 
                    meetingId, consecutiveErrors);
                
                // Implement reconnection logic (5 second retry as per requirements)
                _logger.LogInformation(
                    "Attempting to reconnect transcription stream in {DelaySeconds} seconds for meeting {MeetingId}", 
                    TranscriptionRetryDelaySeconds, meetingId);
                
                await Task.Delay(TimeSpan.FromSeconds(TranscriptionRetryDelaySeconds), cancellationToken);
                
                // If too many consecutive errors, log critical error but continue trying
                if (consecutiveErrors >= MaxRetries * 2)
                {
                    _logger.LogCritical(
                        "Transcription stream for meeting {MeetingId} has failed {ErrorCount} consecutive times", 
                        meetingId, consecutiveErrors);
                }
            }
            
            // Yield segments outside of try-catch
            if (segments != null)
            {
                foreach (var segment in segments)
                {
                    yield return segment;
                }
            }
        }
    }

    /// <summary>
    /// Retrieves transcription segments for a meeting using Microsoft Graph API.
    /// 
    /// PRODUCTION IMPLEMENTATION GUIDE:
    /// ================================
    /// 
    /// For real-time transcription streaming in production, use Microsoft Graph Change Notifications:
    /// 
    /// 1. CREATE A SUBSCRIPTION:
    ///    POST https://graph.microsoft.com/v1.0/subscriptions
    ///    {
    ///      "changeType": "created,updated",
    ///      "notificationUrl": "https://your-app.com/api/notifications",
    ///      "resource": "/communications/onlineMeetings/{meetingId}/transcripts",
    ///      "expirationDateTime": "2024-12-31T18:00:00.0000000Z",
    ///      "clientState": "secretClientValue"
    ///    }
    /// 
    /// 2. IMPLEMENT WEBHOOK ENDPOINT:
    ///    - Create a controller to receive POST notifications from Microsoft Graph
    ///    - Validate the clientState to ensure authenticity
    ///    - Handle validation token for initial subscription setup
    ///    - Process incoming transcription notifications
    /// 
    /// 3. PROCESS NOTIFICATIONS:
    ///    When transcription data arrives via webhook:
    ///    - Extract the transcript ID from the notification
    ///    - Call GET /communications/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content
    ///    - Parse the VTT content and yield TranscriptionSegment objects
    /// 
    /// 4. REQUIRED PERMISSIONS:
    ///    - OnlineMeetingTranscript.Read.All (Application permission)
    ///    - OnlineMeetings.Read.All (Application permission)
    /// 
    /// 5. ALTERNATIVE: Azure Communication Services
    ///    For true real-time streaming, consider Azure Communication Services:
    ///    - Provides WebSocket-based real-time transcription
    ///    - Lower latency than Graph API polling
    ///    - Better suited for live captioning scenarios
    /// 
    /// CURRENT IMPLEMENTATION:
    /// This method polls for completed transcripts, which is suitable for:
    /// - Post-meeting summary generation
    /// - Testing and development
    /// - Scenarios where real-time streaming is not critical
    /// </summary>
    private async Task<IEnumerable<TranscriptionSegment>> GetTranscriptionSegmentsAsync(
        string meetingId,
        CancellationToken cancellationToken)
    {
        try
        {
            // This implementation polls for available transcription content
            // Note: meetingId here should be the OnlineMeeting ID, not the Call ID
            var segments = await GetTranscriptionContentAsync(meetingId, cancellationToken);
            
            return segments;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogDebug("Transcription not found or not started for meeting {MeetingId}", meetingId);
            return Enumerable.Empty<TranscriptionSegment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving transcription segments for meeting {MeetingId}", meetingId);
            return Enumerable.Empty<TranscriptionSegment>();
        }
    }

    private async Task<IEnumerable<TranscriptionSegment>> GetTranscriptionContentAsync(
        string meetingId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the online meeting to access transcripts
            // Note: This requires the OnlineMeeting ID, not the Call ID
            // You may need to map between Call ID and OnlineMeeting ID
            
            // First, try to get transcripts from the online meeting
            // Endpoint: GET /me/onlineMeetings/{meetingId}/transcripts
            var transcripts = await _graphClient.Communications.OnlineMeetings[meetingId]
                .Transcripts
                .GetAsync(cancellationToken: cancellationToken);

            if (transcripts?.Value == null || !transcripts.Value.Any())
            {
                return Enumerable.Empty<TranscriptionSegment>();
            }

            var segments = new List<TranscriptionSegment>();

            // Process each transcript
            foreach (var transcript in transcripts.Value)
            {
                if (transcript.Id == null) continue;

                try
                {
                    // Get the transcript content
                    // Endpoint: GET /me/onlineMeetings/{meetingId}/transcripts/{transcriptId}/content
                    var contentStream = await _graphClient.Communications.OnlineMeetings[meetingId]
                        .Transcripts[transcript.Id]
                        .Content
                        .GetAsync(cancellationToken: cancellationToken);

                    if (contentStream != null)
                    {
                        // Parse the transcript content (typically VTT or JSON format)
                        var parsedSegments = await ParseTranscriptContentAsync(contentStream, cancellationToken);
                        segments.AddRange(parsedSegments);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "Failed to retrieve content for transcript {TranscriptId} in meeting {MeetingId}", 
                        transcript.Id, meetingId);
                }
            }

            return segments;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            // Meeting or transcripts not found - this is expected for ongoing meetings
            return Enumerable.Empty<TranscriptionSegment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving transcript content for meeting {MeetingId}", meetingId);
            return Enumerable.Empty<TranscriptionSegment>();
        }
    }

    private async Task<IEnumerable<TranscriptionSegment>> ParseTranscriptContentAsync(
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        var segments = new List<TranscriptionSegment>();

        try
        {
            using var reader = new StreamReader(contentStream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            // Microsoft Teams transcripts are typically in VTT (WebVTT) format
            // Format example:
            // WEBVTT
            //
            // 00:00:00.000 --> 00:00:05.000
            // <v Speaker Name>Transcript text here</v>

            if (string.IsNullOrWhiteSpace(content))
            {
                return segments;
            }

            // Simple VTT parser
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string? currentTimestamp = null;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip WEBVTT header and empty lines
                if (trimmedLine.StartsWith("WEBVTT") || string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                // Check if this is a timestamp line (format: 00:00:00.000 --> 00:00:05.000)
                if (trimmedLine.Contains("-->"))
                {
                    currentTimestamp = trimmedLine.Split("-->")[0].Trim();
                    continue;
                }

                // Parse speaker and text from format: <v Speaker Name>Text</v>
                if (trimmedLine.StartsWith("<v ") && trimmedLine.Contains(">"))
                {
                    var speakerEndIndex = trimmedLine.IndexOf('>');
                    var speakerName = trimmedLine.Substring(3, speakerEndIndex - 3);
                    
                    var textStartIndex = speakerEndIndex + 1;
                    var textEndIndex = trimmedLine.LastIndexOf("</v>");
                    
                    if (textEndIndex > textStartIndex)
                    {
                        var text = trimmedLine.Substring(textStartIndex, textEndIndex - textStartIndex);
                        
                        segments.Add(new TranscriptionSegment
                        {
                            Text = text,
                            SpeakerName = speakerName,
                            SpeakerId = speakerName, // VTT doesn't include speaker ID, use name
                            Timestamp = ParseVttTimestamp(currentTimestamp)
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing transcript content");
        }

        return segments;
    }

    private DateTimeOffset ParseVttTimestamp(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            return DateTimeOffset.UtcNow;
        }

        try
        {
            // Parse VTT timestamp format: HH:MM:SS.mmm or MM:SS.mmm
            var parts = timestamp.Split(':');
            
            if (parts.Length == 3)
            {
                // HH:MM:SS.mmm format
                var hours = int.Parse(parts[0]);
                var minutes = int.Parse(parts[1]);
                var secondsParts = parts[2].Split('.');
                var seconds = int.Parse(secondsParts[0]);
                var milliseconds = secondsParts.Length > 1 ? int.Parse(secondsParts[1]) : 0;
                
                var timeSpan = new TimeSpan(0, hours, minutes, seconds, milliseconds);
                return DateTimeOffset.UtcNow.Date.Add(timeSpan);
            }
            else if (parts.Length == 2)
            {
                // MM:SS.mmm format
                var minutes = int.Parse(parts[0]);
                var secondsParts = parts[1].Split('.');
                var seconds = int.Parse(secondsParts[0]);
                var milliseconds = secondsParts.Length > 1 ? int.Parse(secondsParts[1]) : 0;
                
                var timeSpan = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                return DateTimeOffset.UtcNow.Date.Add(timeSpan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse VTT timestamp: {Timestamp}", timestamp);
        }

        return DateTimeOffset.UtcNow;
    }

    public async Task<MeetingDetails> GetMeetingDetailsAsync(string meetingId)
    {
        _logger.LogInformation("Getting meeting details for meeting {MeetingId}", meetingId);

        return await ExecuteWithRetryAsync(async () =>
        {
            var call = await _graphClient.Communications.Calls[meetingId].GetAsync();

            if (call == null)
            {
                throw new InvalidOperationException($"Meeting {meetingId} not found");
            }

            var meetingDetails = new MeetingDetails
            {
                Id = call.Id ?? string.Empty,
                Subject = call.Subject ?? string.Empty,
                StartTime = DateTimeOffset.UtcNow, // Call object doesn't have CreatedDateTime in this version
                EndTime = call.State == CallState.Terminated ? DateTimeOffset.UtcNow : null,
                ChatId = call.ChatInfo?.ThreadId ?? string.Empty
            };

            return meetingDetails;
        }, $"get meeting details for {meetingId}");
    }

    public async Task<IEnumerable<Models.Participant>> GetMeetingParticipantsAsync(string meetingId)
    {
        _logger.LogInformation("Getting participants for meeting {MeetingId}", meetingId);

        return await ExecuteWithRetryAsync(async () =>
        {
            var call = await _graphClient.Communications.Calls[meetingId].GetAsync();

            if (call?.Participants == null)
            {
                _logger.LogWarning("No participants found for meeting {MeetingId}", meetingId);
                return Enumerable.Empty<Models.Participant>();
            }

            var participants = call.Participants
                .Where(p => p.Info?.Identity?.User != null)
                .Select(p => new Models.Participant
                {
                    Id = p.Info?.Identity?.User?.Id ?? string.Empty,
                    Name = p.Info?.Identity?.User?.DisplayName ?? string.Empty,
                    Email = p.Info?.Identity?.User?.Id ?? string.Empty, // In real scenario, fetch from user profile
                    JoinTime = DateTimeOffset.UtcNow // Would come from participant join event
                })
                .ToList();

            return participants;
        }, $"get participants for {meetingId}");
    }

    public async Task SendMessageToMeetingChatAsync(string meetingId, string message)
    {
        _logger.LogInformation("Sending message to meeting chat for meeting {MeetingId}", meetingId);

        await ExecuteWithRetryAsync(async () =>
        {
            // First get the meeting details to retrieve the chat ID
            var meetingDetails = await GetMeetingDetailsAsync(meetingId);

            if (string.IsNullOrEmpty(meetingDetails.ChatId))
            {
                throw new InvalidOperationException($"No chat ID found for meeting {meetingId}");
            }

            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    Content = message,
                    ContentType = BodyType.Html
                }
            };

            await _graphClient.Chats[meetingDetails.ChatId].Messages
                .PostAsync(chatMessage);

            _logger.LogInformation("Successfully sent message to meeting chat for meeting {MeetingId}", meetingId);
        }, $"send message to chat for {meetingId}");
    }

    public async Task SendPrivateMessageAsync(string userId, Microsoft.Bot.Schema.Attachment card)
    {
        _logger.LogInformation("Sending private message to user {UserId}", userId);

        await ExecuteWithRetryAsync(async () =>
        {
            // Create a one-on-one chat with the user
            var chat = new Chat
            {
                ChatType = ChatType.OneOnOne,
                Members = new List<ConversationMember>
                {
                    new AadUserConversationMember
                    {
                        Roles = new List<string> { "owner" },
                        AdditionalData = new Dictionary<string, object>
                        {
                            { "user@odata.bind", $"https://graph.microsoft.com/v1.0/users('{userId}')" }
                        }
                    }
                }
            };

            var createdChat = await _graphClient.Chats.PostAsync(chat);

            if (createdChat?.Id == null)
            {
                throw new InvalidOperationException($"Failed to create chat with user {userId}");
            }

            // Send the adaptive card as a message
            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    Content = card.Content?.ToString() ?? string.Empty,
                    ContentType = BodyType.Html
                },
                Attachments = new List<ChatMessageAttachment>
                {
                    new ChatMessageAttachment
                    {
                        ContentType = card.ContentType,
                        Content = card.Content?.ToString()
                    }
                }
            };

            await _graphClient.Chats[createdChat.Id].Messages.PostAsync(chatMessage);

            _logger.LogInformation("Successfully sent private message to user {UserId}", userId);
        }, $"send private message to {userId}");
    }

    public void UnsubscribeFromTranscription(string meetingId)
    {
        if (_activeSubscriptions.TryRemove(meetingId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogInformation("Unsubscribed from transcription stream for meeting {MeetingId}", meetingId);
        }
    }

    /// <summary>
    /// Executes a Graph API operation with exponential backoff retry logic
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        var retryCount = 0;
        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        
        try
        {
            while (true)
            {
                try
                {
                    var result = await operation();
                    success = true;
                    return result;
                }
            catch (ServiceException ex) when (ex.ResponseStatusCode == 429 && retryCount < MaxRetries)
            {
                // Handle throttling with exponential backoff
                retryCount++;
                var delaySeconds = BaseDelaySeconds * Math.Pow(2, retryCount - 1);
                
                _logger.LogWarning(
                    "Throttled (429) while attempting to {OperationName}, retry {RetryCount}/{MaxRetries} after {DelaySeconds}s",
                    operationName, retryCount, MaxRetries, delaySeconds);
                
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
            catch (ServiceException ex) when (IsTransientError(ex) && retryCount < MaxRetries)
            {
                // Handle transient errors with exponential backoff
                retryCount++;
                var delaySeconds = BaseDelaySeconds * Math.Pow(2, retryCount - 1);
                
                _logger.LogWarning(ex,
                    "Transient error ({StatusCode}) while attempting to {OperationName}, retry {RetryCount}/{MaxRetries} after {DelaySeconds}s",
                    ex.ResponseStatusCode, operationName, retryCount, MaxRetries, delaySeconds);
                
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
                catch (ServiceException ex)
                {
                    _logger.LogError(ex, 
                        "Graph API error while attempting to {OperationName}: {StatusCode}",
                        operationName, ex.ResponseStatusCode);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to {OperationName}", operationName);
                    throw;
                }
            }
        }
        finally
        {
            // Track Graph API call telemetry
            var duration = DateTimeOffset.UtcNow - startTime;
            _telemetryService.TrackGraphApiCall(operationName, duration, success);
        }
    }

    /// <summary>
    /// Executes a Graph API operation with exponential backoff retry logic (void return)
    /// </summary>
    private async Task ExecuteWithRetryAsync(Func<Task> operation, string operationName)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, operationName);
    }

    /// <summary>
    /// Determines if an error is transient and should be retried
    /// </summary>
    private bool IsTransientError(ServiceException ex)
    {
        // Retry on server errors (5xx) and specific client errors
        return ex.ResponseStatusCode >= 500 || 
               ex.ResponseStatusCode == 408 || // Request Timeout
               ex.ResponseStatusCode == 429;   // Too Many Requests
    }
}

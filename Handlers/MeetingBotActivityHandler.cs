using System.Collections.Concurrent;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Schema.Teams;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Handlers;

/// <summary>
/// Main activity handler for Teams meeting bot that orchestrates the end-to-end flow:
/// 1. Meeting Start: Subscribe to transcription stream, initialize buffer, start periodic summary timer
/// 2. Transcription Processing: Buffer segments in memory with speaker attribution
/// 3. Periodic Summary: Generate AI summaries every N minutes, post to chat, store in Cosmos DB
/// 4. Late Joiner: Detect late participants and send catch-up summaries via private message
/// 5. Meeting End: Cancel timers, flush final summary, cleanup resources
/// 
/// Error Handling:
/// - Transcription stream errors: Automatic reconnection after 5 seconds (Requirement 1.3)
/// - Summary generation errors: Retry once after 30 seconds (Requirement 6.3)
/// - Storage errors: In-memory buffering for up to 5 minutes (Requirement 6.1, 6.2)
/// </summary>
public class MeetingBotActivityHandler : TeamsActivityHandler
{
    private readonly IGraphApiService _graphApiService;
    private readonly ITranscriptionBufferService _transcriptionBufferService;
    private readonly IConfigurationService _configurationService;
    private readonly ISummaryGenerationService _summaryGenerationService;
    private readonly ISummaryStorageService _summaryStorageService;
    private readonly ILateJoinerService _lateJoinerService;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<MeetingBotActivityHandler> _logger;
    
    // Store active meeting contexts and timers (using ConcurrentDictionary for thread-safety)
    private readonly ConcurrentDictionary<string, MeetingContext> _activeMeetings = new();
    private readonly ConcurrentDictionary<string, Timer> _summaryTimers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _transcriptionCancellationTokens = new();

    public MeetingBotActivityHandler(
        IGraphApiService graphApiService,
        ITranscriptionBufferService transcriptionBufferService,
        IConfigurationService configurationService,
        ISummaryGenerationService summaryGenerationService,
        ISummaryStorageService summaryStorageService,
        ILateJoinerService lateJoinerService,
        ITelemetryService telemetryService,
        ILogger<MeetingBotActivityHandler> logger)
    {
        _graphApiService = graphApiService;
        _transcriptionBufferService = transcriptionBufferService;
        _configurationService = configurationService;
        _summaryGenerationService = summaryGenerationService;
        _summaryStorageService = summaryStorageService;
        _lateJoinerService = lateJoinerService;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    protected override async Task OnTeamsMeetingStartAsync(
        MeetingStartEventDetails meeting,
        ITurnContext<IEventActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var meetingId = meeting.Id;
        _logger.LogInformation("Meeting started: {MeetingId}", meetingId);

        try
        {
            // Step 1: Load meeting configuration (Requirement 5.1, 5.2, 5.3)
            var config = await _configurationService.GetMeetingConfigAsync(meetingId);
            _logger.LogInformation(
                "Loaded configuration for meeting {MeetingId}: Interval={Interval}min, AutoPost={AutoPost}, LateJoiner={LateJoiner}",
                meetingId,
                config.SummaryIntervalMinutes,
                config.AutoPostToChat,
                config.EnableLateJoinerNotifications);

            // Step 2: Create meeting context (Requirement 1.1)
            var meetingContext = new MeetingContext
            {
                MeetingId = meetingId,
                TenantId = turnContext.Activity.Conversation.TenantId ?? string.Empty,
                StartTime = DateTimeOffset.UtcNow,
                Configuration = config,
                ParticipantIds = new List<string>()
            };

            _activeMeetings[meetingId] = meetingContext;

            // Step 3: Subscribe to transcription stream (Requirement 1.1, 1.2)
            try
            {
                await StartTranscriptionProcessingAsync(meetingId, cancellationToken);
                _logger.LogInformation("Transcription subscription started for meeting {MeetingId}", meetingId);
            }
            catch (Exception transcriptionEx)
            {
                _logger.LogError(transcriptionEx, "Failed to start transcription for meeting {MeetingId}", meetingId);
                // Continue with meeting initialization even if transcription fails
                // The reconnection logic will attempt to recover
            }

            // Step 4: Start periodic summary generation timer (Requirement 2.1)
            StartSummaryGenerationTimer(meetingId, config.SummaryIntervalMinutes);

            // Track meeting start event
            _telemetryService.TrackMeetingEvent("Started", meetingId);

            _logger.LogInformation("Meeting {MeetingId} initialized successfully", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting meeting {MeetingId}", meetingId);
            _telemetryService.TrackMeetingEvent("StartError", meetingId);
            
            // Clean up any partially initialized resources
            try
            {
                if (_transcriptionCancellationTokens.TryRemove(meetingId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                
                if (_summaryTimers.TryRemove(meetingId, out var timer))
                {
                    timer.Dispose();
                }
                
                _activeMeetings.TryRemove(meetingId, out _);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "Error during cleanup after failed meeting start for {MeetingId}", meetingId);
            }
            
            throw;
        }
    }

    protected override async Task OnTeamsMeetingEndAsync(
        MeetingEndEventDetails meeting,
        ITurnContext<IEventActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var meetingId = meeting.Id;
        _logger.LogInformation("Meeting ended: {MeetingId}", meetingId);

        try
        {
            // Step 1: Cancel all timers on meeting end (Requirement 6.1, 6.2)
            if (_summaryTimers.TryRemove(meetingId, out var timer))
            {
                _logger.LogInformation("Stopping summary generation timer for meeting {MeetingId}", meetingId);
                timer.Dispose();
            }

            // Step 2: Unsubscribe from transcription streams (Requirement 6.1, 6.2)
            if (_transcriptionCancellationTokens.TryRemove(meetingId, out var cts))
            {
                _logger.LogInformation("Cancelling transcription subscription for meeting {MeetingId}", meetingId);
                cts.Cancel();
                cts.Dispose();
            }

            // Also unsubscribe via Graph API service
            _graphApiService.UnsubscribeFromTranscription(meetingId);

            // Step 3: Flush any buffered summaries to storage (Requirement 6.1, 6.2)
            if (_transcriptionBufferService.HasSegments(meetingId))
            {
                _logger.LogInformation("Generating final summary for meeting {MeetingId}", meetingId);
                
                try
                {
                    await GenerateAndPostSummaryAsync(meetingId);
                }
                catch (Exception summaryEx)
                {
                    _logger.LogError(summaryEx, "Failed to generate final summary for meeting {MeetingId}", meetingId);
                    // Continue with cleanup even if final summary fails
                }
            }

            // Step 4: Clear meeting-specific resources from memory (Requirement 6.1, 6.2)
            _transcriptionBufferService.ClearBuffer(meetingId);
            
            // Update and remove meeting context
            if (_activeMeetings.TryRemove(meetingId, out var context))
            {
                context.EndTime = DateTimeOffset.UtcNow;
                
                _logger.LogInformation(
                    "Meeting {MeetingId} ended. Duration: {Duration} minutes, Participants: {ParticipantCount}",
                    meetingId,
                    (context.EndTime.Value - context.StartTime).TotalMinutes,
                    context.ParticipantIds.Count);
            }

            // Track meeting end event
            _telemetryService.TrackMeetingEvent("Ended", meetingId);

            _logger.LogInformation("Meeting {MeetingId} cleanup completed successfully", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during meeting cleanup for {MeetingId}", meetingId);
            
            // Ensure resources are cleaned up even if there's an error
            try
            {
                if (_summaryTimers.TryRemove(meetingId, out var timer))
                {
                    timer?.Dispose();
                }
                
                if (_transcriptionCancellationTokens.TryRemove(meetingId, out var cts))
                {
                    cts?.Dispose();
                }
                
                _activeMeetings.TryRemove(meetingId, out _);
                _transcriptionBufferService.ClearBuffer(meetingId);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogCritical(cleanupEx, "Critical error during emergency cleanup for meeting {MeetingId}", meetingId);
            }
        }
    }

    protected override async Task OnTeamsMeetingParticipantsJoinAsync(
        MeetingParticipantsEventDetails meeting,
        ITurnContext<IEventActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var meetingId = turnContext.Activity.Conversation.Id;
        _logger.LogInformation("Participants joined meeting: {MeetingId}", meetingId);

        try
        {
            if (!_activeMeetings.TryGetValue(meetingId, out var context))
            {
                _logger.LogWarning("Meeting context not found for {MeetingId}", meetingId);
                return;
            }

            // Check if late joiner notifications are enabled
            if (!context.Configuration.EnableLateJoinerNotifications)
            {
                return;
            }

            var meetingStartTime = context.StartTime;
            var lateJoinerThreshold = TimeSpan.FromMinutes(5);

            foreach (var member in meeting.Members)
            {
                var joinTime = DateTimeOffset.UtcNow;
                var timeSinceStart = joinTime - meetingStartTime;

                // Add participant to context
                if (!context.ParticipantIds.Contains(member.User.Id))
                {
                    context.ParticipantIds.Add(member.User.Id);
                }

                // Check if participant is a late joiner (>5 minutes after start)
                if (timeSinceStart > lateJoinerThreshold)
                {
                    _logger.LogInformation(
                        "Late joiner detected: {UserId} joined {Minutes} minutes after start",
                        member.User.Id,
                        timeSinceStart.TotalMinutes);

                    // Use LateJoinerService to handle catch-up
                    var participant = new Participant
                    {
                        Id = member.User.Id,
                        Name = member.User.Name ?? "Unknown",
                        Email = member.User.Id, // In real scenario, fetch from user profile
                        JoinTime = joinTime
                    };

                    await _lateJoinerService.HandleParticipantJoinAsync(
                        meetingId,
                        participant,
                        meetingStartTime);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling participant join for meeting {MeetingId}", meetingId);
        }
    }

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext,
        CancellationToken cancellationToken)
    {
        var text = turnContext.Activity.Text?.Trim().ToLowerInvariant();

        _logger.LogInformation("Received message: {Text}", text);

        try
        {
            switch (text)
            {
                case "help":
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text("Available commands:\n" +
                            "- **help**: Show this help message\n" +
                            "- **status**: Show current meeting status\n" +
                            "- **summary**: Generate summary now"),
                        cancellationToken);
                    break;

                case "status":
                    var statusMessage = $"Active meetings: {_activeMeetings.Count}";
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text(statusMessage),
                        cancellationToken);
                    break;

                case "summary":
                    // Trigger immediate summary generation
                    var meetingId = turnContext.Activity.Conversation.Id;
                    if (_activeMeetings.ContainsKey(meetingId))
                    {
                        await GenerateAndPostSummaryAsync(meetingId);
                        await turnContext.SendActivityAsync(
                            MessageFactory.Text("Summary generated successfully!"),
                            cancellationToken);
                    }
                    else
                    {
                        await turnContext.SendActivityAsync(
                            MessageFactory.Text("No active meeting found."),
                            cancellationToken);
                    }
                    break;

                default:
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text("Unknown command. Type 'help' for available commands."),
                        cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message activity");
            await turnContext.SendActivityAsync(
                MessageFactory.Text("An error occurred processing your request."),
                cancellationToken);
        }
    }

    /// <summary>
    /// Connects to Graph API transcription stream and buffers segments in memory.
    /// Integration Point: Graph API Service -> Transcription Buffer Service
    /// Requirements: 1.1, 1.2, 1.3, 1.4
    /// </summary>
    private async Task StartTranscriptionProcessingAsync(string meetingId, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transcriptionCancellationTokens[meetingId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                // Connect to transcription stream via Graph API (Requirement 1.1, 1.4)
                var transcriptionStream = await _graphApiService.SubscribeToTranscriptionAsync(
                    meetingId,
                    cts.Token);

                var segmentCount = 0;
                await foreach (var segment in transcriptionStream.WithCancellation(cts.Token))
                {
                    // Buffer segments in memory with speaker attribution (Requirement 1.2, 1.4)
                    _transcriptionBufferService.AddSegment(meetingId, segment);
                    segmentCount++;
                    
                    _logger.LogDebug(
                        "Added transcription segment for meeting {MeetingId}: {Speaker} - {Text}",
                        meetingId,
                        segment.SpeakerName,
                        segment.Text);
                }
                
                // Track total segments processed
                if (segmentCount > 0)
                {
                    _telemetryService.TrackTranscriptionSegmentsProcessed(meetingId, segmentCount);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transcription processing cancelled for meeting {MeetingId}", meetingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing transcription stream for meeting {MeetingId}", meetingId);
                
                // Track the error
                _telemetryService.TrackMeetingEvent("TranscriptionStreamError", meetingId);
                
                // Attempt reconnection after 5 seconds (Requirement 1.3)
                if (!cts.Token.IsCancellationRequested)
                {
                    _logger.LogInformation("Attempting to reconnect transcription stream for meeting {MeetingId}", meetingId);
                    
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
                        await StartTranscriptionProcessingAsync(meetingId, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Reconnection cancelled for meeting {MeetingId}", meetingId);
                    }
                    catch (Exception reconnectEx)
                    {
                        _logger.LogCritical(reconnectEx, "Failed to reconnect transcription stream for meeting {MeetingId}", meetingId);
                    }
                }
            }
        }, cts.Token);
    }

    /// <summary>
    /// Starts periodic timer to trigger summary generation at configured intervals.
    /// Integration Point: Timer -> Summary Generation Workflow
    /// Requirements: 2.1
    /// </summary>
    private void StartSummaryGenerationTimer(string meetingId, int intervalMinutes)
    {
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        
        var timer = new Timer(
            async _ => await GenerateAndPostSummaryAsync(meetingId),
            null,
            interval,
            interval);

        _summaryTimers[meetingId] = timer;
        
        _logger.LogInformation(
            "Started summary generation timer for meeting {MeetingId} with interval {Interval} minutes",
            meetingId,
            intervalMinutes);
    }

    /// <summary>
    /// Orchestrates the complete summary generation workflow:
    /// 1. Retrieve buffered transcription segments
    /// 2. Generate AI summary via OpenAI
    /// 3. Store summary in Cosmos DB
    /// 4. Post summary to meeting chat
    /// 5. Clear transcription buffer
    /// 
    /// Integration Points:
    /// - Transcription Buffer Service -> Summary Generation Service
    /// - Summary Generation Service -> Summary Storage Service
    /// - Summary Storage Service -> Graph API Service (chat posting)
    /// 
    /// Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 1.5, 7.3
    /// </summary>
    private async Task GenerateAndPostSummaryAsync(string meetingId)
    {
        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        
        try
        {
            // Step 1: Check if there are segments to summarize
            if (!_transcriptionBufferService.HasSegments(meetingId))
            {
                _logger.LogInformation("No transcription segments to summarize for meeting {MeetingId}", meetingId);
                return;
            }

            // Step 2: Retrieve buffered segments (Requirement 2.2)
            var segments = _transcriptionBufferService.GetSegments(meetingId);
            var segmentsList = segments.ToList();

            _logger.LogInformation(
                "Generating summary for meeting {MeetingId} with {Count} segments",
                meetingId,
                segmentsList.Count);

            // Track segments being processed
            _telemetryService.TrackTranscriptionSegmentsProcessed(meetingId, segmentsList.Count);

            // Step 3: Generate AI summary (Requirement 2.2, 2.3)
            var summary = await _summaryGenerationService.GenerateSummaryAsync(
                segmentsList,
                new SummaryOptions());

            summary.MeetingId = meetingId;
            success = true;

            // Step 4: Add participant list to summary metadata for access control (Requirement 7.3)
            try
            {
                var participants = await _graphApiService.GetMeetingParticipantsAsync(meetingId);
                summary.Participants = participants.Select(p => p.Id).ToList();
                
                _logger.LogInformation(
                    "Added {Count} participants to summary metadata for meeting {MeetingId}",
                    summary.Participants.Count,
                    meetingId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to retrieve participants for meeting {MeetingId}. Summary will be saved without participant list.",
                    meetingId);
                
                // Use participants from meeting context as fallback
                if (_activeMeetings.TryGetValue(meetingId, out var fallbackContext))
                {
                    summary.Participants = fallbackContext.ParticipantIds.ToList();
                }
            }

            // Step 5: Store summary in Cosmos DB (Requirement 2.5)
            // Note: Storage service handles buffering if Cosmos DB is unavailable (Requirement 6.1, 6.2)
            await _summaryStorageService.SaveSummaryAsync(summary);

            // Step 6: Post to chat if enabled (Requirement 2.4)
            if (_activeMeetings.TryGetValue(meetingId, out var context) && 
                context.Configuration.AutoPostToChat)
            {
                var summaryMessage = FormatSummaryMessage(summary);
                await _graphApiService.SendMessageToMeetingChatAsync(meetingId, summaryMessage);
            }

            // Step 7: Clear buffer after successful summary generation (Requirement 1.5)
            _transcriptionBufferService.ClearBuffer(meetingId);

            _logger.LogInformation("Summary generated and posted for meeting {MeetingId}", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating summary for meeting {MeetingId}", meetingId);
            success = false;
            
            // Track the error
            _telemetryService.TrackMeetingEvent("SummaryGenerationError", meetingId);
            
            // Retry logic is handled within SummaryGenerationService (Requirement 6.3)
            // Log the failure for monitoring
            _logger.LogWarning("Summary generation failed for meeting {MeetingId}. Will retry on next interval.", meetingId);
        }
        finally
        {
            // Track summary generation time
            var duration = DateTimeOffset.UtcNow - startTime;
            _telemetryService.TrackSummaryGenerationTime(meetingId, duration, success);
        }
    }



    private string FormatSummaryMessage(MeetingSummary summary)
    {
        var message = $"**Meeting Summary ({summary.StartTime:HH:mm} - {summary.EndTime:HH:mm})**\n\n";
        message += $"{summary.Content}\n\n";

        if (summary.KeyTopics.Any())
        {
            message += "**Key Topics:**\n";
            foreach (var topic in summary.KeyTopics)
            {
                message += $"- {topic}\n";
            }
            message += "\n";
        }

        if (summary.Decisions.Any())
        {
            message += "**Decisions:**\n";
            foreach (var decision in summary.Decisions)
            {
                message += $"- {decision}\n";
            }
            message += "\n";
        }

        if (summary.ActionItems.Any())
        {
            message += "**Action Items:**\n";
            foreach (var item in summary.ActionItems)
            {
                var assignee = !string.IsNullOrEmpty(item.AssignedTo) ? $" ({item.AssignedTo})" : "";
                message += $"- {item.Description}{assignee}\n";
            }
        }

        return message;
    }


}

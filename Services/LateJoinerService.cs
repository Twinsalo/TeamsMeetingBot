using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;
using Microsoft.Bot.Schema;
using Newtonsoft.Json;

namespace TeamsMeetingBot.Services;

public class LateJoinerService : ILateJoinerService
{
    private readonly ISummaryStorageService _summaryStorageService;
    private readonly IGraphApiService _graphApiService;
    private readonly ILogger<LateJoinerService> _logger;
    private const int LateJoinerThresholdMinutes = 5;
    private const int DeliveryTimeoutSeconds = 10;

    public LateJoinerService(
        ISummaryStorageService summaryStorageService,
        IGraphApiService graphApiService,
        ILogger<LateJoinerService> logger)
    {
        _summaryStorageService = summaryStorageService;
        _graphApiService = graphApiService;
        _logger = logger;
    }

    public async Task HandleParticipantJoinAsync(
        string meetingId, 
        Participant participant, 
        DateTimeOffset meetingStartTime)
    {
        try
        {
            _logger.LogInformation(
                "Processing participant join for {ParticipantName} ({ParticipantId}) in meeting {MeetingId}",
                participant.Name,
                participant.Id,
                meetingId);

            // Detect if participant is a late joiner (>5 minutes after start)
            var timeSinceStart = participant.JoinTime - meetingStartTime;
            if (timeSinceStart.TotalMinutes <= LateJoinerThresholdMinutes)
            {
                _logger.LogInformation(
                    "Participant {ParticipantName} joined within {Minutes} minutes, not a late joiner",
                    participant.Name,
                    timeSinceStart.TotalMinutes);
                return;
            }

            _logger.LogInformation(
                "Participant {ParticipantName} is a late joiner (joined {Minutes} minutes after start)",
                participant.Name,
                timeSinceStart.TotalMinutes);

            // Use a timeout to ensure delivery within 10 seconds
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DeliveryTimeoutSeconds));
            
            // Retrieve previous summaries from storage
            var summaries = await _summaryStorageService.GetMeetingSummariesAsync(
                meetingId,
                startTime: meetingStartTime,
                endTime: participant.JoinTime);

            var summaryList = summaries.ToList();
            
            if (!summaryList.Any())
            {
                _logger.LogInformation(
                    "No summaries available for late joiner {ParticipantName} in meeting {MeetingId}",
                    participant.Name,
                    meetingId);
                return;
            }

            _logger.LogInformation(
                "Retrieved {Count} summaries for late joiner {ParticipantName}",
                summaryList.Count,
                participant.Name);

            // Format summaries into Adaptive Card
            var adaptiveCard = CreateCatchUpAdaptiveCard(summaryList);

            // Send private message to late joiner
            await _graphApiService.SendPrivateMessageAsync(participant.Id, adaptiveCard);

            _logger.LogInformation(
                "Successfully sent catch-up information to late joiner {ParticipantName} ({ParticipantId})",
                participant.Name,
                participant.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "Timeout delivering catch-up information to {ParticipantName} in meeting {MeetingId}",
                participant.Name,
                meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error handling late joiner {ParticipantName} in meeting {MeetingId}",
                participant.Name,
                meetingId);
            // Don't throw - we don't want to disrupt the meeting if catch-up fails
        }
    }

    private Attachment CreateCatchUpAdaptiveCard(List<MeetingSummary> summaries)
    {
        var cardBody = new List<object>
        {
            new
            {
                type = "TextBlock",
                text = "Meeting Catch-Up",
                weight = "Bolder",
                size = "Large"
            },
            new
            {
                type = "TextBlock",
                text = "You joined late. Here's what you missed:",
                wrap = true,
                spacing = "Medium"
            }
        };

        // Add each summary as a container
        foreach (var summary in summaries.OrderBy(s => s.StartTime))
        {
            var timeRange = $"{summary.StartTime:HH:mm} - {summary.EndTime:HH:mm}";
            
            var summaryContainer = new
            {
                type = "Container",
                spacing = "Medium",
                separator = true,
                items = new List<object>
                {
                    new
                    {
                        type = "TextBlock",
                        text = timeRange,
                        weight = "Bolder",
                        color = "Accent"
                    },
                    new
                    {
                        type = "TextBlock",
                        text = summary.Content,
                        wrap = true,
                        spacing = "Small"
                    }
                }
            };

            // Add key topics if available
            if (summary.KeyTopics.Any())
            {
                var topicsText = "**Key Topics:** " + string.Join(", ", summary.KeyTopics);
                ((List<object>)summaryContainer.items).Add(new
                {
                    type = "TextBlock",
                    text = topicsText,
                    wrap = true,
                    spacing = "Small",
                    isSubtle = true
                });
            }

            // Add decisions if available
            if (summary.Decisions.Any())
            {
                var decisionsText = "**Decisions:** " + string.Join("; ", summary.Decisions);
                ((List<object>)summaryContainer.items).Add(new
                {
                    type = "TextBlock",
                    text = decisionsText,
                    wrap = true,
                    spacing = "Small",
                    isSubtle = true
                });
            }

            // Add action items if available
            if (summary.ActionItems.Any())
            {
                var actionItemsText = "**Action Items:** " + string.Join("; ", 
                    summary.ActionItems.Select(ai => 
                        string.IsNullOrEmpty(ai.AssignedTo) 
                            ? ai.Description 
                            : $"{ai.Description} ({ai.AssignedTo})"));
                
                ((List<object>)summaryContainer.items).Add(new
                {
                    type = "TextBlock",
                    text = actionItemsText,
                    wrap = true,
                    spacing = "Small",
                    isSubtle = true
                });
            }

            cardBody.Add(summaryContainer);
        }

        var adaptiveCardContent = new
        {
            type = "AdaptiveCard",
            body = cardBody,
            version = "1.4"
        };

        return new Attachment
        {
            ContentType = "application/vnd.microsoft.card.adaptive",
            Content = adaptiveCardContent
        };
    }
}

using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Service for managing authorization and access control for meeting summaries
/// Implements participant-based access control as per requirement 7.3
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    private readonly IGraphApiService _graphApiService;
    private readonly ILogger<AuthorizationService> _logger;

    public AuthorizationService(
        IGraphApiService graphApiService,
        ILogger<AuthorizationService> logger)
    {
        _graphApiService = graphApiService;
        _logger = logger;
    }

    public bool CanAccessSummary(string userId, MeetingSummary summary)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Access denied: User ID is null or empty");
            return false;
        }

        if (summary == null)
        {
            _logger.LogWarning("Access denied: Summary is null");
            return false;
        }

        // Check if the user is in the participant list
        var hasAccess = summary.Participants.Contains(userId, StringComparer.OrdinalIgnoreCase);

        if (!hasAccess)
        {
            _logger.LogWarning(
                "Access denied: User {UserId} is not a participant of meeting {MeetingId}",
                userId,
                summary.MeetingId);
        }
        else
        {
            _logger.LogDebug(
                "Access granted: User {UserId} is a participant of meeting {MeetingId}",
                userId,
                summary.MeetingId);
        }

        return hasAccess;
    }

    public IEnumerable<MeetingSummary> FilterAccessibleSummaries(
        string userId,
        IEnumerable<MeetingSummary> summaries)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Cannot filter summaries: User ID is null or empty");
            return Enumerable.Empty<MeetingSummary>();
        }

        var accessibleSummaries = summaries
            .Where(s => CanAccessSummary(userId, s))
            .ToList();

        _logger.LogInformation(
            "User {UserId} has access to {AccessibleCount} out of {TotalCount} summaries",
            userId,
            accessibleSummaries.Count,
            summaries.Count());

        return accessibleSummaries;
    }

    public async Task ValidateMeetingAccessAsync(string userId, string meetingId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        }

        if (string.IsNullOrEmpty(meetingId))
        {
            throw new ArgumentException("Meeting ID cannot be null or empty", nameof(meetingId));
        }

        try
        {
            // Get the list of meeting participants from Graph API
            var participants = await _graphApiService.GetMeetingParticipantsAsync(meetingId);
            
            // Check if the user is in the participant list
            var isParticipant = participants.Any(p => 
                p.Id.Equals(userId, StringComparison.OrdinalIgnoreCase) ||
                p.Email.Equals(userId, StringComparison.OrdinalIgnoreCase));

            if (!isParticipant)
            {
                _logger.LogWarning(
                    "Access denied: User {UserId} is not a participant of meeting {MeetingId}",
                    userId,
                    meetingId);
                
                throw new UnauthorizedAccessException(
                    $"User {userId} does not have access to meeting {meetingId}. " +
                    "Only meeting participants can access summaries.");
            }

            _logger.LogInformation(
                "Access validated: User {UserId} is a participant of meeting {MeetingId}",
                userId,
                meetingId);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error validating access for user {UserId} to meeting {MeetingId}",
                userId,
                meetingId);
            throw new InvalidOperationException(
                $"Unable to validate access to meeting {meetingId}. Please try again later.", ex);
        }
    }
}

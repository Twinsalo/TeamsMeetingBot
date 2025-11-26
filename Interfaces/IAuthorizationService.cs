using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

/// <summary>
/// Service for managing authorization and access control for meeting summaries
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Checks if a user has access to a specific meeting summary
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="summary">The meeting summary to check access for</param>
    /// <returns>True if the user has access, false otherwise</returns>
    bool CanAccessSummary(string userId, MeetingSummary summary);
    
    /// <summary>
    /// Filters a collection of summaries to only include those the user can access
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="summaries">The collection of summaries to filter</param>
    /// <returns>Filtered collection of summaries the user can access</returns>
    IEnumerable<MeetingSummary> FilterAccessibleSummaries(string userId, IEnumerable<MeetingSummary> summaries);
    
    /// <summary>
    /// Validates that a user has access to summaries for a specific meeting
    /// </summary>
    /// <param name="userId">The user ID to check</param>
    /// <param name="meetingId">The meeting ID to check access for</param>
    /// <returns>Task that completes when validation is done</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown if user doesn't have access</exception>
    Task ValidateMeetingAccessAsync(string userId, string meetingId);
}

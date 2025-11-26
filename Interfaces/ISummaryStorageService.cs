using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

public interface ISummaryStorageService
{
    Task<string> SaveSummaryAsync(MeetingSummary summary);
    Task<MeetingSummary?> GetSummaryAsync(string summaryId, string? userId = null);
    Task<IEnumerable<MeetingSummary>> GetMeetingSummariesAsync(string meetingId, string? userId = null, DateTimeOffset? startTime = null, DateTimeOffset? endTime = null);
    Task<IEnumerable<MeetingSummary>> SearchSummariesAsync(string meetingId, string searchQuery, string? userId = null);
    Task DeleteSummariesAsync(string meetingId);
}

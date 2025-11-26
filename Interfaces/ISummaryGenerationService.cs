using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

public interface ISummaryGenerationService
{
    Task<MeetingSummary> GenerateSummaryAsync(IEnumerable<TranscriptionSegment> segments, SummaryOptions options);
}

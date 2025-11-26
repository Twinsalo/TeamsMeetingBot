using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

public interface ITranscriptionBufferService
{
    void AddSegment(string meetingId, TranscriptionSegment segment);
    IEnumerable<TranscriptionSegment> GetSegments(string meetingId, TimeSpan? duration = null);
    void ClearBuffer(string meetingId);
    bool HasSegments(string meetingId);
}

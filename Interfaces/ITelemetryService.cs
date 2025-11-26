namespace TeamsMeetingBot.Interfaces;

public interface ITelemetryService
{
    void TrackSummaryGenerationTime(string meetingId, TimeSpan duration, bool success);
    void TrackTranscriptionSegmentsProcessed(string meetingId, int segmentCount);
    void TrackGraphApiCall(string operation, TimeSpan duration, bool success);
    void TrackCosmosDbCall(string operation, TimeSpan duration, bool success);
    void TrackMeetingEvent(string eventType, string meetingId, Dictionary<string, string>? properties = null);
}

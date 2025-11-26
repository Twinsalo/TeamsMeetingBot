namespace TeamsMeetingBot.Models;

public class TranscriptionSegment
{
    public string Text { get; set; } = string.Empty;
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}

namespace TeamsMeetingBot.Models;

public class MeetingDetails
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public string OrganizerEmail { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}

namespace TeamsMeetingBot.Models;

public class MeetingContext
{
    public string MeetingId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public List<string> ParticipantIds { get; set; } = new();
    public MeetingConfiguration Configuration { get; set; } = new();
}

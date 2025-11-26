using Newtonsoft.Json;

namespace TeamsMeetingBot.Models;

public class MeetingSummary
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("meetingId")]
    public string MeetingId { get; set; } = string.Empty;
    
    [JsonProperty("startTime")]
    public DateTimeOffset StartTime { get; set; }
    
    [JsonProperty("endTime")]
    public DateTimeOffset EndTime { get; set; }
    
    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;
    
    [JsonProperty("keyTopics")]
    public List<string> KeyTopics { get; set; } = new();
    
    [JsonProperty("decisions")]
    public List<string> Decisions { get; set; } = new();
    
    [JsonProperty("actionItems")]
    public List<ActionItem> ActionItems { get; set; } = new();
    
    [JsonProperty("participants")]
    public List<string> Participants { get; set; } = new();
    
    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
    
    [JsonProperty("ttl")]
    public int? Ttl { get; set; }
}

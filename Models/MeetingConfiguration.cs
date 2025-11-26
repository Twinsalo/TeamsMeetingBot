using System.ComponentModel.DataAnnotations;

namespace TeamsMeetingBot.Models;

public class MeetingConfiguration
{
    [Range(5, 30, ErrorMessage = "Summary interval must be between 5 and 30 minutes")]
    public int SummaryIntervalMinutes { get; set; } = 10;
    
    public bool AutoPostToChat { get; set; } = true;
    
    public bool EnableLateJoinerNotifications { get; set; } = true;
    
    [Range(30, 365, ErrorMessage = "Retention days must be between 30 and 365")]
    public int RetentionDays { get; set; } = 30;
}

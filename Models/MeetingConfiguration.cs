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
    
    /// <summary>
    /// Determines the transcription method to use.
    /// - Polling: Traditional polling-based approach (default, simpler setup)
    /// - Webhook: Microsoft Graph Change Notifications (requires public endpoint, more real-time)
    /// </summary>
    public TranscriptionMethod TranscriptionMethod { get; set; } = TranscriptionMethod.Polling;
}

public enum TranscriptionMethod
{
    /// <summary>
    /// Poll for transcription updates periodically (simpler, no webhook required)
    /// </summary>
    Polling = 0,
    
    /// <summary>
    /// Use Microsoft Graph Change Notifications webhooks (more real-time, requires public HTTPS endpoint)
    /// </summary>
    Webhook = 1
}

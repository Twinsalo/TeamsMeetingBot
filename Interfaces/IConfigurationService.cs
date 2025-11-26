using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

public interface IConfigurationService
{
    Task<MeetingConfiguration> GetMeetingConfigAsync(string meetingId);
    Task UpdateMeetingConfigAsync(string meetingId, MeetingConfiguration config);
}

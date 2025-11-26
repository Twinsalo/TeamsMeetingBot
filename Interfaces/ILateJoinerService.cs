using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

public interface ILateJoinerService
{
    Task HandleParticipantJoinAsync(string meetingId, Participant participant, DateTimeOffset meetingStartTime);
}

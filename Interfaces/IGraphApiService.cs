using TeamsMeetingBot.Models;
using Microsoft.Bot.Schema;

namespace TeamsMeetingBot.Interfaces;

public interface IGraphApiService
{
    Task<IAsyncEnumerable<TranscriptionSegment>> SubscribeToTranscriptionAsync(string meetingId, CancellationToken cancellationToken);
    void UnsubscribeFromTranscription(string meetingId);
    Task<MeetingDetails> GetMeetingDetailsAsync(string meetingId);
    Task<IEnumerable<Participant>> GetMeetingParticipantsAsync(string meetingId);
    Task SendMessageToMeetingChatAsync(string meetingId, string message);
    Task SendPrivateMessageAsync(string userId, Attachment card);
}

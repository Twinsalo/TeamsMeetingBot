using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Interfaces;

/// <summary>
/// Strategy interface for different transcription retrieval methods.
/// Allows switching between polling-based and webhook-based approaches.
/// </summary>
public interface ITranscriptionStrategy
{
    /// <summary>
    /// Starts transcription processing for a meeting
    /// </summary>
    /// <param name="meetingId">The meeting identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task StartAsync(string meetingId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Stops transcription processing for a meeting
    /// </summary>
    /// <param name="meetingId">The meeting identifier</param>
    Task StopAsync(string meetingId);
    
    /// <summary>
    /// Gets the transcription method type
    /// </summary>
    TranscriptionMethod Method { get; }
}

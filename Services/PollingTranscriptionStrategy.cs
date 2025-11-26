using System.Runtime.CompilerServices;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Polling-based transcription strategy that periodically checks for new transcription content.
/// This is the traditional approach and doesn't require webhook setup.
/// </summary>
public class PollingTranscriptionStrategy : ITranscriptionStrategy
{
    private readonly IGraphApiService _graphApiService;
    private readonly ITranscriptionBufferService _transcriptionBufferService;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<PollingTranscriptionStrategy> _logger;
    private readonly Dictionary<string, CancellationTokenSource> _activeSessions = new();

    public TranscriptionMethod Method => TranscriptionMethod.Polling;

    public PollingTranscriptionStrategy(
        IGraphApiService graphApiService,
        ITranscriptionBufferService transcriptionBufferService,
        ITelemetryService telemetryService,
        ILogger<PollingTranscriptionStrategy> logger)
    {
        _graphApiService = graphApiService;
        _transcriptionBufferService = transcriptionBufferService;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    public async Task StartAsync(string meetingId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Starting polling-based transcription for meeting {MeetingId}",
            meetingId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeSessions[meetingId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var transcriptionStream = await _graphApiService.SubscribeToTranscriptionAsync(
                    meetingId,
                    cts.Token);

                var segmentCount = 0;
                await foreach (var segment in transcriptionStream.WithCancellation(cts.Token))
                {
                    _transcriptionBufferService.AddSegment(meetingId, segment);
                    segmentCount++;
                    
                    _logger.LogDebug(
                        "Buffered transcription segment for meeting {MeetingId}: {Speaker} - {Text}",
                        meetingId,
                        segment.SpeakerName,
                        segment.Text);
                }
                
                if (segmentCount > 0)
                {
                    _telemetryService.TrackTranscriptionSegmentsProcessed(meetingId, segmentCount);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Polling transcription cancelled for meeting {MeetingId}",
                    meetingId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error in polling transcription for meeting {MeetingId}",
                    meetingId);
            }
        }, cts.Token);

        await Task.CompletedTask;
    }

    public Task StopAsync(string meetingId)
    {
        if (_activeSessions.TryGetValue(meetingId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _activeSessions.Remove(meetingId);
            
            _graphApiService.UnsubscribeFromTranscription(meetingId);
            
            _logger.LogInformation(
                "Stopped polling transcription for meeting {MeetingId}",
                meetingId);
        }

        return Task.CompletedTask;
    }
}

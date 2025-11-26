using System.Collections.Concurrent;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

public class TranscriptionBufferService : ITranscriptionBufferService
{
    private const int MaxSegmentsPerMeeting = 1000;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TranscriptionSegment>> _buffers = new();
    private readonly ILogger<TranscriptionBufferService> _logger;

    public TranscriptionBufferService(ILogger<TranscriptionBufferService> logger)
    {
        _logger = logger;
    }

    public void AddSegment(string meetingId, TranscriptionSegment segment)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            throw new ArgumentException("Meeting ID cannot be null or empty", nameof(meetingId));
        }

        if (segment == null)
        {
            throw new ArgumentNullException(nameof(segment));
        }

        var queue = _buffers.GetOrAdd(meetingId, _ => new ConcurrentQueue<TranscriptionSegment>());
        
        // Enforce memory limit
        if (queue.Count >= MaxSegmentsPerMeeting)
        {
            _logger.LogWarning(
                "Meeting {MeetingId} has reached maximum buffer size of {MaxSegments}. Removing oldest segment.",
                meetingId,
                MaxSegmentsPerMeeting);
            
            queue.TryDequeue(out _);
        }

        queue.Enqueue(segment);
        
        _logger.LogDebug(
            "Added transcription segment to meeting {MeetingId}. Buffer size: {BufferSize}",
            meetingId,
            queue.Count);
    }

    public IEnumerable<TranscriptionSegment> GetSegments(string meetingId, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            throw new ArgumentException("Meeting ID cannot be null or empty", nameof(meetingId));
        }

        if (!_buffers.TryGetValue(meetingId, out var queue))
        {
            return Enumerable.Empty<TranscriptionSegment>();
        }

        var segments = queue.ToArray();

        if (duration.HasValue)
        {
            var cutoffTime = DateTimeOffset.UtcNow - duration.Value;
            segments = segments.Where(s => s.Timestamp >= cutoffTime).ToArray();
        }

        return segments;
    }

    public void ClearBuffer(string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            throw new ArgumentException("Meeting ID cannot be null or empty", nameof(meetingId));
        }

        if (_buffers.TryRemove(meetingId, out var queue))
        {
            _logger.LogInformation(
                "Cleared transcription buffer for meeting {MeetingId}. Removed {SegmentCount} segments.",
                meetingId,
                queue.Count);
        }
        else
        {
            _logger.LogDebug(
                "No buffer found for meeting {MeetingId} to clear.",
                meetingId);
        }
    }

    public bool HasSegments(string meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            throw new ArgumentException("Meeting ID cannot be null or empty", nameof(meetingId));
        }

        return _buffers.TryGetValue(meetingId, out var queue) && !queue.IsEmpty;
    }
}

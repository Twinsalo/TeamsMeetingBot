using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using TeamsMeetingBot.Interfaces;

namespace TeamsMeetingBot.Services;

public class TelemetryService : ITelemetryService
{
    private readonly TelemetryClient _telemetryClient;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(TelemetryClient telemetryClient, ILogger<TelemetryService> logger)
    {
        _telemetryClient = telemetryClient;
        _logger = logger;
    }

    public void TrackSummaryGenerationTime(string meetingId, TimeSpan duration, bool success)
    {
        var properties = new Dictionary<string, string>
        {
            { "MeetingId", meetingId },
            { "Success", success.ToString() }
        };

        var metrics = new Dictionary<string, double>
        {
            { "DurationMs", duration.TotalMilliseconds }
        };

        _telemetryClient.TrackEvent("SummaryGeneration", properties, metrics);
        _telemetryClient.TrackMetric("SummaryGenerationTime", duration.TotalMilliseconds, properties);

        _logger.LogInformation(
            "Tracked summary generation: MeetingId={MeetingId}, Duration={Duration}ms, Success={Success}",
            meetingId,
            duration.TotalMilliseconds,
            success);
    }

    public void TrackTranscriptionSegmentsProcessed(string meetingId, int segmentCount)
    {
        var properties = new Dictionary<string, string>
        {
            { "MeetingId", meetingId }
        };

        var metrics = new Dictionary<string, double>
        {
            { "SegmentCount", segmentCount }
        };

        _telemetryClient.TrackEvent("TranscriptionSegmentsProcessed", properties, metrics);
        _telemetryClient.TrackMetric("TranscriptionSegmentCount", segmentCount, properties);

        _logger.LogDebug(
            "Tracked transcription segments: MeetingId={MeetingId}, SegmentCount={SegmentCount}",
            meetingId,
            segmentCount);
    }

    public void TrackGraphApiCall(string operation, TimeSpan duration, bool success)
    {
        var dependency = new DependencyTelemetry
        {
            Name = $"GraphAPI.{operation}",
            Type = "HTTP",
            Target = "graph.microsoft.com",
            Duration = duration,
            Success = success,
            Timestamp = DateTimeOffset.UtcNow
        };

        dependency.Properties.Add("Operation", operation);
        dependency.Metrics.Add("DurationMs", duration.TotalMilliseconds);

        _telemetryClient.TrackDependency(dependency);

        _logger.LogDebug(
            "Tracked Graph API call: Operation={Operation}, Duration={Duration}ms, Success={Success}",
            operation,
            duration.TotalMilliseconds,
            success);
    }

    public void TrackCosmosDbCall(string operation, TimeSpan duration, bool success)
    {
        var dependency = new DependencyTelemetry
        {
            Name = $"CosmosDB.{operation}",
            Type = "Azure DocumentDB",
            Target = "CosmosDB",
            Duration = duration,
            Success = success,
            Timestamp = DateTimeOffset.UtcNow
        };

        dependency.Properties.Add("Operation", operation);
        dependency.Metrics.Add("DurationMs", duration.TotalMilliseconds);

        _telemetryClient.TrackDependency(dependency);

        _logger.LogDebug(
            "Tracked Cosmos DB call: Operation={Operation}, Duration={Duration}ms, Success={Success}",
            operation,
            duration.TotalMilliseconds,
            success);
    }

    public void TrackMeetingEvent(string eventType, string meetingId, Dictionary<string, string>? properties = null)
    {
        var eventProperties = properties ?? new Dictionary<string, string>();
        eventProperties["EventType"] = eventType;
        eventProperties["MeetingId"] = meetingId;

        _telemetryClient.TrackEvent($"Meeting.{eventType}", eventProperties);

        _logger.LogInformation(
            "Tracked meeting event: EventType={EventType}, MeetingId={MeetingId}",
            eventType,
            meetingId);
    }
}

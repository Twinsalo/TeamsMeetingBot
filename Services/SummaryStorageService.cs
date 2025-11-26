using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;
using System.Collections.Concurrent;

namespace TeamsMeetingBot.Services;

public class SummaryStorageService : ISummaryStorageService
{
    private readonly Container _container;
    private readonly IAuthorizationService _authorizationService;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<SummaryStorageService> _logger;
    private readonly ConcurrentQueue<MeetingSummary> _buffer = new();
    private readonly SemaphoreSlim _flushLock = new(1);
    private readonly int _retentionDays;
    private const int MaxBufferSize = 10;

    public SummaryStorageService(
        CosmosClient cosmosClient,
        IAuthorizationService authorizationService,
        ITelemetryService telemetryService,
        IConfiguration configuration,
        ILogger<SummaryStorageService> logger)
    {
        _authorizationService = authorizationService;
        _telemetryService = telemetryService;
        _logger = logger;
        _retentionDays = configuration.GetValue<int>("SummarySettings:RetentionDays", 30);
        
        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "MeetingSummaries";
        var containerName = configuration["CosmosDb:ContainerName"] ?? "summaries";
        
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    public async Task<string> SaveSummaryAsync(MeetingSummary summary)
    {
        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        
        try
        {
            // Set metadata
            if (string.IsNullOrEmpty(summary.Id))
            {
                summary.Id = $"summary-{Guid.NewGuid()}";
            }
            
            summary.CreatedAt = DateTimeOffset.UtcNow;
            summary.Ttl = _retentionDays * 24 * 60 * 60; // Convert days to seconds

            // Try to save to Cosmos DB
            var response = await _container.CreateItemAsync(
                summary,
                new PartitionKey(summary.MeetingId));

            success = true;

            _logger.LogInformation(
                "Summary {SummaryId} saved for meeting {MeetingId}",
                summary.Id,
                summary.MeetingId);

            // Flush any buffered summaries if save was successful
            await FlushBufferAsync();

            return response.Resource.Id;
        }
        catch (CosmosException ex)
        {
            _logger.LogWarning(
                ex,
                "Cosmos DB unavailable, buffering summary {SummaryId} for meeting {MeetingId}",
                summary.Id,
                summary.MeetingId);

            // Buffer the summary
            _buffer.Enqueue(summary);

            if (_buffer.Count > MaxBufferSize)
            {
                _logger.LogError(
                    "Buffer overflow detected. Buffer size: {BufferSize}. Summaries may be lost.",
                    _buffer.Count);
            }

            return summary.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error saving summary {SummaryId} for meeting {MeetingId}",
                summary.Id,
                summary.MeetingId);
            throw;
        }
        finally
        {
            // Track Cosmos DB call telemetry
            var duration = DateTimeOffset.UtcNow - startTime;
            _telemetryService.TrackCosmosDbCall("SaveSummary", duration, success);
        }
    }

    public async Task<MeetingSummary?> GetSummaryAsync(string summaryId, string? userId = null)
    {
        var startTime = DateTimeOffset.UtcNow;
        var success = false;
        
        try
        {
            // Extract meetingId from summaryId if needed, or query across partitions
            var query = _container.GetItemLinqQueryable<MeetingSummary>()
                .Where(s => s.Id == summaryId)
                .ToFeedIterator();

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                var summary = response.FirstOrDefault();
                if (summary != null)
                {
                    // Apply authorization check if userId is provided
                    if (!string.IsNullOrEmpty(userId))
                    {
                        if (!_authorizationService.CanAccessSummary(userId, summary))
                        {
                            _logger.LogWarning(
                                "Access denied: User {UserId} attempted to access summary {SummaryId}",
                                userId,
                                summaryId);
                            throw new UnauthorizedAccessException(
                                $"User {userId} does not have access to this summary. " +
                                "Only meeting participants can access summaries.");
                        }
                    }
                    
                    success = true;
                    return summary;
                }
            }

            success = true;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Summary {SummaryId} not found", summaryId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving summary {SummaryId}", summaryId);
            throw;
        }
        finally
        {
            // Track Cosmos DB call telemetry
            var duration = DateTimeOffset.UtcNow - startTime;
            _telemetryService.TrackCosmosDbCall("GetSummary", duration, success);
        }
    }

    public async Task<IEnumerable<MeetingSummary>> GetMeetingSummariesAsync(
        string meetingId,
        string? userId = null,
        DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null)
    {
        var operationStartTime = DateTimeOffset.UtcNow;
        var success = false;
        
        try
        {
            // Validate user access to the meeting if userId is provided
            if (!string.IsNullOrEmpty(userId))
            {
                await _authorizationService.ValidateMeetingAccessAsync(userId, meetingId);
            }
            
            var summaries = new List<MeetingSummary>();
            
            var queryable = _container.GetItemLinqQueryable<MeetingSummary>()
                .Where(s => s.MeetingId == meetingId);

            // Apply time range filters if provided
            if (startTime.HasValue)
            {
                queryable = queryable.Where(s => s.StartTime >= startTime.Value);
            }

            if (endTime.HasValue)
            {
                queryable = queryable.Where(s => s.EndTime <= endTime.Value);
            }

            var query = queryable
                .OrderBy(s => s.StartTime)
                .ToFeedIterator();

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                summaries.AddRange(response);
            }

            // Apply authorization filter if userId is provided
            if (!string.IsNullOrEmpty(userId))
            {
                summaries = _authorizationService
                    .FilterAccessibleSummaries(userId, summaries)
                    .ToList();
            }

            success = true;

            _logger.LogInformation(
                "Retrieved {Count} summaries for meeting {MeetingId}" + 
                (string.IsNullOrEmpty(userId) ? "" : " for user {UserId}"),
                summaries.Count,
                meetingId,
                userId);

            return summaries;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving summaries for meeting {MeetingId}",
                meetingId);
            throw;
        }
        finally
        {
            // Track Cosmos DB call telemetry
            var duration = DateTimeOffset.UtcNow - operationStartTime;
            _telemetryService.TrackCosmosDbCall("GetMeetingSummaries", duration, success);
        }
    }

    public async Task<IEnumerable<MeetingSummary>> SearchSummariesAsync(
        string meetingId,
        string searchQuery,
        string? userId = null)
    {
        try
        {
            var summaries = new List<MeetingSummary>();
            var searchLower = searchQuery.ToLowerInvariant();

            // Get all summaries for the meeting (with authorization if userId provided)
            var allSummaries = await GetMeetingSummariesAsync(meetingId, userId);

            // Perform in-memory search on content, keyTopics, and decisions
            summaries = allSummaries.Where(s =>
                s.Content.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) ||
                s.KeyTopics.Any(t => t.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)) ||
                s.Decisions.Any(d => d.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _logger.LogInformation(
                "Search for '{SearchQuery}' in meeting {MeetingId} returned {Count} results" +
                (string.IsNullOrEmpty(userId) ? "" : " for user {UserId}"),
                searchQuery,
                meetingId,
                summaries.Count,
                userId);

            return summaries;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error searching summaries for meeting {MeetingId} with query '{SearchQuery}'",
                meetingId,
                searchQuery);
            throw;
        }
    }

    public async Task DeleteSummariesAsync(string meetingId)
    {
        try
        {
            // Get all summaries for the meeting
            var summaries = await GetMeetingSummariesAsync(meetingId);

            // Delete each summary
            foreach (var summary in summaries)
            {
                await _container.DeleteItemAsync<MeetingSummary>(
                    summary.Id,
                    new PartitionKey(summary.MeetingId));
            }

            _logger.LogInformation(
                "Deleted {Count} summaries for meeting {MeetingId}",
                summaries.Count(),
                meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deleting summaries for meeting {MeetingId}",
                meetingId);
            throw;
        }
    }

    private async Task FlushBufferAsync()
    {
        if (!_buffer.Any())
        {
            return;
        }

        await _flushLock.WaitAsync();
        try
        {
            var flushedCount = 0;
            while (_buffer.TryDequeue(out var summary))
            {
                try
                {
                    await _container.CreateItemAsync(
                        summary,
                        new PartitionKey(summary.MeetingId));
                    
                    flushedCount++;
                    
                    _logger.LogInformation(
                        "Flushed buffered summary {SummaryId} for meeting {MeetingId}",
                        summary.Id,
                        summary.MeetingId);
                }
                catch (CosmosException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to flush buffered summary {SummaryId}, re-queuing",
                        summary.Id);
                    
                    // Re-queue the summary
                    _buffer.Enqueue(summary);
                    break; // Stop flushing if we still can't connect
                }
            }

            if (flushedCount > 0)
            {
                _logger.LogInformation(
                    "Successfully flushed {Count} buffered summaries",
                    flushedCount);
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }
}

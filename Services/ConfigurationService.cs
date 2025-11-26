using Microsoft.Azure.Cosmos;
using System.Collections.Concurrent;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly Container _container;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly ConcurrentDictionary<string, MeetingConfiguration> _cache = new();
    private readonly MeetingConfiguration _defaultConfig;
    private readonly SemaphoreSlim _updateLock = new(1);

    public ConfigurationService(
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        
        // Initialize default configuration
        _defaultConfig = new MeetingConfiguration
        {
            SummaryIntervalMinutes = configuration.GetValue<int>("SummarySettings:DefaultIntervalMinutes", 10),
            AutoPostToChat = configuration.GetValue<bool>("SummarySettings:AutoPostToChat", true),
            EnableLateJoinerNotifications = configuration.GetValue<bool>("SummarySettings:EnableLateJoinerNotifications", true),
            RetentionDays = configuration.GetValue<int>("SummarySettings:RetentionDays", 30),
            TranscriptionMethod = configuration.GetValue<TranscriptionMethod>("SummarySettings:TranscriptionMethod", TranscriptionMethod.Polling)
        };

        var databaseName = configuration["CosmosDb:DatabaseName"] ?? "MeetingSummaries";
        var containerName = configuration["CosmosDb:ConfigContainerName"] ?? "configurations";
        
        _container = cosmosClient.GetContainer(databaseName, containerName);
    }

    public async Task<MeetingConfiguration> GetMeetingConfigAsync(string meetingId)
    {
        // Check cache first
        if (_cache.TryGetValue(meetingId, out var cachedConfig))
        {
            _logger.LogDebug(
                "Retrieved configuration for meeting {MeetingId} from cache",
                meetingId);
            return cachedConfig;
        }

        try
        {
            // Try to retrieve from Cosmos DB
            var response = await _container.ReadItemAsync<MeetingConfigurationDocument>(
                meetingId,
                new PartitionKey(meetingId));

            var config = response.Resource.Configuration;
            
            // Update cache
            _cache.TryAdd(meetingId, config);

            _logger.LogInformation(
                "Retrieved configuration for meeting {MeetingId} from storage",
                meetingId);

            return config;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Meeting config not found, return default
            _logger.LogInformation(
                "No configuration found for meeting {MeetingId}, using default configuration",
                meetingId);

            var defaultConfigCopy = new MeetingConfiguration
            {
                SummaryIntervalMinutes = _defaultConfig.SummaryIntervalMinutes,
                AutoPostToChat = _defaultConfig.AutoPostToChat,
                EnableLateJoinerNotifications = _defaultConfig.EnableLateJoinerNotifications,
                RetentionDays = _defaultConfig.RetentionDays,
                TranscriptionMethod = _defaultConfig.TranscriptionMethod
            };

            // Cache the default config for this meeting
            _cache.TryAdd(meetingId, defaultConfigCopy);

            return defaultConfigCopy;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving configuration for meeting {MeetingId}, using default",
                meetingId);

            // Return default config on error
            return new MeetingConfiguration
            {
                SummaryIntervalMinutes = _defaultConfig.SummaryIntervalMinutes,
                AutoPostToChat = _defaultConfig.AutoPostToChat,
                EnableLateJoinerNotifications = _defaultConfig.EnableLateJoinerNotifications,
                RetentionDays = _defaultConfig.RetentionDays,
                TranscriptionMethod = _defaultConfig.TranscriptionMethod
            };
        }
    }

    public async Task UpdateMeetingConfigAsync(string meetingId, MeetingConfiguration config)
    {
        await _updateLock.WaitAsync();
        try
        {
            // Validate configuration
            if (config.SummaryIntervalMinutes < 5 || config.SummaryIntervalMinutes > 30)
            {
                throw new ArgumentException(
                    "Summary interval must be between 5 and 30 minutes",
                    nameof(config.SummaryIntervalMinutes));
            }

            if (config.RetentionDays < 30 || config.RetentionDays > 365)
            {
                throw new ArgumentException(
                    "Retention days must be between 30 and 365",
                    nameof(config.RetentionDays));
            }

            var configDocument = new MeetingConfigurationDocument
            {
                Id = meetingId,
                MeetingId = meetingId,
                Configuration = config,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                // Upsert to Cosmos DB
                await _container.UpsertItemAsync(
                    configDocument,
                    new PartitionKey(meetingId));

                _logger.LogInformation(
                    "Updated configuration for meeting {MeetingId} in storage",
                    meetingId);
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to persist configuration for meeting {MeetingId} to storage, updating cache only",
                    meetingId);
            }

            // Update cache (always update cache even if storage fails)
            _cache.AddOrUpdate(
                meetingId,
                config,
                (key, oldValue) => config);

            _logger.LogInformation(
                "Configuration for meeting {MeetingId} updated in cache. " +
                "Interval: {Interval}min, AutoPost: {AutoPost}, LateJoiner: {LateJoiner}, Retention: {Retention}days, Method: {Method}",
                meetingId,
                config.SummaryIntervalMinutes,
                config.AutoPostToChat,
                config.EnableLateJoinerNotifications,
                config.RetentionDays,
                config.TranscriptionMethod);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private class MeetingConfigurationDocument
    {
        public string Id { get; set; } = string.Empty;
        public string MeetingId { get; set; } = string.Empty;
        public MeetingConfiguration Configuration { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
    }
}

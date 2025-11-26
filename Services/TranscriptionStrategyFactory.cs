using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

/// <summary>
/// Factory for creating transcription strategy instances based on configuration
/// </summary>
public class TranscriptionStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranscriptionStrategyFactory> _logger;

    public TranscriptionStrategyFactory(
        IServiceProvider serviceProvider,
        ILogger<TranscriptionStrategyFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public ITranscriptionStrategy CreateStrategy(TranscriptionMethod method)
    {
        _logger.LogInformation("Creating transcription strategy for method: {Method}", method);

        return method switch
        {
            TranscriptionMethod.Polling => _serviceProvider.GetRequiredService<PollingTranscriptionStrategy>(),
            TranscriptionMethod.Webhook => _serviceProvider.GetRequiredService<WebhookTranscriptionStrategy>(),
            _ => throw new ArgumentException($"Unknown transcription method: {method}", nameof(method))
        };
    }
}

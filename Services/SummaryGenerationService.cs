using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using System.Text;
using System.Text.Json;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Models;

namespace TeamsMeetingBot.Services;

public class SummaryGenerationService : ISummaryGenerationService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly ILogger<SummaryGenerationService> _logger;
    private readonly string _deploymentName;
    private const int MaxTokensPerRequest = 8000;
    private const int RetryDelaySeconds = 30;

    public SummaryGenerationService(
        AzureOpenAIClient openAIClient,
        IConfiguration configuration,
        ILogger<SummaryGenerationService> logger)
    {
        _openAIClient = openAIClient;
        _logger = logger;
        _deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4-turbo";
    }

    public async Task<MeetingSummary> GenerateSummaryAsync(
        IEnumerable<TranscriptionSegment> segments,
        SummaryOptions options)
    {
        if (segments == null || !segments.Any())
        {
            throw new ArgumentException("Transcription segments cannot be null or empty", nameof(segments));
        }

        try
        {
            _logger.LogInformation("Starting summary generation for {SegmentCount} segments", segments.Count());

            // Build transcription text from segments
            var transcriptionText = BuildTranscriptionText(segments);

            // Manage token limits
            transcriptionText = ManageTokenLimit(transcriptionText, MaxTokensPerRequest);

            // Generate summary with retry logic
            var summaryResponse = await GenerateSummaryWithRetryAsync(transcriptionText, options);

            // Parse response into MeetingSummary object
            var meetingSummary = ParseSummaryResponse(summaryResponse, segments);

            _logger.LogInformation("Successfully generated summary with {TopicCount} topics and {ActionItemCount} action items",
                meetingSummary.KeyTopics.Count, meetingSummary.ActionItems.Count);

            return meetingSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate summary after all retry attempts");
            throw;
        }
    }

    private string BuildTranscriptionText(IEnumerable<TranscriptionSegment> segments)
    {
        var sb = new StringBuilder();
        
        foreach (var segment in segments.OrderBy(s => s.Timestamp))
        {
            var speakerName = !string.IsNullOrEmpty(segment.SpeakerName) 
                ? segment.SpeakerName 
                : $"Speaker {segment.SpeakerId}";
            
            sb.AppendLine($"[{segment.Timestamp:HH:mm:ss}] {speakerName}: {segment.Text}");
        }

        return sb.ToString();
    }

    private string ManageTokenLimit(string text, int maxTokens)
    {
        // Rough estimation: 1 token ≈ 4 characters
        var estimatedTokens = text.Length / 4;

        if (estimatedTokens <= maxTokens)
        {
            return text;
        }

        _logger.LogWarning("Transcription exceeds token limit. Estimated tokens: {EstimatedTokens}, Max: {MaxTokens}. Truncating...",
            estimatedTokens, maxTokens);

        // Truncate to fit within token limit (leaving room for prompt and response)
        var maxChars = (maxTokens - 1000) * 4; // Reserve 1000 tokens for prompt and response
        return text.Substring(0, Math.Min(text.Length, maxChars)) + "\n[Transcription truncated due to length]";
    }

    private async Task<string> GenerateSummaryWithRetryAsync(string transcriptionText, SummaryOptions options)
    {
        try
        {
            return await CallOpenAIAsync(transcriptionText, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Summary generation failed, retrying in {RetryDelaySeconds} seconds", RetryDelaySeconds);
            
            await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));

            try
            {
                return await CallOpenAIAsync(transcriptionText, options);
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Summary generation failed after retry");
                throw;
            }
        }
    }

    private async Task<string> CallOpenAIAsync(string transcriptionText, SummaryOptions options)
    {
        var chatClient = _openAIClient.GetChatClient(_deploymentName);

        var prompt = BuildPromptTemplate(transcriptionText, options);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are an AI assistant that analyzes meeting transcriptions and generates structured summaries."),
            new UserChatMessage(prompt)
        };

        var chatOptions = new ChatCompletionOptions
        {
            Temperature = (float)options.Temperature,
            MaxOutputTokenCount = 2000
        };

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, chatOptions);
            
            if (response?.Value?.Content == null || response.Value.Content.Count == 0)
            {
                throw new InvalidOperationException("OpenAI returned empty response");
            }

            return response.Value.Content[0].Text;
        }
        catch (RequestFailedException ex) when (ex.Status == 429)
        {
            _logger.LogError(ex, "OpenAI rate limit exceeded");
            throw new InvalidOperationException("OpenAI rate limit exceeded. Please try again later.", ex);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == "context_length_exceeded")
        {
            _logger.LogError(ex, "Token limit exceeded for OpenAI request");
            throw new InvalidOperationException("Transcription is too long to process. Token limit exceeded.", ex);
        }
    }

    private string BuildPromptTemplate(string transcriptionText, SummaryOptions options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze the following meeting transcription and provide:");
        sb.AppendLine();

        if (options.IncludeKeyTopics)
        {
            sb.AppendLine("1. A concise summary (3-5 sentences)");
            sb.AppendLine("2. Key topics discussed (bullet points)");
        }

        if (options.IncludeDecisions)
        {
            sb.AppendLine("3. Decisions made (bullet points)");
        }

        if (options.IncludeActionItems)
        {
            sb.AppendLine("4. Action items with assignees if mentioned (bullet points)");
        }

        sb.AppendLine();
        sb.AppendLine("Transcription:");
        sb.AppendLine(transcriptionText);
        sb.AppendLine();
        sb.AppendLine("Format the response as JSON with the following structure:");
        sb.AppendLine("{");
        sb.AppendLine("  \"summary\": \"string\",");
        sb.AppendLine("  \"keyTopics\": [\"string\"],");
        sb.AppendLine("  \"decisions\": [\"string\"],");
        sb.AppendLine("  \"actionItems\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"description\": \"string\",");
        sb.AppendLine("      \"assignedTo\": \"string\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private MeetingSummary ParseSummaryResponse(string responseText, IEnumerable<TranscriptionSegment> segments)
    {
        try
        {
            // Extract JSON from response (in case there's additional text)
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1)
            {
                throw new InvalidOperationException("No valid JSON found in OpenAI response");
            }

            var jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsedResponse = JsonSerializer.Deserialize<SummaryResponse>(jsonText, jsonOptions);

            if (parsedResponse == null)
            {
                throw new InvalidOperationException("Failed to deserialize OpenAI response");
            }

            var orderedSegments = segments.OrderBy(s => s.Timestamp).ToList();
            var startTime = orderedSegments.First().Timestamp;
            var endTime = orderedSegments.Last().Timestamp;

            return new MeetingSummary
            {
                Id = Guid.NewGuid().ToString(),
                StartTime = startTime,
                EndTime = endTime,
                Content = parsedResponse.Summary ?? string.Empty,
                KeyTopics = parsedResponse.KeyTopics ?? new List<string>(),
                Decisions = parsedResponse.Decisions ?? new List<string>(),
                ActionItems = parsedResponse.ActionItems?.Select(ai => new ActionItem
                {
                    Description = ai.Description ?? string.Empty,
                    AssignedTo = ai.AssignedTo ?? string.Empty
                }).ToList() ?? new List<ActionItem>()
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenAI response as JSON. Response: {Response}", responseText);
            throw new InvalidOperationException("Failed to parse summary response from OpenAI", ex);
        }
    }

    private class SummaryResponse
    {
        public string? Summary { get; set; }
        public List<string>? KeyTopics { get; set; }
        public List<string>? Decisions { get; set; }
        public List<ActionItemResponse>? ActionItems { get; set; }
    }

    private class ActionItemResponse
    {
        public string? Description { get; set; }
        public string? AssignedTo { get; set; }
    }
}

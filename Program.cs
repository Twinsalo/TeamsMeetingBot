using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Azure.Cosmos;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using TeamsMeetingBot.Handlers;
using TeamsMeetingBot.Interfaces;
using TeamsMeetingBot.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TeamsMeetingBot")
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Conditional(
        evt => !string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]),
        wt => wt.ApplicationInsights(
            builder.Configuration["ApplicationInsights:ConnectionString"]!,
            new TraceTelemetryConverter(),
            LogEventLevel.Information))
    .CreateLogger();

builder.Host.UseSerilog();

Log.Information("Starting TeamsMeetingBot application");

// Add Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    options.EnableAdaptiveSampling = true;
    options.EnableQuickPulseMetricStream = true;
});

// Add controllers
builder.Services.AddControllers();

// Configure Bot Framework Authentication
builder.Services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();

// Add Bot Framework services with error handling
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();
builder.Services.AddTransient<IBot, MeetingBotActivityHandler>();

// Register authentication and authorization services (must be registered before services that depend on them)
builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();

// Configure Cosmos DB client with connection policy
builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var endpointUrl = configuration["CosmosDb:EndpointUrl"];
    var primaryKey = configuration["CosmosDb:PrimaryKey"];
    
    if (string.IsNullOrEmpty(endpointUrl))
    {
        throw new InvalidOperationException("CosmosDb:EndpointUrl is not configured");
    }
    
    if (string.IsNullOrEmpty(primaryKey))
    {
        throw new InvalidOperationException("CosmosDb:PrimaryKey is not configured");
    }
    
    var cosmosClientOptions = new CosmosClientOptions
    {
        ApplicationName = "TeamsMeetingBot",
        ConnectionMode = ConnectionMode.Direct,
        MaxRetryAttemptsOnRateLimitedRequests = 3,
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(10)
    };
    
    logger.LogInformation("Initializing Cosmos DB client for endpoint {EndpointUrl}", endpointUrl);
    
    return new CosmosClient(endpointUrl, primaryKey, cosmosClientOptions);
});

// Configure Azure OpenAI client
builder.Services.AddSingleton<AzureOpenAIClient>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var endpoint = configuration["AzureOpenAI:Endpoint"];
    var apiKey = configuration["AzureOpenAI:ApiKey"];
    
    if (string.IsNullOrEmpty(endpoint))
    {
        throw new InvalidOperationException("AzureOpenAI:Endpoint is not configured");
    }
    
    logger.LogInformation("Initializing Azure OpenAI client for endpoint {Endpoint}", endpoint);
    
    // Use API key if provided, otherwise use DefaultAzureCredential (Managed Identity)
    if (!string.IsNullOrEmpty(apiKey))
    {
        return new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
    }
    else
    {
        logger.LogInformation("Using DefaultAzureCredential for Azure OpenAI authentication");
        return new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    }
});

// Register application services with appropriate lifetimes
// Singleton: Services that maintain state or are expensive to create
builder.Services.AddSingleton<ITelemetryService, TelemetryService>();
builder.Services.AddSingleton<ITranscriptionBufferService, TranscriptionBufferService>();
builder.Services.AddSingleton<IGraphApiService, GraphApiService>();
builder.Services.AddSingleton<ISummaryGenerationService, SummaryGenerationService>();
builder.Services.AddSingleton<ISummaryStorageService, SummaryStorageService>();
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<ILateJoinerService, LateJoinerService>();

// Register Graph subscription service for webhook-based transcription
builder.Services.AddSingleton<GraphSubscriptionService>();

// Register transcription strategies
builder.Services.AddSingleton<PollingTranscriptionStrategy>();
builder.Services.AddSingleton<WebhookTranscriptionStrategy>();
builder.Services.AddSingleton<TranscriptionStrategyFactory>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme ?? "http");
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        diagnosticContext.Set("UserAgent", !string.IsNullOrEmpty(userAgent) ? userAgent : "Unknown");
        
        // Add correlation ID for request tracing
        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            var correlationIdValue = correlationId.ToString();
            diagnosticContext.Set("CorrelationId", !string.IsNullOrEmpty(correlationIdValue) ? correlationIdValue : Guid.NewGuid().ToString());
        }
        else
        {
            var newCorrelationId = Guid.NewGuid().ToString();
            httpContext.Items["CorrelationId"] = newCorrelationId;
            diagnosticContext.Set("CorrelationId", newCorrelationId);
        }
    };
});

app.UseHttpsRedirection();
app.UseRouting();
app.MapControllers();

try
{
    Log.Information("TeamsMeetingBot application started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

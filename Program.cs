using OmgekeerdeStemWijzer.Api.Models;
using OmgekeerdeStemWijzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

// Configure Serilog before building the app
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/app-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

try
{
    Log.Information("Starting OmgekeerdeStemWijzer API");

var builder = WebApplication.CreateBuilder(args);

// Use Serilog for logging
builder.Host.UseSerilog();

builder.Services.AddOptions<GroqOptions>()
    .Bind(builder.Configuration.GetSection("Groq"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection("OpenAI"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var groqOptions = builder.Configuration.GetSection("Groq").Get<GroqOptions>() ?? new GroqOptions();
var groqApiKey = groqOptions.ApiKey ?? string.Empty;
var groqBaseUrl = groqOptions.BaseUrl ?? "https://api.groq.com/openai/v1";
// If the configured model is empty or whitespace, fall back to a sane default (llama-3.1-8b-instant)
var groqModel = string.IsNullOrWhiteSpace(groqOptions.Model) ? "llama-3.1-8b-instant" : groqOptions.Model;
var openAIOptions = builder.Configuration.GetSection("OpenAI").Get<OpenAIOptions>() ?? new OpenAIOptions();
var openAIApiKey = openAIOptions.ApiKey ?? string.Empty;
// Treat empty/whitespace embedding model as missing and use the recommended default
var openAIEmbeddingModel = string.IsNullOrWhiteSpace(openAIOptions.EmbeddingModel) ? "text-embedding-3-small" : openAIOptions.EmbeddingModel;
var chromaDbUrl = builder.Configuration.GetSection("ServiceUrls:ChromaDb").Value ?? "http://localhost:8000";

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    => HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => (int)msg.StatusCode == 429)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

builder.Services.AddHttpClient<GroqService>("groq", (sp, client) =>
{
    var groqOptions = sp.GetRequiredService<IOptions<GroqOptions>>().Value;
    var baseUrl = groqOptions.BaseUrl.EndsWith('/') ? groqOptions.BaseUrl : groqOptions.BaseUrl + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
})
    .AddPolicyHandler(GetRetryPolicy());

builder.Services.AddScoped<EmbeddingService>();

var chromaApiKey = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKey")
    ?? Environment.GetEnvironmentVariable("Chroma__ApiKey");
var chromaApiKeyHeader = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKeyHeader") ?? "Authorization";
var chromaApiKeyScheme = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKeyScheme") ?? "Bearer";

builder.Services.AddHttpClient("chroma", client =>
{
    var baseUrl = chromaDbUrl.EndsWith('/') ? chromaDbUrl : chromaDbUrl + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);

    if (!string.IsNullOrWhiteSpace(chromaApiKey))
    {
        // If header name is Authorization, use the typed Authorization header with a scheme
        if (string.Equals(chromaApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(chromaApiKeyScheme, chromaApiKey);
        }
        else
        {
            client.DefaultRequestHeaders.Remove(chromaApiKeyHeader);
            client.DefaultRequestHeaders.Add(chromaApiKeyHeader, chromaApiKey);
        }
    }
});

builder.Services.AddSingleton<IChromaClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var client = factory.CreateClient("chroma");
    return new ChromaHttpClient(client);
});

builder.Services.AddSingleton<VectorStoreService>();

builder.Services.AddHostedService<StartupInitializer>();

builder.Services.AddHealthChecks()
    .AddCheck<VectorStoreHealthCheck>("vectorstore");

builder.Services.AddSingleton<DocumentProcessor>();

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

app.UseCors();

// Enable Swagger in Development, or in Production when explicitly allowed via configuration.
// Set configuration key `Swagger:EnableInProduction=true` (or env var `Swagger__EnableInProduction=true`) to enable in prod.
var enableSwaggerInProd = builder.Configuration.GetSection("Swagger").GetValue<bool>("EnableInProduction", false);
if (app.Environment.IsDevelopment() || enableSwaggerInProd)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

    Log.Information("Application shut down gracefully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

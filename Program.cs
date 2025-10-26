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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Google.Cloud.Firestore;

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
// sanitize API key values - remove whitespace and any new-line characters which are invalid in headers
openAIApiKey = openAIApiKey.Trim();
var safeOpenAIApiKey = openAIApiKey.Replace("\r", string.Empty).Replace("\n", string.Empty);
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

builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
    if (!string.IsNullOrWhiteSpace(safeOpenAIApiKey))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", safeOpenAIApiKey);
    }
});

var chromaApiKey = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKey")
    ?? Environment.GetEnvironmentVariable("Chroma__ApiKey");
chromaApiKey = (chromaApiKey ?? string.Empty).Trim();
var safeChromaApiKey = chromaApiKey.Replace("\r", string.Empty).Replace("\n", string.Empty);
// Default header for Chroma v2 is x-chroma-token; can be overridden via config
var chromaApiKeyHeader = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKeyHeader") ?? "x-chroma-token";
var chromaApiKeyScheme = builder.Configuration.GetSection("Chroma").GetValue<string>("ApiKeyScheme") ?? "Bearer";

// Configure a typed HttpClient for IChromaClient so ChromaHttpClient always receives the correctly configured instance.
var chromaClientBuilder = builder.Services.AddHttpClient<IChromaClient, ChromaHttpClient>(client =>
{
    var baseUrl = chromaDbUrl.EndsWith('/') ? chromaDbUrl : chromaDbUrl + "/";
    // Keep it simple: always target v2 under the provided base URL
    baseUrl = baseUrl + "api/v2/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);

    if (!string.IsNullOrWhiteSpace(safeChromaApiKey))
    {
        if (string.Equals(chromaApiKeyHeader, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(chromaApiKeyScheme, safeChromaApiKey);
        }
        else
        {
            // For x-chroma-token, the value should be the raw token
            client.DefaultRequestHeaders.Remove(chromaApiKeyHeader);
            client.DefaultRequestHeaders.Add(chromaApiKeyHeader, safeChromaApiKey);
        }
    }
});

// --- Firebase JwtBearer authentication (validate Firebase ID tokens) ---
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? builder.Configuration["Google:ProjectId"]
    ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");

if (!string.IsNullOrWhiteSpace(firebaseProjectId))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://securetoken.google.com/{firebaseProjectId}";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://securetoken.google.com/{firebaseProjectId}",
                ValidateAudience = true,
                ValidAudience = firebaseProjectId,
                ValidateLifetime = true
            };
        });
}

// --- Firestore ---
var gcpProjectId = builder.Configuration["Google:ProjectId"]
    ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT");
if (!string.IsNullOrWhiteSpace(gcpProjectId))
{
    builder.Services.AddSingleton(sp => FirestoreDb.Create(gcpProjectId));
}

builder.Services.AddTransient<IChatHistoryService, ChatHistoryService>();

builder.Services.AddSingleton<IVectorStoreService, VectorStoreService>();

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

var enableSwaggerInProd = builder.Configuration.GetSection("Swagger").GetValue<bool>("EnableInProduction", false);
if (app.Environment.IsDevelopment() || enableSwaggerInProd)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Enable auth if configured
app.UseAuthentication();
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

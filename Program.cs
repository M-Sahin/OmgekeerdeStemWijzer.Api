using OmgekeerdeStemWijzer.Api.Models;
using OmgekeerdeStemWijzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.Extensions.Http;
using System;
using System.Net.Http;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// === 1. Configuratie Ophalen ===
// Bind typed options and validate on start
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
var groqModel = groqOptions.Model ?? "groq/compound";
var openAIOptions = builder.Configuration.GetSection("OpenAI").Get<OpenAIOptions>() ?? new OpenAIOptions();
var openAIApiKey = openAIOptions.ApiKey ?? string.Empty;
var openAIEmbeddingModel = openAIOptions.EmbeddingModel ?? "text-embedding-3-small";
var chromaDbUrl = builder.Configuration.GetSection("ServiceUrls:ChromaDb").Value ?? "http://localhost:8000";

// === 2. Services Toevoegen ===

// Voeg de services toe voor Dependency Injection

// Groq Service: register using IHttpClientFactory so the service receives a named HttpClient
// Register a named HttpClient for Groq with the configured base URL
// Polly policies
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    => HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => (int)msg.StatusCode == 429)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

// Circuit breaker removed for Groq client to avoid blocking application after provider-side 500s.
// If you want a circuit breaker, scope it narrowly (e.g., only 429), or shorten break duration.
//static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
//    => HttpPolicyExtensions
//        .HandleTransientHttpError()
//        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient("groq", client =>
{
    // Ensure trailing slash so relative paths append after /v1/
    var baseUrl = groqBaseUrl.EndsWith('/') ? groqBaseUrl : groqBaseUrl + "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
})
    .AddPolicyHandler(GetRetryPolicy());

builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient("groq");
    var logger = sp.GetRequiredService<ILogger<OmgekeerdeStemWijzer.Api.Services.GroqService>>();

    if (string.IsNullOrEmpty(groqApiKey))
    {
        throw new InvalidOperationException("Groq API key is not configured (check appsettings or user secrets).");
    }

    return new OmgekeerdeStemWijzer.Api.Services.GroqService(httpClient, groqApiKey, logger, groqModel);
});

// OpenAI Embedding Service
builder.Services.AddScoped<EmbeddingService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
    
    if (string.IsNullOrEmpty(openAIApiKey))
    {
        throw new InvalidOperationException("OpenAI API key is not configured (check appsettings or user secrets).");
    }
    
    return new EmbeddingService(openAIApiKey, openAIEmbeddingModel, logger);
});

// ChromaDB Vector Store Service
builder.Services.AddSingleton(provider => new VectorStoreService(chromaDbUrl));

// Hosted initializer to run async startup tasks (instead of blocking .Wait())
builder.Services.AddHostedService<StartupInitializer>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<VectorStoreHealthCheck>("vectorstore");

// Document Processor Service (vereist geen URL, is pure logica)
builder.Services.AddSingleton<DocumentProcessor>();

// Voeg Controllers toe
builder.Services.AddControllers();

// Voeg de vereiste Swagger/OpenAPI functionaliteit toe
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

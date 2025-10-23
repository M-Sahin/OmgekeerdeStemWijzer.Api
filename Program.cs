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

builder.Services.AddHttpClient("groq", client =>
{
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

builder.Services.AddScoped<EmbeddingService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
    
    if (string.IsNullOrEmpty(openAIApiKey))
    {
        throw new InvalidOperationException("OpenAI API key is not configured (check appsettings or user secrets).");
    }
    
    return new EmbeddingService(openAIApiKey, openAIEmbeddingModel, logger);
});

builder.Services.AddSingleton(provider => new VectorStoreService(chromaDbUrl));

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

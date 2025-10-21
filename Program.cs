using OmgekeerdeStemWijzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// === 1. Configuratie Ophalen ===
var groqApiKey = builder.Configuration.GetSection("Groq:ApiKey").Value ?? throw new InvalidOperationException("Groq:ApiKey is niet geconfigureerd.");
var ollamaUrl = builder.Configuration.GetSection("ServiceUrls:Ollama").Value ?? "http://localhost:11434";
var chromaDbUrl = builder.Configuration.GetSection("ServiceUrls:ChromaDb").Value ?? "http://localhost:8000";

// === 2. Services Toevoegen ===

// Voeg de services toe voor Dependency Injection

// Groq Service
builder.Services.AddSingleton(new GroqService(groqApiKey));

// Ollama Embedding Service (optimized implementation renamed to EmbeddingService)
builder.Services.AddHttpClient();
builder.Services.AddScoped<EmbeddingService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient();
    var logger = sp.GetRequiredService<ILogger<EmbeddingService>>();
    return new EmbeddingService(ollamaUrl, httpClient, logger);
});

// ChromaDB Vector Store Service
builder.Services.AddSingleton(provider =>
{
    var service = new VectorStoreService(chromaDbUrl);
    // Initialiseer de ChromaDB connectie direct bij het starten
    service.InitializeAsync().Wait(); 
    return service;
});

// Document Processor Service (vereist geen URL, is pure logica)
builder.Services.AddSingleton<DocumentProcessor>();

// Voeg Controllers toe
builder.Services.AddControllers();

// Voeg de vereiste Swagger/OpenAPI functionaliteit toe
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var vectorSvc = scope.ServiceProvider.GetRequiredService<VectorStoreService>();
    await vectorSvc.InitializeAsync(); 
}

// === 3. Middleware Pijplijn ===

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

namespace OmgekeerdeStemWijzer.Api.Services
{
    // Minimal GroqService implementation to satisfy DI registration.
    // Expand this with real API calls as needed.
    public class GroqService
    {
        private readonly string _apiKey;

        public GroqService(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        }

        public Task<string> QueryAsync(string query)
        {
            // Placeholder implementation; replace with actual HTTP call to Groq API.
            return Task.FromResult(string.Empty);
        }
    }
}

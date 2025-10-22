using OmgekeerdeStemWijzer.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// === 1. Configuratie Ophalen ===
var groqApiKey = builder.Configuration.GetSection("Groq:ApiKey").Value ?? string.Empty;
var groqBaseUrl = builder.Configuration.GetSection("Groq:BaseUrl").Value ?? "https://api.groq.com/openai/v1";
var groqModel = builder.Configuration.GetSection("Groq:Model").Value ?? "groq/compound";
var ollamaUrl = builder.Configuration.GetSection("ServiceUrls:Ollama").Value ?? "http://localhost:11434";
var chromaDbUrl = builder.Configuration.GetSection("ServiceUrls:ChromaDb").Value ?? "http://localhost:8000";

// === 2. Services Toevoegen ===

// Voeg de services toe voor Dependency Injection

// Groq Service: register using IHttpClientFactory so the service receives an HttpClient
// Register a named HttpClient for Groq with the configured base URL
builder.Services.AddHttpClient("groq", client =>
{
    client.BaseAddress = new Uri(groqBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddSingleton<OmgekeerdeStemWijzer.Api.Services.GroqService>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient("groq");
    var logger = sp.GetRequiredService<ILogger<OmgekeerdeStemWijzer.Api.Services.GroqService>>();
    return new OmgekeerdeStemWijzer.Api.Services.GroqService(httpClient, groqApiKey, logger, groqModel);
});

// Ollama Embedding Service
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

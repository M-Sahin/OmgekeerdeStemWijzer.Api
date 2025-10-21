using Microsoft.AspNetCore.Mvc;
using OmgekeerdeStemWijzer.Api.Services;

namespace OmgekeerdeStemWijzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly DocumentProcessor _processor;
    private readonly VectorStoreService _vectorStore;
    private readonly EmbeddingService _embedder;
    private readonly ILogger<IngestionController> _logger;
    private const string ManifestsDir = "Data/Manifesten";

    public IngestionController(
        DocumentProcessor processor,
        VectorStoreService vectorStore,
        EmbeddingService embedder,
        ILogger<IngestionController> logger)
    {
        _processor = processor;
        _vectorStore = vectorStore;
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// Indexeert alle PDF manifesten in de Data/Manifesten map in ChromaDB.
    /// Dit is een eenmalige bewerking.
    /// </summary>
    [HttpPost("start-indexing")]
    public async Task<IActionResult> StartIndexing()
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), ManifestsDir);
        if (!Directory.Exists(basePath))
        {
            return NotFound($"De map '{ManifestsDir}' is niet gevonden. Plaats hier de PDF's.");
        }

        var pdfFiles = Directory.GetFiles(basePath, "*.pdf");
        if (!pdfFiles.Any())
        {
            return NotFound($"Geen PDF-bestanden gevonden in '{ManifestsDir}'.");
        }

        int totalChunks = 0;
        
        // Loop door elk bestand
        foreach (var filePath in pdfFiles)
        {
            var partyName = Path.GetFileNameWithoutExtension(filePath);
            _logger.LogInformation("Start verwerking van manifest: {Party}", partyName);

            // 1. CHUNK de tekst
            var chunks = _processor.ProcessPdf(filePath, partyName);

            // 2. EMBED en ADD naar ChromaDB
            foreach (var chunk in chunks)
            {
                try
                {
                    // Genereer de vector (dit is de zware stap)
                    float[] embedding = await _embedder.GenerateEmbeddingAsync(chunk.Content);
                    
                    if (embedding.Length > 0)
                    {
                        // Sla de vector + chunk op in ChromaDB
                        await _vectorStore.AddChunkAsync(chunk, embedding);
                        totalChunks++;
                    }
                }
                catch (Exception ex)
                {
                    // Belangrijk om fouten op te vangen als Ollama/ChromaDB even niet reageert
                    _logger.LogError(ex, "Fout bij verwerken chunk {Id} voor partij {Party}", chunk.Id, partyName);
                }
            }
            _logger.LogInformation("Voltooide verwerking voor {Party}. {Count} chunks toegevoegd.", partyName, chunks.Count());
        }

        return Ok(new { Message = $"Success! {totalChunks} chunks geïndexeerd in ChromaDB.", TotalFiles = pdfFiles.Length });
    }
}
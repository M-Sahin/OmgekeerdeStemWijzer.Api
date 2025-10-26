using Microsoft.AspNetCore.Mvc;
using OmgekeerdeStemWijzer.Api.Services;

namespace OmgekeerdeStemWijzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly DocumentProcessor _processor;
    private readonly IVectorStoreService _vectorStore;
    private readonly EmbeddingService _embedder;
    private readonly ILogger<IngestionController> _logger;
    private const string ManifestsDir = "Data/Manifesten";
    private const string CollectionName = "verkiezingsprogrammas";

    public IngestionController(
        DocumentProcessor processor,
        IVectorStoreService vectorStore,
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

        try
        {
            // STEP 1: Ensure collection exists before processing any chunks
            _logger.LogInformation("Controleren of collectie '{CollectionName}' bestaat of aanmaken...", CollectionName);
            await _vectorStore.GetOrCreateCollectionAsync(CollectionName);
            _logger.LogInformation("Collectie '{CollectionName}' is beschikbaar.", CollectionName);

            int totalChunks = 0;
            
            // STEP 2: Process chunks and add them to the existing collection
            foreach (var filePath in pdfFiles)
            {
                var partyName = Path.GetFileNameWithoutExtension(filePath);
                _logger.LogInformation("Start verwerking van manifest: {Party}", partyName);

                var chunks = _processor.ProcessPdf(filePath, partyName);

                foreach (var chunk in chunks)
                {
                    try
                    {
                        float[] embedding = await _embedder.GenerateEmbeddingAsync(chunk.Content);
                        
                        if (embedding.Length > 0)
                        {
                            await _vectorStore.AddChunkAsync(CollectionName, chunk, embedding);
                            totalChunks++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fout bij verwerken chunk {Id} voor partij {Party}", chunk.Id, partyName);
                    }
                }
                _logger.LogInformation("Voltooide verwerking voor {Party}. {Count} chunks toegevoegd.", partyName, chunks.Count());
            }

            return Ok(new { Message = $"Success! {totalChunks} chunks geïndexeerd in ChromaDB.", TotalFiles = pdfFiles.Length });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Algemene fout tijdens indexering.");
            return StatusCode(500, $"Interne serverfout: {ex.Message}");
        }
    }
}
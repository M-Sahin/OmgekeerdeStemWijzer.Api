using Microsoft.AspNetCore.Mvc;
using OmgekeerdeStemWijzer.Api.Services;
using System.Threading.Tasks;

[ApiController]
[Route("api/[controller]")]
public class MatchingController : ControllerBase
{
    private readonly EmbeddingService _embeddingService;
    private readonly VectorStoreService _vectorStoreService;
    private readonly GroqService _groqService;

    public MatchingController(
        EmbeddingService embeddingService,
        VectorStoreService vectorStoreService,
        GroqService groqService)
    {
        _embeddingService = embeddingService;
        _vectorStoreService = vectorStoreService;
        _groqService = groqService;
    }

    /// <summary>
    /// Voert de RAG-workflow uit: zoekt naar manifesten die overeenkomen met de gebruikersvraag en laat de LLM het antwoord genereren.
    /// </summary>
    /// <param name="userQuery">De politieke stelling van de gebruiker (bijv. "Ik wil lagere belastingen").</param>
    /// <returns>Het geanalyseerde antwoord van de LLM inclusief bronnen.</returns>
    [HttpPost("match")]
    public async Task<IActionResult> GetMatch([FromBody] string userQuery)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            return BadRequest("De gebruikersvraag mag niet leeg zijn.");
        }

        // STAP 1: Vraag embedding op voor de gebruikersquery
        // De front-end verstuurt de query als een geserialiseerde string, vandaar de [FromBody] string userQuery
        float[] queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery);

        if (queryEmbedding.Length == 0)
        {
            // Foutafhandeling voor de front-end (index.html)
            return StatusCode(503, "Kon geen embedding genereren. Controleer Ollama service.");
        }

        // STAP 2: Zoek relevante chunks in ChromaDB (Retrieval)
        string[] relevantChunks = await _vectorStoreService.QueryRelevantChunksAsync(queryEmbedding, nResults: 5);

        if (relevantChunks.Length == 0)
        {
            // Dit kan gebeuren als ChromaDB leeg is of de query te vaag is.
            return NotFound("Kon geen relevante manifestfragmenten vinden in de database. Zorg ervoor dat de IngestionController is uitgevoerd.");
        }

        // STAP 3: Genereer het antwoord (Augmentation/Generation)
        // De chunks worden toegevoegd als context aan de prompt.
        string context = string.Join("\n\n--- Bron ---\n\n", relevantChunks);
        
        string systemPrompt = $@"Je bent een 'Omgekeerde Stemwijzer' analist. Je taak is om de stelling van de gebruiker (User Query) te analyseren en deze te matchen met de meegeleverde manifestfragmenten (Context).
        Geef een heldere, objectieve samenvatting van welke partijen de stelling het beste ondersteunen, gebaseerd **uitsluitend** op de meegeleverde Context.
        
        FORMAT:
        Begin met een algemene samenvatting (max 3 zinnen).
        Geef daarna per partij in de context aan: 'Partij X: Match: JA/NEE/Neutraal' en citeer de meest relevante zin uit de Context om je oordeel te onderbouwen. Gebruik de Partijnaam in je analyse.
        ";

        string userMessage = $"User Query: {userQuery}\n\nContext:\n{context}";

        string llmResponse;
        try
        {
            llmResponse = await _groqService.QueryAsync(systemPrompt, userMessage);
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                return StatusCode(502, "LLM returned no content. Controleer Groq API en logbestanden.");
            }
        }
        catch
        {
            return StatusCode(502, "Er is een fout opgetreden tijdens het aanroepen van de LLM (Groq).");
        }

        // STAP 4: Geef het resultaat terug aan de front-end
        return Ok(new 
        { 
            UserQuery = userQuery,
            llmAnalysis = llmResponse, 
            retrievedSources = relevantChunks
        });
    }
}
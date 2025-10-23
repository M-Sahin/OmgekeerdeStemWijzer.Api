using Microsoft.AspNetCore.Mvc;
using OmgekeerdeStemWijzer.Api.Services;
using OmgekeerdeStemWijzer.Api.Models;
using System.Threading.Tasks;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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
    public async Task<IActionResult> GetMatch([FromBody] MatchingRequest request)
    {
        if (request?.messages == null || request.messages.Length == 0 || string.IsNullOrWhiteSpace(request.messages[0].content))
        {
            return BadRequest("De gebruikersvraag mag niet leeg zijn.");
        }

        var userQuery = request.messages[0].content!;

        float[] queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(userQuery);

        if (queryEmbedding.Length == 0)
        {
            return StatusCode(503, "Kon geen embedding genereren. Controleer OpenAI API service.");
        }

        string[] relevantChunks = await _vectorStoreService.QueryRelevantChunksAsync(queryEmbedding, nResults: 5);

        if (relevantChunks.Length == 0)
        {
            return NotFound("Kon geen relevante manifestfragmenten vinden in de database. Zorg ervoor dat de IngestionController is uitgevoerd.");
        }

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
            var modelOverride = string.IsNullOrWhiteSpace(request.model) ||
                                string.Equals(request.model?.Trim(), "string", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : request.model?.Trim();

            llmResponse = await _groqService.QueryAsync(systemPrompt, userMessage, modelOverride);
            if (string.IsNullOrWhiteSpace(llmResponse))
            {
                var extra = string.IsNullOrWhiteSpace(_groqService.LastError) ? string.Empty : $" Details: {_groqService.LastError}";
                return StatusCode(502, $"LLM returned no content. Controleer Groq API en logbestanden.{extra}");
            }
        }
        catch
        {
            return StatusCode(502, "Er is een fout opgetreden tijdens het aanroepen van de LLM (Groq).");
        }

        return Ok(new 
        { 
            UserQuery = userQuery,
            llmAnalysis = llmResponse, 
            retrievedSources = relevantChunks
        });
    }
}

public class MatchingRequest
{
    [Required]
    public Message[] messages { get; set; } = System.Array.Empty<Message>();
    
    [DefaultValue("llama-3.1-8b-instant")]
    public string? model { get; set; } = "llama-3.1-8b-instant";
}

public class Message
{
    [Required]
    [DefaultValue("Ik wil lagere belastingen")]
    public string content { get; set; } = string.Empty;
}
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
    private const string CollectionName = "verkiezingsprogrammas";
    
    private readonly EmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStoreService;
    private readonly GroqService _groqService;

    public MatchingController(
        EmbeddingService embeddingService,
        IVectorStoreService vectorStoreService,
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

        string[] relevantChunks = await _vectorStoreService.QueryRelevantChunksAsync(CollectionName, queryEmbedding, nResults: 5);

        if (relevantChunks.Length == 0)
        {
            return NotFound("Kon geen relevante manifestfragmenten vinden in de database. Zorg ervoor dat de IngestionController is uitgevoerd.");
        }

        string context = string.Join("\n\n--- Bron ---\n\n", relevantChunks);
        
        // Limit context size to avoid token limits (system prompt is now much longer)
        if (context.Length > 8000)
        {
            context = context.Substring(0, 8000) + "\n\n[Context ingekort vanwege lengte...]";
        }
        
        string systemPrompt = $@"Je bent een 'Omgekeerde Stemwijzer' analist. Je taak is om de stelling van de gebruiker (User Query) te analyseren en deze te matchen met de meegeleverde manifestfragmenten (Context).
        Geef een heldere, objectieve samenvatting van welke partijen de stelling het beste ondersteunen, gebaseerd **uitsluitend** op de meegeleverde Context.
        
        FORMATTING INSTRUCTIES:
        1. Begin met een algemene samenvatting (max 3 zinnen)
        2. Gebruik duidelijke structuur met partijnamen in **vet**
        3. Geef per partij: Match status (JA/NEE/NEUTRAAL) en citaat
        4. Gebruik bullet points (•) voor overzichtelijkheid
        5. Gebruik Nederlandse interpunctie en spelling

        VOORBEELD FORMAT:
        Op basis van de verkiezingsprogramma's zie ik verschillende standpunten over jouw stelling. Sommige partijen steunen dit volledig, anderen hebben voorbehoud.

        • **VVD (Volkspartij voor Vrijheid en Democratie)**: Match: JA - ""Citaat uit verkiezingsprogramma""
        • **PvdA (Partij van de Arbeid)**: Match: NEE - ""Tegengesteld standpunt uit programma""  
        • **D66 (Democraten 66)**: Match: NEUTRAAL - ""Genuanceerd standpunt""

        BELANGRIJKE INSTRUCTIE: Gebruik ALTIJD de correcte volledige partijnamen. Hier zijn de juiste Nederlandse politieke partijen en hun afkortingen:

        - VVD = Volkspartij voor Vrijheid en Democratie
        - PVV = Partij voor de Vrijheid  
        - CDA = Christen-Democratisch Appèl
        - D66 = Democraten 66
        - GL = GroenLinks
        - SP = Socialistische Partij
        - PvdA = Partij van de Arbeid
        - CU = ChristenUnie
        - PvdD = Partij voor de Dieren
        - 50PLUS = 50PLUS
        - SGP = Staatkundig Gereformeerde Partij
        - DENK = DENK
        - FvD = Forum voor Democratie
        - JA21 = JA21
        - Volt = Volt Nederland
        - BBB = BoerBurgerBeweging
        - NSC = Nieuw Sociaal Contract
        - BVNL = Belang van Nederland

        Gebruik bij het noemen van partijen ALTIJD de correcte volledige naam of de juiste afkorting zoals hierboven aangegeven. Verzin NOOIT nieuwe betekenissen voor afkortingen.
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
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OmgekeerdeStemWijzer.Api.Models;
using OmgekeerdeStemWijzer.Api.Services;

namespace OmgekeerdeStemWijzer.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private const string CollectionName = "verkiezingsprogrammas";
        
        private readonly IChatHistoryService _chatHistory;
        private readonly IVectorStoreService _vectorStore;
        private readonly EmbeddingService _embeddingService;
        private readonly GroqService _groq;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatHistoryService chatHistory, IVectorStoreService vectorStore, EmbeddingService embeddingService, GroqService groq, ILogger<ChatController> logger)
        {
            _chatHistory = chatHistory;
            _vectorStore = vectorStore;
            _embeddingService = embeddingService;
            _groq = groq;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message) || string.IsNullOrWhiteSpace(request.ChatId))
                return BadRequest(new { error = "chatId and message are required" });

            var userId = GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var now = Timestamp.FromDateTime(DateTime.UtcNow);

            var userMsg = new ChatMessage
            {
                Role = "user",
                Content = request.Message,
                Timestamp = now
            };
            await _chatHistory.AddMessageAsync(userId, request.ChatId, userMsg);

            // RAG: embedding -> vector search -> context
            var embedding = await _embeddingService.GenerateEmbeddingAsync(request.Message);
            if (embedding.Length == 0)
            {
                var errorMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = "Sorry, ik kon geen embedding genereren voor je vraag. Controleer de OpenAI API service.",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _chatHistory.AddMessageAsync(userId, request.ChatId, errorMsg);
                return Ok(errorMsg);
            }

            var top = await _vectorStore.QueryRelevantChunksAsync(CollectionName, embedding, nResults: 5);
            if (top.Length == 0)
            {
                var errorMsg = new ChatMessage
                {
                    Role = "assistant", 
                    Content = "Sorry, ik kon geen relevante informatie vinden in de verkiezingsprogramma's voor je vraag. Zorg ervoor dat de manifesten geïndexeerd zijn.",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _chatHistory.AddMessageAsync(userId, request.ChatId, errorMsg);
                return Ok(errorMsg);
            }

            var context = string.Join("\n\n--- Bron ---\n\n", top);
            
            // Limit context size to avoid token limits (system prompt is now much longer)
            if (context.Length > 8000)
            {
                context = context.Substring(0, 8000) + "\n\n[Context ingekort vanwege lengte...]";
            }

            var systemPrompt = @"Je bent een 'Omgekeerde Stemwijzer' assistent. Je helpt gebruikers begrijpen wat Nederlandse politieke partijen denken over verschillende onderwerpen.

Analyseer de vraag van de gebruiker en beantwoord deze op basis van de meegeleverde verkiezingsprogramma fragmenten:

VRAAGTYPEN EN AANPAK:
1. **Informatieve vragen** (""Wat denkt PVV over X?"", ""Hoe staat VVD tegenover Y?"")
   - Geef een directe samenvatting van die partij's standpunt
   - Gebruik citaten uit hun programma
   - Vergelijk eventueel met andere partijen als relevant

2. **Stellingen/voorkeur vragen** (""Welke partij is voor/tegen X?"", ""Wie steunt Y?"")
   - Lijst partijen op die voor/tegen/neutraal zijn
   - Geef hun standpunten met citaten

3. **Vergelijkingsvragen** (""Verschil tussen partij X en Y?"", ""Wat zijn de verschillende standpunten?"")
   - Vergelijk de relevante partijen systematisch
   - Toon overeenkomsten en verschillen

FORMATTING INSTRUCTIES:
- Gebruik bullet points (•) voor duidelijke structuur
- Partijnamen altijd in **vet** met volledige naam
- Citeer relevant uit verkiezingsprogramma's
- Gebruik Nederlandse interpunctie en spelling
- Pas je antwoord aan op het type vraag

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

Gebruik bij het noemen van partijen ALTIJD de correcte volledige naam of de juiste afkorting zoals hierboven aangegeven. Verzin NOOIT nieuwe betekenissen voor afkortingen.";

            var userMessage = $"User Query: {request.Message}\n\nContext:\n{context}";
            var aiContent = await _groq.QueryAsync(systemPrompt, userMessage);

            if (string.IsNullOrWhiteSpace(aiContent))
            {
                var errorDetails = string.IsNullOrWhiteSpace(_groq.LastError) ? "" : $" Details: {_groq.LastError}";
                var errorMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = $"Sorry, er is een probleem opgetreden bij het genereren van een antwoord. Controleer de Groq API service.{errorDetails}",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                };
                await _chatHistory.AddMessageAsync(userId, request.ChatId, errorMsg);
                return Ok(errorMsg);
            }

            var aiMsg = new ChatMessage
            {
                Role = "assistant",
                Content = aiContent,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await _chatHistory.AddMessageAsync(userId, request.ChatId, aiMsg);

            return Ok(aiMsg);
        }

        /// <summary>
        /// Haalt alle chat sessions op voor de ingelogde gebruiker
        /// </summary>
        [HttpGet("history")]
        public async Task<IActionResult> GetChatHistory()
        {
            var userId = GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var chats = await _chatHistory.GetChatHistoryAsync(userId);
            return Ok(chats);
        }

        /// <summary>
        /// Haalt alle berichten op van een specifieke chat
        /// </summary>
        [HttpGet("{chatId}/messages")]
        public async Task<IActionResult> GetChatMessages(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
                return BadRequest(new { error = "chatId is required" });

            var userId = GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();

            var messages = await _chatHistory.GetChatMessagesAsync(userId, chatId);
            return Ok(messages);
        }

        private static string GetUserId(ClaimsPrincipal user)
        {
            // Firebase tokens often carry user_id; JwtBearer maps sub/nameidentifier sometimes
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? user.FindFirst("user_id")?.Value
                   ?? user.FindFirst("sub")?.Value
                   ?? string.Empty;
        }
    }
}

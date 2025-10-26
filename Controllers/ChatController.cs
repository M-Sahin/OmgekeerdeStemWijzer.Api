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

            var systemPrompt = @"Je bent een 'Omgekeerde Stemwijzer' assistent. Je taak is om de stelling van de gebruiker te analyseren en deze te matchen met de meegeleverde manifestfragmenten.
        
Geef een heldere, objectieve samenvatting van welke partijen de stelling het beste ondersteunen, gebaseerd **uitsluitend** op de meegeleverde Context.

Context:
" + context;

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

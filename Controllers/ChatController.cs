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
        private readonly IChatHistoryService _chatHistory;
        private readonly VectorStoreService _vectorStore;
        private readonly EmbeddingService _embeddingService;
        private readonly GroqService _groq;

        public ChatController(IChatHistoryService chatHistory, VectorStoreService vectorStore, EmbeddingService embeddingService, GroqService groq)
        {
            _chatHistory = chatHistory;
            _vectorStore = vectorStore;
            _embeddingService = embeddingService;
            _groq = groq;
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
            var top = await _vectorStore.QueryRelevantChunksAsync(embedding, nResults: 5);
            var context = string.Join("\n---\n", top);

            var systemPrompt = "You are a helpful assistant. Use the provided context to answer the user's question succinctly. If the answer is not in the context, say you are unsure.\n\nContext:\n" + context;
            var aiContent = await _groq.QueryAsync(systemPrompt, request.Message);

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

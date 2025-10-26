using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public interface IChatHistoryService
    {
        Task AddMessageAsync(string userId, string chatId, Models.ChatMessage message);
        Task<IEnumerable<Models.ChatSession>> GetChatHistoryAsync(string userId);
        Task<IEnumerable<Models.ChatMessage>> GetChatMessagesAsync(string userId, string chatId);
    }

    public class ChatHistoryService : IChatHistoryService
    {
        private readonly FirestoreDb _db;

        public ChatHistoryService(FirestoreDb db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task AddMessageAsync(string userId, string chatId, Models.ChatMessage message)
        {
            if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
            if (string.IsNullOrWhiteSpace(chatId)) throw new ArgumentException("chatId required", nameof(chatId));
            if (message is null) throw new ArgumentNullException(nameof(message));

            var users = _db.Collection("users");
            var chatDoc = users.Document(userId).Collection("chats").Document(chatId);
            var messages = chatDoc.Collection("messages");

            await messages.Document().SetAsync(message);

            // Update chat metadata
            var title = message.Role == "user" ? Truncate(message.Content, 80) : null;
            var update = new Dictionary<string, object?>
            {
                ["lastUpdated"] = message.Timestamp,
            };
            if (title != null)
                update["title"] = title;

            await chatDoc.SetAsync(update, SetOptions.MergeAll);
        }

        public async Task<IEnumerable<Models.ChatSession>> GetChatHistoryAsync(string userId)
        {
            var users = _db.Collection("users");
            var chats = users.Document(userId).Collection("chats");
            var snap = await chats.OrderByDescending("lastUpdated").Limit(50).GetSnapshotAsync();
            var list = new List<Models.ChatSession>();
            foreach (var doc in snap.Documents)
            {
                var data = doc.ToDictionary();
                data.TryGetValue("title", out var titleObj);
                data.TryGetValue("lastUpdated", out var lastObj);
                var session = new Models.ChatSession
                {
                    Id = doc.Id,
                    Title = titleObj as string ?? "Chat",
                    LastUpdated = lastObj as Timestamp? ?? Timestamp.FromDateTime(DateTime.UtcNow)
                };
                list.Add(session);
            }
            return list;
        }

        public async Task<IEnumerable<Models.ChatMessage>> GetChatMessagesAsync(string userId, string chatId)
        {
            var users = _db.Collection("users");
            var msgs = users.Document(userId).Collection("chats").Document(chatId).Collection("messages");
            var snap = await msgs.OrderBy("timestamp").Limit(500).GetSnapshotAsync();
            return snap.Documents.Select(d => d.ConvertTo<Models.ChatMessage>());
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}

using Google.Cloud.Firestore;

namespace OmgekeerdeStemWijzer.Api.Models
{
    [FirestoreData]
    public class ChatMessage
    {
        [FirestoreProperty("role")]
        public string Role { get; set; } = "user"; // "user" or "assistant"

        [FirestoreProperty("content")]
        public string Content { get; set; } = string.Empty;

        [FirestoreProperty("timestamp")]
        public Timestamp Timestamp { get; set; }
    }
}

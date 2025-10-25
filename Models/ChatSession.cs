using Google.Cloud.Firestore;

namespace OmgekeerdeStemWijzer.Api.Models
{
    [FirestoreData]
    public class ChatSession
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("title")]
        public string Title { get; set; } = "Chat";

        [FirestoreProperty("lastUpdated")]
        public Timestamp LastUpdated { get; set; }
    }
}

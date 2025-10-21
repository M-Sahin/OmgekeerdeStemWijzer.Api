namespace OmgekeerdeStemWijzer.Api.Models;
// /Models/PoliticalChunk.cs
public class PoliticalChunk
{
    // ID voor ChromaDB
    public required string Id { get; set; }

    // De tekst zelf
    public required string Content { get; set; }

    // Metadata voor RAG
    public required string PartyName { get; set; }
    public required string Theme { get; set; }
    public required int PageNumber { get; set; }
}
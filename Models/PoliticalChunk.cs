namespace OmgekeerdeStemWijzer.Api.Models;

public class PoliticalChunk
{
    public required string Id { get; set; }
    public required string Content { get; set; }
    public required string PartyName { get; set; }
    public required string Theme { get; set; }
    public required int PageNumber { get; set; }
}
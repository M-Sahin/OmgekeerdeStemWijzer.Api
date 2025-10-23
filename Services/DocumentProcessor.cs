using UglyToad.PdfPig;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmgekeerdeStemWijzer.Api.Models;

namespace OmgekeerdeStemWijzer.Api.Services;

public class DocumentProcessor
{
    private static readonly int ChunkSize = 300;
    private static readonly int ChunkOverlap = 50;

    /// <summary>
    /// Leest een PDF-bestand, extraheert de tekst en splitst deze in overlappende chunks.
    /// </summary>
    /// <param name="filePath">Het pad naar het PDF-bestand.</param>
    /// <param name="partyName">De naam van de partij.</param>
    /// <returns>Een lijst met PoliticalChunk objecten.</returns>
    public IEnumerable<PoliticalChunk> ProcessPdf(string filePath, string partyName)
    {
        var allChunks = new List<PoliticalChunk>();

        using (PdfDocument document = PdfDocument.Open(filePath))
        {
            var fullText = string.Join(" ", document.GetPages().Select(p => p.Text));

            var words = fullText.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            int chunkIndex = 0;
            int startWordIndex = 0;

            while (startWordIndex < words.Length)
            {
                var endWordIndex = Math.Min(startWordIndex + ChunkSize, words.Length);

                var currentChunkWords = words.Skip(startWordIndex).Take(endWordIndex - startWordIndex);
                var chunkContent = string.Join(" ", currentChunkWords);

                if (!string.IsNullOrWhiteSpace(chunkContent))
                {
                    allChunks.Add(new PoliticalChunk
                    {
                        Id = $"{partyName}_chunk_{chunkIndex}",
                        Content = chunkContent,
                        PartyName = partyName,
                        Theme = "N.v.t.",
                        PageNumber = 0
                    });

                    chunkIndex++;
                }
                startWordIndex += (ChunkSize - ChunkOverlap);

                if (ChunkSize <= ChunkOverlap) break;
            }
        }
        return allChunks;
    }
}


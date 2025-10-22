using UglyToad.PdfPig;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmgekeerdeStemWijzer.Api.Models;

namespace OmgekeerdeStemWijzer.Api.Services;

public class DocumentProcessor
{
    private static readonly int ChunkSize = 300; // Maximale grootte van de chunk in woorden
    private static readonly int ChunkOverlap = 50; // Overlapping om context te behouden

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
            // Combineer de tekst van de hele PDF voor een consistente chunking
            var fullText = string.Join(" ", document.GetPages().Select(p => p.Text));

            // Splits de tekst op in woorden
            var words = fullText.Split(new char[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            int chunkIndex = 0;
            int startWordIndex = 0;

            while (startWordIndex < words.Length)
            {
                // Bepaal het einde van de chunk
                var endWordIndex = Math.Min(startWordIndex + ChunkSize, words.Length);

                // Haal de woorden voor de chunk op
                var currentChunkWords = words.Skip(startWordIndex).Take(endWordIndex - startWordIndex);
                var chunkContent = string.Join(" ", currentChunkWords);

                if (!string.IsNullOrWhiteSpace(chunkContent))
                {
                    allChunks.Add(new PoliticalChunk
                    {
                        Id = $"{partyName}_chunk_{chunkIndex}",
                        Content = chunkContent,
                        PartyName = partyName,
                        Theme = "N.v.t.", // Thema kan later via AI-extractie worden ingevuld
                        PageNumber = 0 // Paginanummer is moeilijker te bepalen bij full-text chunking
                    });

                    chunkIndex++;
                }
                // Verplaats de startindex voor de volgende chunk met overlap
                startWordIndex += (ChunkSize - ChunkOverlap);

                // Voorkom een oneindige loop als de stapgrootte 0 is
                if (ChunkSize <= ChunkOverlap) break;
            }
        }
        return allChunks;
    }
}


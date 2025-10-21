using OmgekeerdeStemWijzer.Api.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class VectorStoreService
{
    private readonly ChromaClient _chroma;
    private IChromaCollection? _collection;
    private const string CollectionName = "verkiezingsprogrammas";

    public VectorStoreService(string chromaUrl)
    {
        _chroma = new ChromaClient(chromaUrl);
    }

    /// <summary>
    /// Initialiseert de verbinding met de ChromaDB collectie. Moet worden aangeroepen bij de start van de applicatie.
    /// </summary>
    public async Task InitializeAsync()
    {
        _collection = await _chroma.GetOrCreateCollectionAsync(CollectionName);
    }

    /// <summary>
    /// Voegt een enkele chunk met de bijbehorende embedding toe aan de vector store.
    /// </summary>
    /// <param name="chunk">Het PoliticalChunk object met de tekst en metadata.</param>
    /// <param name="embedding">De gegenereerde embedding vector.</param>
    public async Task AddChunkAsync(PoliticalChunk chunk, float[] embedding)
    {
        if (_collection == null)
            throw new System.InvalidOperationException("VectorStoreService is niet geïnitialiseerd. Roep InitializeAsync aan.");

        var metadata = new Dictionary<string, object>
        {
            { "partij", chunk.PartyName },
            { "thema", chunk.Theme },
            { "pagina", chunk.PageNumber }
        };

        await _collection.AddAsync(
            ids: new[] { chunk.Id },
            embeddings: new[] { embedding },
            metadatas: new[] { metadata },
            documents: new[] { chunk.Content }
        );
    }
    
    /// <summary>
    /// Zoekt naar de meest relevante document-chunks op basis van een query embedding.
    /// </summary>
    /// <param name="userQueryEmbedding">De embedding van de gebruikersvraag.</param>
    /// <param name="nResults">Het aantal resultaten dat moet worden opgehaald.</param>
    /// <returns>Een array met de tekst van de meest relevante chunks.</returns>
    public async Task<string[]> QueryRelevantChunksAsync(float[] userQueryEmbedding, int nResults = 5)
    {
        if (_collection == null)
            throw new System.InvalidOperationException("VectorStoreService is niet geïnitialiseerd. Roep InitializeAsync aan.");

        var results = await _collection.QueryAsync(
            queryEmbeddings: new[] { userQueryEmbedding },
            nResults: nResults
        );
        
        // We geven alleen de tekst (documenten) terug
        return results.Documents.FirstOrDefault()?.ToArray() ?? System.Array.Empty<string>();
    }
}

// Minimal local implementations to avoid needing an external Chroma SDK
// These are simple in-memory stubs that allow the project to compile
// and can be replaced with a real SDK integration later.
public class ChromaClient
{
    private readonly string _url;

    public ChromaClient(string chromaUrl)
    {
        _url = chromaUrl;
    }

    public Task<IChromaCollection> GetOrCreateCollectionAsync(string name)
    {
        IChromaCollection collection = new InMemoryChromaCollection(name);
        return Task.FromResult(collection);
    }
}

public interface IChromaCollection
{
    Task AddAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents);
    Task<QueryResult> QueryAsync(float[][] queryEmbeddings, int nResults);
}

public class QueryResult
{
    // Outer enumerable corresponds to each query embedding; inner enumerable is the list of documents returned for that query
    public IEnumerable<IEnumerable<string>> Documents { get; set; } = Enumerable.Empty<IEnumerable<string>>();
}

internal class InMemoryChromaCollection : IChromaCollection
{
    private readonly string _name;
    private readonly List<StoredDocument> _documents = new List<StoredDocument>();

    public InMemoryChromaCollection(string name)
    {
        _name = name;
    }

    public Task AddAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents)
    {
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new StoredDocument
            {
                Id = ids?.ElementAtOrDefault(i) ?? System.Guid.NewGuid().ToString(),
                Embedding = embeddings?.ElementAtOrDefault(i),
                Metadata = metadatas?.ElementAtOrDefault(i),
                Content = documents[i] ?? string.Empty
            };
            _documents.Add(doc);
        }

        return Task.CompletedTask;
    }

    public Task<QueryResult> QueryAsync(float[][] queryEmbeddings, int nResults)
    {
        // Simple naive implementation: for each query embedding return up to nResults stored documents (by insertion order)
        var resultsPerQuery = queryEmbeddings.Select(q => _documents.Take(nResults).Select(d => d.Content)).ToList();
        var result = new QueryResult { Documents = resultsPerQuery };
        return Task.FromResult(result);
    }

    private class StoredDocument
    {
        public string Id = string.Empty;
        public float[]? Embedding;
        public IDictionary<string, object>? Metadata;
        public string Content = string.Empty;
    }
}
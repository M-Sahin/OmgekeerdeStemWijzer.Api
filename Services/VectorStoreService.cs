using OmgekeerdeStemWijzer.Api.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class VectorStoreService
    {
        private readonly IChromaClient _chroma;
        private IChromaCollection? _collection;
        private const string CollectionName = "verkiezingsprogrammas";

        // Accepts an IChromaClient so the implementation can be HTTP-backed (Render) or in-memory for tests
        public VectorStoreService(IChromaClient chromaClient)
        {
            _chroma = chromaClient;
        }

        /// <summary>
        /// Initialiseert de verbinding met de ChromaDB collectie. Moet worden aangeroepen bij de start van de applicatie.
        /// </summary>
        public async Task InitializeAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            _collection = await _chroma.GetOrCreateCollectionAsync(CollectionName);
            await Task.CompletedTask;
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
            
            return results.Documents.FirstOrDefault()?.ToArray() ?? System.Array.Empty<string>();
        }
    }

    public interface IChromaClient
    {
        Task<IChromaCollection> GetOrCreateCollectionAsync(string name);
    }

    public class ChromaHttpClient : IChromaClient
    {
        private readonly HttpClient _http;

        public ChromaHttpClient(HttpClient httpClient)
        {
            _http = httpClient ?? throw new System.ArgumentNullException(nameof(httpClient));
        }

        public async Task<IChromaCollection> GetOrCreateCollectionAsync(string name)
        {
            // Try to create the collection on the server. This is idempotent:
            // if the collection already exists the server may return 409 or similar.
            // We swallow network or server errors here so the client can still
            // construct a collection wrapper; AddAsync will surface any remaining
            // problems when the server truly rejects the add.
            try
            {
                var createPayload = new { name = name };
                var resp = await _http.PostAsJsonAsync("collections", createPayload);
                // Accept success (200/201) or conflict (already exists). For other
                // non-success responses, throw to make the problem visible.
                if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.Conflict)
                {
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch (System.Exception)
            {
                // Non-fatal: we choose to continue and return a collection wrapper.
                // The calling AddAsync will fail if the server actually doesn't have
                // the collection or the network is down. Avoid throwing here to keep
                // initialization resilient; callers can retry or handle errors.
            }

            IChromaCollection collection = new ChromaHttpCollection(_http, name);
            return collection;
        }

        private class ChromaHttpCollection : IChromaCollection
        {
            private readonly HttpClient _http;
            private readonly string _name;

            public ChromaHttpCollection(HttpClient http, string name)
            {
                _http = http;
                _name = name;
            }

            public async Task AddAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents)
            {
                var payload = new
                {
                    ids,
                    embeddings,
                    metadatas,
                    documents
                };

                var resp = await _http.PostAsJsonAsync($"collections/{_name}/add", payload);
                resp.EnsureSuccessStatusCode();
            }

            public async Task<QueryResult> QueryAsync(float[][] queryEmbeddings, int nResults)
            {
                var payload = new
                {
                    query_embeddings = queryEmbeddings,
                    n_results = nResults
                };

                var resp = await _http.PostAsJsonAsync($"collections/{_name}/query", payload);
                if (!resp.IsSuccessStatusCode)
                {
                    return new QueryResult { Documents = Enumerable.Empty<IEnumerable<string>>() };
                }

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                var root = doc.RootElement;

                var results = new List<IEnumerable<string>>();
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("documents", out var docsEl) && docsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var arr in docsEl.EnumerateArray())
                    {
                        if (arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var item in arr.EnumerateArray())
                            {
                                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                                    list.Add(item.GetString() ?? string.Empty);
                            }
                            results.Add(list);
                        }
                    }
                }

                return new QueryResult { Documents = results };
            }
        }
    }

    public interface IChromaCollection
    {
        Task AddAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents);
        Task<QueryResult> QueryAsync(float[][] queryEmbeddings, int nResults);
    }

    public class QueryResult
    {
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
            var resultsPerQuery = new List<IEnumerable<string>>();

            foreach (var q in queryEmbeddings)
            {
                if (q == null || q.Length == 0)
                {
                    resultsPerQuery.Add(_documents.Take(nResults).Select(d => d.Content));
                    continue;
                }

                var scored = new List<(double Score, string Content)>();
                foreach (var d in _documents)
                {
                    if (d.Embedding == null || d.Embedding.Length != q.Length)
                        continue;

                    var score = CosineSimilarity(q, d.Embedding);
                    scored.Add((score, d.Content));
                }

                if (scored.Count == 0)
                {
                    resultsPerQuery.Add(_documents.Take(nResults).Select(d => d.Content));
                }
                else
                {
                    var top = scored
                        .OrderByDescending(s => s.Score)
                        .Take(nResults)
                        .Select(s => s.Content)
                        .ToList();
                    resultsPerQuery.Add(top);
                }
            }

            var result = new QueryResult { Documents = resultsPerQuery };
            return Task.FromResult(result);
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            double dot = 0, na = 0, nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double va = a[i];
                double vb = b[i];
                dot += va * vb;
                na += va * va;
                nb += vb * vb;
            }
            if (na == 0 || nb == 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        private class StoredDocument
        {
            public string Id = string.Empty;
            public float[]? Embedding;
            public IDictionary<string, object>? Metadata;
            public string Content = string.Empty;
        }
    }
}
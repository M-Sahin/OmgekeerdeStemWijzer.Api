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

            await _collection.UpsertAsync(
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
        private readonly string _tenant;
        private readonly string _database;

        public ChromaHttpClient(HttpClient httpClient, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _http = httpClient ?? throw new System.ArgumentNullException(nameof(httpClient));
            _tenant = configuration.GetSection("Chroma").GetValue<string>("Tenant") ?? "default_tenant";
            _database = configuration.GetSection("Chroma").GetValue<string>("Database") ?? "default_database";
        }

        public async Task<IChromaCollection> GetOrCreateCollectionAsync(string name)
        {
            // Try to create the collection on the server. This is idempotent:
            // if the collection already exists the server may return 409 or similar.
            // now we must obtain the collection ID to use modern endpoints (v2)
            string? collectionId = null;
            try
            {
                var createPayload = new { name = name, get_or_create = true };
                var createResp = await _http.PostAsJsonAsync($"tenants/{_tenant}/databases/{_database}/collections", createPayload);
                if (createResp.IsSuccessStatusCode)
                {
                    collectionId = await TryExtractCollectionIdAsync(createResp.Content);
                }
                else
                {
                    // Attempt a lookup by listing and filtering by name
                    var listResp = await _http.GetAsync($"tenants/{_tenant}/databases/{_database}/collections?limit=1000&offset=0");
                    if (listResp.IsSuccessStatusCode)
                        collectionId = await TryFindCollectionIdByNameAsync(listResp.Content, name);
                }
            }
            catch (System.Exception)
            {
                // We'll fall through and throw a clear error if we couldn't determine the ID
            }

            if (string.IsNullOrWhiteSpace(collectionId))
                throw new System.InvalidOperationException($"Kon Chroma-collectie-ID niet ophalen voor '{name}'.");

            IChromaCollection collection = new ChromaHttpCollection(_http, _tenant, _database, collectionId);
            return collection;
        }

        private class ChromaHttpCollection : IChromaCollection
        {
            private readonly HttpClient _http;
            private readonly string _tenant;
            private readonly string _database;
            private readonly string _collectionId;

            public ChromaHttpCollection(HttpClient http, string tenant, string database, string collectionId)
            {
                _http = http;
                _tenant = tenant;
                _database = database;
                _collectionId = collectionId;
            }

            public async Task UpsertAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents)
            {
                var payload = new
                {
                    ids,
                    embeddings,
                    metadatas,
                    documents
                };

                // Chroma v2: POST /api/v2/tenants/{tenant}/databases/{database}/collections/{collection_id}/upsert
                var resp = await _http.PostAsJsonAsync($"tenants/{_tenant}/databases/{_database}/collections/{_collectionId}/upsert", payload);
                resp.EnsureSuccessStatusCode();
            }

            public async Task<QueryResult> QueryAsync(float[][] queryEmbeddings, int nResults)
            {
                var payload = new
                {
                    query_embeddings = queryEmbeddings,
                    n_results = nResults
                };

                // Chroma v2: POST /api/v2/tenants/{tenant}/databases/{database}/collections/{collection_id}/query
                var resp = await _http.PostAsJsonAsync($"tenants/{_tenant}/databases/{_database}/collections/{_collectionId}/query", payload);
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

        private static async Task<string?> TryExtractCollectionIdAsync(HttpContent content)
        {
            try
            {
                using var stream = await content.ReadAsStreamAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                var root = doc.RootElement;

                // Common shapes to try: { id: "..." }
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        return idEl.GetString();

                    // { collection: { id: "..." } }
                    if (root.TryGetProperty("collection", out var collEl) && collEl.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        if (collEl.TryGetProperty("id", out var id2) && id2.ValueKind == System.Text.Json.JsonValueKind.String)
                            return id2.GetString();
                    }

                    // { collections: [ { id: "..." } ] }
                    if (root.TryGetProperty("collections", out var collsEl) && collsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var item in collsEl.EnumerateArray())
                        {
                            if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("id", out var id3) && id3.ValueKind == System.Text.Json.JsonValueKind.String)
                                return id3.GetString();
                        }
                    }
                }

                // [ { id: "..." } ]
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            return idEl.GetString();
                    }
                }
            }
            catch
            {
                // ignore and return null
            }

            return null;
        }

        private static async Task<string?> TryFindCollectionIdByNameAsync(HttpContent content, string targetName)
        {
            try
            {
                using var stream = await content.ReadAsStreamAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(stream);
                var root = doc.RootElement;

                // Array of collections
                if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in root.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (item.TryGetProperty("name", out var nameEl) &&
                                nameEl.ValueKind == System.Text.Json.JsonValueKind.String &&
                                string.Equals(nameEl.GetString(), targetName, System.StringComparison.Ordinal))
                            {
                                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                    return idEl.GetString();
                            }
                        }
                    }
                }

                // { collections: [ ... ] }
                if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    root.TryGetProperty("collections", out var collsEl) && collsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in collsEl.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (item.TryGetProperty("name", out var nameEl) &&
                                nameEl.ValueKind == System.Text.Json.JsonValueKind.String &&
                                string.Equals(nameEl.GetString(), targetName, System.StringComparison.Ordinal))
                            {
                                if (item.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                    return idEl.GetString();
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore parsing errors; return null
            }
            return null;
        }
    }

    public interface IChromaCollection
    {
        Task UpsertAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents);
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

        public Task UpsertAsync(string[] ids, float[][] embeddings, IDictionary<string, object>[] metadatas, string[] documents)
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var id = ids?.ElementAtOrDefault(i) ?? System.Guid.NewGuid().ToString();
                var existing = _documents.FirstOrDefault(d => d.Id == id);
                if (existing != null)
                {
                    // update existing
                    existing.Embedding = embeddings?.ElementAtOrDefault(i) ?? existing.Embedding;
                    existing.Metadata = metadatas?.ElementAtOrDefault(i) ?? existing.Metadata;
                    existing.Content = documents[i] ?? existing.Content;
                }
                else
                {
                    var doc = new StoredDocument
                    {
                        Id = id,
                        Embedding = embeddings?.ElementAtOrDefault(i),
                        Metadata = metadatas?.ElementAtOrDefault(i),
                        Content = documents[i] ?? string.Empty
                    };
                    _documents.Add(doc);
                }
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
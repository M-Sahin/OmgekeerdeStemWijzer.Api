using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI.Embeddings;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class EmbeddingService : IDisposable
    {
        private readonly EmbeddingClient _client;
        private readonly string _modelName;
        private readonly ILogger<EmbeddingService>? _logger;

        public EmbeddingService(string apiKey, string modelName = "text-embedding-3-small", ILogger<EmbeddingService>? logger = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("OpenAI API key cannot be null or empty.", nameof(apiKey));
            }

            // Ensure we never pass an empty or whitespace model name to the OpenAI client.
            // Config sources (env vars) may accidentally set an empty string which the
            // OpenAI API rejects with HTTP 400 "you must provide a model parameter".
            _modelName = string.IsNullOrWhiteSpace(modelName) ? "text-embedding-3-small" : modelName;
            _logger = logger;
            _client = new EmbeddingClient(_modelName, apiKey);
            _logger?.LogInformation("Initialized OpenAI EmbeddingService with model: {Model}", _modelName);
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger?.LogWarning("Attempted to generate embedding for empty or null text");
                    return Array.Empty<float>();
                }

                _logger?.LogDebug("GenerateEmbeddingAsync: generating embedding for text length {Len}", text.Length);
                
                var embedding = await _client.GenerateEmbeddingAsync(text);
                var vector = embedding.Value.ToFloats().ToArray();
                
                _logger?.LogDebug("Successfully generated embedding with dimension {Dim}", vector.Length);
                
                return vector;
            }
            catch (Exception ex)
            {
                // Include the model name in logs to make misconfiguration obvious.
                _logger?.LogError(ex, "Failed to generate embedding using OpenAI API (model: {Model})", _modelName);
                throw;
            }
        }

        public void Dispose()
        {
        }
    }
}


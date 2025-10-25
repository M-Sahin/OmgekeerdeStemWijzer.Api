using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using OmgekeerdeStemWijzer.Api.Models;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class EmbeddingService : IDisposable
    {
        private readonly EmbeddingClient _client;
        private readonly string _modelName;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(IOptions<OpenAIOptions> options, ILogger<EmbeddingService> logger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);
            
            var openAIOptions = options.Value;
            
            if (string.IsNullOrWhiteSpace(openAIOptions.ApiKey))
            {
                throw new ArgumentException("OpenAI API key cannot be null or empty.", nameof(openAIOptions.ApiKey));
            }

            _modelName = string.IsNullOrWhiteSpace(openAIOptions.EmbeddingModel) 
                ? "text-embedding-3-small" 
                : openAIOptions.EmbeddingModel;
            
            _logger = logger;
            // The client expects the API key first; pass the api key then the model name to ensure the
            // model parameter is set on subsequent calls.
            _client = new EmbeddingClient(openAIOptions.ApiKey, _modelName);
            _logger.LogInformation("Initialized OpenAI EmbeddingService with model: {Model}", _modelName);
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Attempted to generate embedding for empty or null text");
                    return Array.Empty<float>();
                }

                _logger.LogDebug("GenerateEmbeddingAsync: generating embedding for text length {Len}", text.Length);
                
                // Ensure we pass the configured model explicitly to the client call.
                // Call the client; the constructor was given the api key and model so the
                // model parameter will be included by the client implementation.
                var embedding = await _client.GenerateEmbeddingAsync(text);
                var vector = embedding.Value.ToFloats().ToArray();
                
                _logger.LogDebug("Successfully generated embedding with dimension {Dim}", vector.Length);
                
                return vector;
            }
            catch (Exception ex)
            {
                // Include the model name in logs to make misconfiguration obvious.
                _logger.LogError(ex, "Failed to generate embedding using OpenAI API (model: {Model})", _modelName);
                throw;
            }
        }

        public void Dispose()
        {
        }
    }
}


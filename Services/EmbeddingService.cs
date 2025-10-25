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
        private readonly System.Net.Http.HttpClient _http;
        private readonly string _modelName;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(IOptions<OpenAIOptions> options, ILogger<EmbeddingService> logger, IServiceProvider sp)
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

            var factory = sp.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
            _http = factory?.CreateClient("openai") ?? new System.Net.Http.HttpClient();

            _logger.LogInformation("Initialized EmbeddingService with model: {Model}", _modelName);
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
                
                // Make an explicit POST to the OpenAI embeddings endpoint so we control
                // the payload (model + input) and avoid any ambiguity about constructor
                // parameter ordering in third-party SDKs.
                var payload = new { model = _modelName, input = text };
                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var resp = await _http.PostAsync("v1/embeddings", content);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                // Response shape: { data: [ { embedding: [number, ...], index: 0 } ], ... }
                if (root.TryGetProperty("data", out var dataEl) && dataEl.GetArrayLength() > 0)
                {
                    var embEl = dataEl[0].GetProperty("embedding");
                    var list = new System.Collections.Generic.List<float>();
                    foreach (var item in embEl.EnumerateArray())
                    {
                        if (item.ValueKind == System.Text.Json.JsonValueKind.Number && item.TryGetSingle(out var f))
                        {
                            list.Add(f);
                        }
                        else if (item.ValueKind == System.Text.Json.JsonValueKind.Number && item.TryGetDouble(out var d))
                        {
                            list.Add((float)d);
                        }
                    }

                    var vector = list.ToArray();
                    _logger.LogDebug("Successfully generated embedding with dimension {Dim}", vector.Length);
                    return vector;
                }

                _logger.LogWarning("Embedding response did not contain data");
                return Array.Empty<float>();
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


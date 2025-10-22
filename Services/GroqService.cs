using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OmgekeerdeStemWijzer.Api.Services
{
    /// <summary>
    /// Minimal Groq service that uses the OpenAI-compatible HTTP API surface.
    /// This keeps us decoupled from any specific OpenAI client library and works
    /// by posting completions/chat requests to the configured Groq endpoint.
    /// </summary>
    public class GroqService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
    private readonly ILogger<GroqService>? _logger;
    private readonly string _defaultModel;

        public GroqService(HttpClient httpClient, string apiKey, ILogger<GroqService>? logger = null, string defaultModel = "groq/compound")
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _logger = logger;
            _defaultModel = defaultModel ?? "groq/compound";
        }

        public async Task<string> QueryAsync(string systemPrompt, string userMessage, string? model = null)
        {
            var modelToUse = model ?? _defaultModel;

            var url = new Uri(_http.BaseAddress ?? new Uri("https://api.groq.com/openai/v1"), "chat/completions").ToString();

            var payload = new
            {
                model = modelToUse,
                messages = new[] {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                temperature = 0.1,
                max_tokens = 1024
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            };

            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            try
            {
                using var resp = await _http.SendAsync(request);
                resp.EnsureSuccessStatusCode();

                using var stream = await resp.Content.ReadAsStreamAsync();
                var doc = await JsonSerializer.DeserializeAsync<JsonElement>(stream);

                // Try to extract first choice text (OpenAI-compatible shape)
                if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentEl))
                    {
                        return contentEl.GetString() ?? string.Empty;
                    }
                }

                _logger?.LogWarning("Groq response did not contain a completion.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Groq request failed");
                return string.Empty;
            }
        }
    }
}

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
    public string? LastError { get; private set; }

        public GroqService(HttpClient httpClient, string apiKey, ILogger<GroqService>? logger = null, string defaultModel = "groq/compound")
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _logger = logger;
            _defaultModel = defaultModel ?? "groq/compound";
        }

        public async Task<string> QueryAsync(string systemPrompt, string userMessage, string? model = null, bool allowFallback = true)
        {
            var modelToUse = model ?? _defaultModel;

            // First attempt with the requested/default model
            var first = await SendOnceAsync(systemPrompt, userMessage, modelToUse);
            if (first.Ok)
                return first.Content;

            // Try simple fallbacks if allowed and we saw an error
            if (allowFallback && !string.IsNullOrWhiteSpace(LastError))
            {
                var errLower = LastError.ToLowerInvariant();
                var mightBeServerIssue = errLower.Contains("internal_server_error") || errLower.Contains("http 500");
                var looksLikeCompound = modelToUse.StartsWith("groq/compound", StringComparison.OrdinalIgnoreCase);
                if (mightBeServerIssue || looksLikeCompound)
                {
                    var fallbacks = new[] { "groq/compound-mini", "llama-3.1-8b-instant", "llama-3.3-70b-versatile" };
                    foreach (var fb in fallbacks)
                    {
                        if (string.Equals(fb, modelToUse, StringComparison.OrdinalIgnoreCase))
                            continue;

                        _logger?.LogWarning("Retrying Groq request with fallback model: {Model}", fb);
                        var alt = await SendOnceAsync(systemPrompt, userMessage, fb);
                        if (alt.Ok)
                        {
                            _logger?.LogInformation("Groq request succeeded with fallback model: {Model}", fb);
                            return alt.Content;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private async Task<(bool Ok, string Content)> SendOnceAsync(string systemPrompt, string userMessage, string modelToUse)
        {
            // Build the correct endpoint. If BaseAddress is set (recommended), use a relative path so it appends to e.g. https://api.groq.com/openai/v1/
            // If not, fall back to an absolute URL.
            var relativePath = new Uri("chat/completions", UriKind.Relative);
            var absoluteFallback = new Uri("https://api.groq.com/openai/v1/chat/completions", UriKind.Absolute);
            var requestUri = _http.BaseAddress != null ? relativePath : absoluteFallback;

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

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(payload, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            };

            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            try
            {
                _logger?.LogDebug("Sending Groq chat request with model: {Model}", modelToUse);
                using var resp = await _http.SendAsync(request);
                if (!resp.IsSuccessStatusCode)
                {
                    string errorBody = await resp.Content.ReadAsStringAsync();
                    var parsed = TryParseGroqError(errorBody);
                    LastError = $"HTTP {(int)resp.StatusCode}: {parsed}";
                    _logger?.LogError("Groq API error {Status}: {Body}", (int)resp.StatusCode, Truncate(errorBody, 2000));
                    return (false, string.Empty);
                }

                using var stream = await resp.Content.ReadAsStreamAsync();
                var doc = await JsonSerializer.DeserializeAsync<JsonElement>(stream);

                // Try to extract first choice text (OpenAI-compatible shape)
                if (doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var contentEl))
                    {
                        LastError = null;
                        return (true, contentEl.GetString() ?? string.Empty);
                    }
                }

                LastError = "Groq response did not contain a completion.";
                _logger?.LogWarning("Groq response did not contain a completion.");
                return (false, string.Empty);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger?.LogError(ex, "Groq request failed");
                return (false, string.Empty);
            }
        }

        private static string Truncate(string? text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max);
        }

        private static string TryParseGroqError(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("error", out var err))
                    {
                        if (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg))
                        {
                            return msg.GetString() ?? body;
                        }
                        return err.ToString();
                    }
                    if (root.TryGetProperty("message", out var message))
                    {
                        return message.GetString() ?? body;
                    }
                }
            }
            catch
            {
                // ignore parse errors
            }
            return body;
        }
    }
}

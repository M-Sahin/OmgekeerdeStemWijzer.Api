using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

// EmbeddingService uses reflection to try to call into the installed
// OllamaSharp client at runtime. If that fails it falls back to calling
// the Ollama HTTP API directly. This avoids compile-time dependency on
// specific OllamaSharp types and prevents errors when method/property
// names changed between versions.
public class EmbeddingService
{
    private readonly object? _ollamaClient; // kept as object to avoid compile-time type dependency
    private readonly Uri _ollamaBaseUri;
    private readonly string _modelName = "nomic-embed-text"; // configure your model here
    private readonly HttpClient _http = new HttpClient();

    public EmbeddingService(string ollamaUrl)
    {
        _ollamaBaseUri = new Uri(ollamaUrl);

        // Try to find the Ollama client type (OllamaSharp) and instantiate it via reflection.
        try
        {
            var clientType = Type.GetType("OllamaSharp.OllamaApiClient, OllamaSharp");
            if (clientType != null)
            {
                // prefer constructor that accepts a Uri
                ConstructorInfo? ctor = clientType.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(Uri));

                if (ctor != null)
                {
                    _ollamaClient = ctor.Invoke(new object[] { _ollamaBaseUri });
                }
                else
                {
                    // try parameterless constructor
                    ctor = clientType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0);
                    if (ctor != null)
                        _ollamaClient = ctor.Invoke(Array.Empty<object>());
                }
            }
        }
        catch
        {
            _ollamaClient = null; // ignore and fall back to HTTP later
        }
    }

    // Public API: returns a float[] embedding for the provided text.
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // 1) If we instantiated an Ollama client, try to call an embedding method via reflection.
        if (_ollamaClient != null)
        {
            try
            {
                var clientType = _ollamaClient.GetType();
                var methods = clientType.GetMethods(BindingFlags.Instance | BindingFlags.Public);

                // find the first method whose name contains 'embed' (case-insensitive)
                var embedMethod = methods.FirstOrDefault(m => m.Name.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0);
                if (embedMethod != null)
                {
                    var parameters = embedMethod.GetParameters();
                    object?[] args;

                    if (parameters.Length == 0)
                    {
                        args = Array.Empty<object?>();
                    }
                    else if (parameters.Length == 1)
                    {
                        var pType = parameters[0].ParameterType;

                        if (pType == typeof(string))
                        {
                            args = new object?[] { text };
                        }
                        else if (pType == typeof(string[]))
                        {
                            args = new object?[] { new string[] { text } };
                        }
                        else
                        {
                            // try to build a request object (EmbedRequest or similar)
                            var req = Activator.CreateInstance(pType);
                            if (req != null)
                            {
                                // common property names used across different versions
                                var triedProps = new[] { "Prompt", "Input", "Inputs", "Text", "Model" };
                                foreach (var name in triedProps)
                                {
                                    var prop = pType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                    if (prop != null && prop.CanWrite)
                                    {
                                        if (string.Equals(prop.Name, "Model", StringComparison.OrdinalIgnoreCase) && prop.PropertyType == typeof(string))
                                            prop.SetValue(req, _modelName);
                                        else if (prop.PropertyType == typeof(string))
                                            prop.SetValue(req, text);
                                        else if (prop.PropertyType == typeof(string[]))
                                            prop.SetValue(req, new string[] { text });
                                    }
                                }
                            }

                            args = new object?[] { req };
                        }
                    }
                    else
                    {
                        // if multiple parameters, try simple fallback: pass text as first param
                        args = new object?[] { text }.Concat(Enumerable.Repeat<object?>(null, parameters.Length - 1)).ToArray();
                    }

                    // invoke method and await its returned Task (works for Task and Task<T>)
                    var invoked = embedMethod.Invoke(_ollamaClient, args);
                    if (invoked != null)
                    {
                        // Handle Task<T> or synchronous return
                        var invokedType = invoked.GetType();
                        if (typeof(System.Threading.Tasks.Task).IsAssignableFrom(invokedType))
                        {
                            // await the task dynamically: get Result property if Task<T>
                            var awaiter = ((System.Threading.Tasks.Task)invoked);
                            await awaiter;

                            var resultProp = invokedType.GetProperty("Result");
                            var invocationResult = resultProp != null ? resultProp.GetValue(invoked) : null;
                            var embedding = ExtractEmbeddingFromResult(invocationResult);
                            if (embedding != null && embedding.Length > 0)
                                return embedding;
                        }
                        else
                        {
                            var embedding = ExtractEmbeddingFromResult(invoked);
                            if (embedding != null && embedding.Length > 0)
                                return embedding;
                        }
                    }
                }
            }
            catch
            {
                // ignore reflection errors and fall back to HTTP
            }
        }

        // 2) Fallback: call Ollama HTTP endpoints with a few common patterns.
        return await GenerateEmbeddingHttp(text);
    }

    // Try several common Ollama REST endpoints and response shapes to extract the embedding.
    private async Task<float[]> GenerateEmbeddingHttp(string text)
    {
        var attempts = new[] {
            new { Path = "/embed", Body = (object)new { model = _modelName, input = text } },
            new { Path = "/api/embed", Body = (object)new { model = _modelName, input = text } },
            new { Path = $"/api/embed?model={_modelName}", Body = (object)new { input = text } }
        };

        foreach (var attempt in attempts)
        {
            try
            {
                var url = new Uri(_ollamaBaseUri, attempt.Path).ToString();
                using var resp = await _http.PostAsJsonAsync(url, attempt.Body);
                if (!resp.IsSuccessStatusCode)
                    continue;

                using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                // Check common response shapes
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("embedding", out var embEl))
                        return JsonElementToFloatArray(embEl);

                    if (doc.RootElement.TryGetProperty("embeddings", out var embsEl))
                    {
                        if (embsEl.ValueKind == JsonValueKind.Array && embsEl.GetArrayLength() > 0)
                            return JsonElementToFloatArray(embsEl[0]);
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonElementToFloatArray(doc.RootElement);
                }
            }
            catch
            {
                // ignore and try the next pattern
            }
        }

        // If all attempts fail return an empty embedding so caller can handle it.
        return Array.Empty<float>();
    }

    private static float[]? ExtractEmbeddingFromResult(object? result)
    {
        if (result == null) return null;

        // direct arrays
        if (result is float[] f) return f;
        if (result is double[] d) return d.Select(x => (float)x).ToArray();

        var t = result.GetType();

        // look for common property names
        var propNames = new[] { "Embedding", "Embeddings", "embedding", "embeddings", "Data" };
        foreach (var name in propNames)
        {
            var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) continue;
            var val = prop.GetValue(result);
            if (val == null) continue;

            if (val is float[] ff) return ff;
            if (val is double[] dd) return dd.Select(x => (float)x).ToArray();

            if (val is Array arr && arr.Length > 0)
            {
                var first = arr.GetValue(0);
                if (first is double[] dfirst) return dfirst.Select(x => (float)x).ToArray();
                if (first is float[] ffirst) return ffirst;
            }
        }

        return null;
    }

    private static float[] JsonElementToFloatArray(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return Array.Empty<float>();

        var list = new System.Collections.Generic.List<float>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number)
            {
                if (item.TryGetSingle(out var f)) list.Add(f);
                else if (item.TryGetDouble(out var d)) list.Add((float)d);
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                if (float.TryParse(item.GetString(), out var pf)) list.Add(pf);
            }
        }

        return list.ToArray();
    }
}
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class OllamaHealthCheck : IHealthCheck
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly string _ollamaUrl;

        public OllamaHealthCheck(IHttpClientFactory httpFactory, Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _httpFactory = httpFactory;
            _ollamaUrl = config.GetValue<string>("ServiceUrls:Ollama") ?? "http://localhost:11434";
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var resp = await client.GetAsync(_ollamaUrl, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Healthy("Ollama reachable");
                }

                return HealthCheckResult.Unhealthy($"Ollama returned {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Ollama check failed", ex);
            }
        }
    }
}

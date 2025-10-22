using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class GroqHealthCheck : IHealthCheck
    {
        private readonly IHttpClientFactory _httpFactory;

        public GroqHealthCheck(IHttpClientFactory httpFactory)
        {
            _httpFactory = httpFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _httpFactory.CreateClient("groq");
                var request = new HttpRequestMessage(HttpMethod.Get, client.BaseAddress ?? new Uri("/" , UriKind.Relative));
                using var resp = await client.SendAsync(request, cancellationToken);
                if (resp.IsSuccessStatusCode)
                {
                    return HealthCheckResult.Healthy("Groq reachable");
                }

                return HealthCheckResult.Unhealthy($"Groq returned {(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Groq check failed", ex);
            }
        }
    }
}

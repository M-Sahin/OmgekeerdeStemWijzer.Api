using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class VectorStoreHealthCheck : IHealthCheck
    {
        private readonly VectorStoreService _vectorStore;

        public VectorStoreHealthCheck(VectorStoreService vectorStore)
        {
            _vectorStore = vectorStore;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await _vectorStore.InitializeAsync(cancellationToken);
                return HealthCheckResult.Healthy("Vector store initialized");
            }
            catch (System.Exception ex)
            {
                return HealthCheckResult.Unhealthy("Vector store not ready", ex);
            }
        }
    }
}

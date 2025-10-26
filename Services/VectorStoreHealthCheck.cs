using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class VectorStoreHealthCheck : IHealthCheck
    {
        private readonly IVectorStoreService _vectorStore;

        public VectorStoreHealthCheck(IVectorStoreService vectorStore)
        {
            _vectorStore = vectorStore;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // Test connectivity by trying to get a collection (creates if not exists)
                await _vectorStore.GetOrCreateCollectionAsync("health-check");
                return HealthCheckResult.Healthy("Vector store connection successful");
            }
            catch (System.Exception ex)
            {
                return HealthCheckResult.Unhealthy("Vector store not ready", ex);
            }
        }
    }
}

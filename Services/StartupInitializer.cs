using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace OmgekeerdeStemWijzer.Api.Services
{
    public class StartupInitializer : IHostedService
    {
        private readonly IServiceProvider _sp;

        public StartupInitializer(IServiceProvider sp)
        {
            _sp = sp;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = ((IServiceProvider)_sp).CreateScope();
            var vector = scope.ServiceProvider.GetRequiredService<VectorStoreService>();
            await vector.InitializeAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

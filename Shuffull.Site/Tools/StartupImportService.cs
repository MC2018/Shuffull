using Shuffull.Site.Configuration;

namespace Shuffull.Site.Tools
{
    public class StartupImportService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly SongImporter _songImporter;

        public StartupImportService(IServiceProvider services)
        {
            _services = services;
            _songImporter = _services.GetRequiredService<SongImporter>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _songImporter.ImportManualFiles();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

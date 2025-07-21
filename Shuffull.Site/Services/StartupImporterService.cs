using Shuffull.Site.Configuration;

namespace Shuffull.Site.Services
{
    public class StartupImporterService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly SongImporterService _songImporter;
        public static ShuffullFilesConfiguration _fileConfig { get; private set; } // TODO: bad

        public StartupImporterService(IServiceProvider services, IConfiguration configuration)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("ShuffullFilesConfiguration not configured.");
            // _songImporter = _services.GetRequiredService<SongImportService>();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // await _songImporter.ImportManualFiles();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

using Microsoft.EntityFrameworkCore;
using OpenAI_API.Moderation;
using Shuffull.Site.Configuration;
using Shuffull.Site.Database.Models;

namespace Shuffull.Site.Tools
{
    public class GenreImporter : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ShuffullFilesConfiguration _fileConfig;

        public GenreImporter(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>();
        }

        public void ImportGenres()
        {
            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            var currentGenreNames = context.Tags
                .AsNoTracking()
                .Where(x => x.Type == Enums.TagType.Genre)
                .Select(x => x.Name)
                .ToList();
            var genreNames = File.ReadAllLines(_fileConfig.GenresFile).ToList();
            var newGenreNames = genreNames.Except(currentGenreNames).Where(x => !string.IsNullOrEmpty(x)).ToList();

            foreach (var newGenreName in newGenreNames)
            {
                var newGenre = new Tag()
                {
                    Name = newGenreName,
                    Type = Enums.TagType.Genre
                };
                context.Add(newGenre);
            }

            context.SaveChanges();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ImportGenres();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

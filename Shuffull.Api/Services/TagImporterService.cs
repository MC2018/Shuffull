using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Nut.Results;
using Shuffull.Shared.Enums;
using Shuffull.Shared.Tools;
using Shuffull.Api.Configuration;
using Shuffull.Core.Models.Database;
using Shuffull.Core.Persistence;
using Shuffull.Metadata.Models;
using Shuffull.Api.Services.FileStorage;
using System.Threading;

namespace Shuffull.Api.Services
{
    public class TagImporterService : IHostedService
    {
        private readonly IServiceProvider _services;
        private readonly ShuffullFilesConfiguration _fileConfig;

        public TagImporterService(IConfiguration configuration, IServiceProvider services)
        {
            _services = services;
            _fileConfig = configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new Exception("File configuration not found.");
        }

        // TODO: improve, seems like itll break if you change around main/sub genres
        // This should probably rebuild the genres every time
        public async Task ImportGenreList(CancellationToken cancellationToken)
        {
            using var scope = _services.CreateScope();
            using var dbContext = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var fileStorageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            var dbGenreRelations = await dbContext.GenreRelations.Include(x => x.MainGenre).Include(x => x.SubGenre).ToListAsync(cancellationToken);
            var dbGenres = dbGenreRelations
                .Select(x => x.MainGenre)
                .Concat(dbGenreRelations.Select(x => x.SubGenre))
                .DistinctBy(x => x.TagId)
                .ToList();
            var downloadResult = await fileStorageService.DownloadSerializableObjectAsync<GenresFile>(_fileConfig.GenresFile, cancellationToken);
            if (downloadResult.IsError)
            {
                throw downloadResult.GetError();
            }
            var genresFile = downloadResult.Get();

            // Add new genres
            var genresFileGenres = genresFile.MainGenres.Select(x => x.Name).Concat(genresFile.MainGenres.SelectMany(x => x.SubGenreNames));
            var newGenreNames = genresFileGenres
                .Except(dbGenreRelations.Select(x => x.MainGenre).Select(x => x.Name))
                .Where(x => !string.IsNullOrEmpty(x) && !dbGenres.Select(y => y.Name).Contains(x))
                .ToList();

            foreach (var newGenreName in newGenreNames)
            {
                var newGenre = new Genre()
                {
                    TagId = IdGenerator.Generate(),
                    Name = newGenreName,
                };
                dbContext.Add(newGenre);
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            // Relations
            var allGenres = await dbContext.Genres
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var genresFileNamePairs = genresFile.MainGenres
                .SelectMany(main => main.SubGenreNames.Select(sub => new { MainGenreName = main.Name, SubGenreName = sub }))
                .Distinct()
                .ToList();

            foreach (var genresFileNamePair in genresFileNamePairs)
            {
                var mainGenre = allGenres.Where(x => x.Name == genresFileNamePair.MainGenreName).First();
                var subGenre = allGenres.Where(x => x.Name == genresFileNamePair.SubGenreName).First();

                // Skip relations that already exist
                if (dbGenreRelations.Where(x => x.MainGenre.TagId == mainGenre.TagId && x.SubGenre.TagId == subGenre.TagId).Any())
                {
                    continue;
                }

                var genreRelation = new GenreRelation()
                {
                    GenreRelationId = IdGenerator.Generate(),
                    MainGenreId = mainGenre.TagId,
                    SubGenreId = subGenre.TagId,
                };
                dbContext.Add(genreRelation);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await ImportGenreList(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

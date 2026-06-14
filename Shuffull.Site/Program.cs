using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Shuffull.Site.Configuration;
using Shuffull.Site;
using NLog.Web;
using Shuffull.Site.Services.FileStorage;
using Shuffull.Site.Services;
using Shuffull.Metadata.Models;
using Shuffull.Core.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddHostedService<TagImporterService>();
builder.Services.AddHostedService<DetailedSongImporterService>();
// The JSON/CQRS API has moved to Shuffull.Api. This host keeps only the MVC upload UI, the static
// music/album-art serving, the background importers, and the EF migrations (applied on startup).
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    // ShuffullContext now lives in Shuffull.Core, but the EF migrations remain in this assembly,
    // so EF must be told where to find them.
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Shuffull"),
        sql => sql.MigrationsAssembly("Shuffull.Site"));
});

builder.Services.TryAddAIService(builder.Configuration);
builder.Services.AddHostedService<SongImportService>();
builder.Services.AddHostedService<ExternalSongImporterService>();
// TODO: Register IYouTubeApiService -> YouTubeApiService once a "YouTubeApi" config section (ApiKey)
// is added. SongImportService resolves it via the nullable GetService<IYouTubeApiService>(), so it's
// safe to leave unregistered for now (AI genre-context enrichment is simply skipped).
builder.Logging.ClearProviders();
builder.Host.UseNLog();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// TODO: Move into its own file, and abstract the file access layer
var filesConfig = builder.Configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>() ?? throw new InvalidOperationException("Files configuration is not set.");

Directory.CreateDirectory(filesConfig.FailedImportDirectory);
Directory.CreateDirectory(filesConfig.MusicRootDirectory);
Directory.CreateDirectory(filesConfig.ManualSongImportDirectory);
Directory.CreateDirectory(filesConfig.SongImportDirectory);
Directory.CreateDirectory(filesConfig.ExternalSongImportDirectory);
Directory.CreateDirectory(filesConfig.SavedAiResponsesDirectory);
Directory.CreateDirectory(filesConfig.AlbumArtDirectory);

if (!File.Exists(filesConfig.GenresFile))
{
    // Seed a new deployment's genres file from the canonical list shared with the funnel
    // (Shuffull.Metadata), not an empty list, so AI genre tagging has a vocabulary out of the box.
    // An existing file is left untouched, so local edits to the list are preserved.
    File.WriteAllText(filesConfig.GenresFile, GenresFile.CanonicalJson);
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        filesConfig.MusicRootDirectory
    ),
    RequestPath = "/music"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        filesConfig.AlbumArtDirectory
    ),
    RequestPath = "/albumart"
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
    db.Database.Migrate();
}

app.Run();

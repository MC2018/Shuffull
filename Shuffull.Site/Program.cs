using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Shuffull.Site.Configuration;
using Shuffull.Site;
using Shuffull.Site.Tools.Authorization;
using NLog.Web;
using Shuffull.Site.Services.FileStorage;
using Shuffull.Site.Services;
using Shuffull.Metadata.Models;
using FluentValidation;
using MediatR;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddHostedService<TagImporterService>();
builder.Services.AddHostedService<DetailedSongImporterService>();
builder.Services.AddScoped<JwtHelper>();
// Lets Shuffull.Core's user slices issue tokens without referencing Site/JWT types.
builder.Services.AddScoped<Shuffull.Core.Authentication.IAuthTokenGenerator, JwtAuthTokenGenerator>();
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shuffull"));
});

// Expose the concrete ShuffullContext as the base DbContext so Shuffull.Core's generic
// UnitOfWork/Repository (which depend only on DbContext) resolve the request-scoped context.
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShuffullContext>());
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// CQRS pipeline (mirrors the Sociallite backend): MediatR handlers + FluentValidation validators
// are discovered from BOTH the Site assembly (legacy/in-progress slices) and the Core assembly
// (where migrated feature slices live). The ValidationBehavior (in Shuffull.Core) runs the
// validators before each handler and short-circuits to Result.Error on failure.
var siteAssembly = typeof(Program).Assembly;
var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
builder.Services.AddValidatorsFromAssemblies(new[] { siteAssembly, coreAssembly });
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(siteAssembly, coreAssembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
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

app.UseMiddleware<JwtMiddleware>();

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

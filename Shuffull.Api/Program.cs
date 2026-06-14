using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using NLog.Web;
using Shuffull.Api.Configuration;
using Shuffull.Api.Services;
using Shuffull.Api.Services.FileStorage;
using Shuffull.Api.Tools.Authorization;
using Shuffull.Core.Authentication;
using Shuffull.Core.Behaviors;
using Shuffull.Core.Persistence;
using Shuffull.Core.Persistence.Repositories;
using Shuffull.Metadata.Models;

var builder = WebApplication.CreateBuilder(args);

// Single host: thin JSON CQRS API + the background song-import/AI pipeline + static music/album-art
// serving + the EF migrations (applied on startup). There is no server-rendered UI — a dedicated
// website will consume this API later.
builder.Services.AddControllers();

// --- Auth (JWT) ---------------------------------------------------------------------------------
builder.Services.AddScoped<JwtHelper>();
// Lets Shuffull.Core's user slices issue tokens without referencing API/JWT types.
builder.Services.AddScoped<IAuthTokenGenerator, JwtAuthTokenGenerator>();

// --- Persistence --------------------------------------------------------------------------------
// The shared ShuffullContext lives in Shuffull.Core, but the EF migrations now live in this
// assembly (Migrations/), so EF must be told where to find them.
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Shuffull"),
        sql => sql.MigrationsAssembly("Shuffull.Api"));
});

// Expose the concrete ShuffullContext as the base DbContext so Shuffull.Core's generic
// UnitOfWork/Repository (which depend only on DbContext) resolve the request-scoped context.
builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShuffullContext>());
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// --- CQRS pipeline (mirrors the Sociallite backend) ---------------------------------------------
// MediatR handlers + FluentValidation validators are discovered from Shuffull.Core, where every
// feature slice lives. ValidationBehavior runs the validators before each handler and short-circuits
// to Result.Error on failure.
var coreAssembly = typeof(ValidationBehavior<,>).Assembly;
builder.Services.AddValidatorsFromAssemblies(new[] { coreAssembly });
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(coreAssembly);
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

// --- Song import / metadata pipeline ------------------------------------------------------------
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddHostedService<TagImporterService>();
builder.Services.AddHostedService<DetailedSongImporterService>();
builder.Services.TryAddAIService(builder.Configuration);
builder.Services.AddHostedService<SongImportService>();
builder.Services.AddHostedService<ExternalSongImporterService>();
// TODO: Register IYouTubeApiService -> YouTubeApiService once a "YouTubeApi" config section (ApiKey)
// is added. SongImportService resolves it via the nullable GetService<IYouTubeApiService>(), so it's
// safe to leave unregistered for now (AI genre-context enrichment is simply skipped).

// --- Logging ------------------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Host.UseNLog();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see
    // https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// TODO: Move into its own file, and abstract the file access layer.
var filesConfig = builder.Configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>()
    ?? throw new InvalidOperationException("Files configuration is not set.");

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

// Static serving of the music library and album art (no wwwroot — there is no UI).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(filesConfig.MusicRootDirectory),
    RequestPath = "/music"
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(filesConfig.AlbumArtDirectory),
    RequestPath = "/albumart"
});

app.UseRouting();

// Resolves the bearer token (if any) to HttpContext.Items["User"] for the [Authorize] filter.
app.UseMiddleware<JwtMiddleware>();

app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShuffullContext>();
    db.Database.Migrate();
}

app.Run();

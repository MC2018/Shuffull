using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Shuffull.Site.Configuration;
using Shuffull.Site.Tools;
using Shuffull.Site;
using Shuffull.Site.Tools.Authorization;
using NLog.Web;
using Shuffull.Site.Services.FileStorage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();
builder.Services.AddSingleton<SongImporter>();
builder.Services.AddHostedService<TagImporter>();
builder.Services.AddHostedService<StartupImportService>();
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shuffull"));
});
builder.Services.TryAddApiService(builder.Configuration);
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
Directory.CreateDirectory(filesConfig.SavedAiResponsesDirectory);
Directory.CreateDirectory(filesConfig.AlbumArtDirectory);

if (!File.Exists(filesConfig.GenresFile))
{
    File.WriteAllText(filesConfig.GenresFile, string.Empty);
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

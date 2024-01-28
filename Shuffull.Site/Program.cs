using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Shuffull.Site.Database;
using Shuffull.Site.Configuration;
using Shuffull.Site.Tools;
using System.Net;
using Shuffull.Site;
using Shuffull.Site.Tools.Authorization;
using static System.Formats.Asn1.AsnWriter;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHostedService<SongImporter>();
builder.Services.AddHostedService<TagImporter>();
builder.Services.AddScoped<JwtHelper>();
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shuffull"));
});
builder.Services.AddSingleton<OpenAIManager>();
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

var filesConfig = builder.Configuration.GetSection(ShuffullFilesConfiguration.FilesConfigurationSection).Get<ShuffullFilesConfiguration>();

Directory.CreateDirectory(filesConfig.FailedImportDirectory);
Directory.CreateDirectory(filesConfig.MusicRootDirectory);
Directory.CreateDirectory(filesConfig.ManualSongImportDirectory);
Directory.CreateDirectory(filesConfig.SongImportDirectory);
Directory.CreateDirectory(filesConfig.SavedAiResponsesDirectory);

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        filesConfig.MusicRootDirectory
    ),
    RequestPath = "/music"
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Shuffull.Site.Database;
using Shuffull.Site.Configuration;
using Shuffull.Site.Tools;
using Shuffull.Site.Services;
using System.Net;
using Shuffull.Site;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<SongImportService>();
builder.Services.AddDbContext<ShuffullContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Shuffull"));
});

var app = builder.Build();

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
Directory.CreateDirectory(filesConfig.SongImportDirectory);
FileRetrieval.RootDirectory = filesConfig.MusicRootDirectory;

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(
        FileRetrieval.RootDirectory
    ),
    RequestPath = "/music"
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

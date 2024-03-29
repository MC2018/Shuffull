using LibVLCSharp.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shuffull.Shared;
using Shuffull.Shared.Models.Server;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Tools;
using System.Net.Http.Json;

namespace Shuffull.Windows
{
    internal static class Program
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        async static Task Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            InitializeDatabase();
            ApplicationConfiguration.Initialize();
            Core.Initialize();
            ConfigureServices();
            Directory.CreateDirectory(Directories.LocalDataAbsolutePath);
            Directory.CreateDirectory(Directories.MusicFolderAbsolutePath);
            Application.Run(new Login());
        }

        static void ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddTransient(provider =>
            {
                var context = new ShuffullContext(Directories.DatabaseFileAbsolutePath);
                context.Database.SetCommandTimeout(5);
                return context;
            });
            ServiceProvider = services.BuildServiceProvider();
        }

        static void InitializeDatabase()
        {
            using var context = new ShuffullContext(Directories.DatabaseFileAbsolutePath);

            if (context.Database.GetPendingMigrations().Any())
            {
                try
                {
                    context.Database.Migrate();
                }
                catch (Exception)
                {
                    File.Delete(Directories.DatabaseFileAbsolutePath);
                    context.Database.Migrate();
                }
            }
        }
    }
}
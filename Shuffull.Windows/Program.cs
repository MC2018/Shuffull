using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shuffull.Shared;
using Shuffull.Windows.Constants;
using Shuffull.Windows.Tools;

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
            ApplicationConfiguration.Initialize();
            ConfigureServices();
            Directory.CreateDirectory(Directories.LocalDataAbsolutePath);
            Directory.CreateDirectory(Directories.MusicFolderAbsolutePath);
            await SyncManager.Initialize();
            Application.Run(new Home());
        }

        static void ConfigureServices()
        {
            var services = new ServiceCollection();
            services.AddHttpClient();
            services.AddTransient(provider =>
            {
                return new ShuffullContext(Directories.DatabaseFileAbsolutePath);
            });
            ServiceProvider = services.BuildServiceProvider();
        }
    }
}
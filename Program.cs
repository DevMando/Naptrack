using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using Naptrack.Components;
using Naptrack.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>(configure: config =>
    {
        config.ConfigureServices(services =>
        {
            services.AddSingleton<ConfigService>();
            services.AddSingleton<BinaryDownloader>();
            services.AddSingleton<DependencyChecker>();
            services.AddSingleton<YtDlpService>();
            services.AddSingleton<FolderPickerService>();
        });
    });

IHost host = hostBuilder.Build();
await host.RunAsync();

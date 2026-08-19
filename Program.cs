using System.Text;
using Spectre.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using Naptrack.Components;
using Naptrack.Services;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

ClearScreen();

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>(configure: config =>
    {
        config.ConfigureServices(services =>
        {
            services.AddSingleton<ConfigService>();
            services.AddSingleton<UpdateChecker>();
            services.AddSingleton<BinaryDownloader>();
            services.AddSingleton<DependencyChecker>();
            services.AddSingleton<YtDlpUpdater>();
            services.AddSingleton<YtDlpService>();
            services.AddSingleton<FolderPickerService>();
        });
    });

IHost host = hostBuilder.Build();
await host.RunAsync();

// Naptrack paints a full-screen UI, so whatever the shell left on screen would otherwise sit
// behind and above it for the whole session.
static void ClearScreen()
{
    try
    {
        Console.Clear();

        // Console.Clear only wipes the visible window; the previous commands are still one
        // scroll away. Dropping the scrollback needs an escape sequence, and a terminal that
        // does not understand it would print the sequence literally instead of acting on it --
        // so it is sent only where Spectre reports that ANSI is actually supported.
        if (AnsiConsole.Profile.Capabilities.Ansi)
        {
            Console.Write("[3J");
        }
    }
    catch (IOException)
    {
        // Output is redirected to a file or a pipe. There is no screen to clear, and failing to
        // clear one is never a reason not to start.
    }
}

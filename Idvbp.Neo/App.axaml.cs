using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Idvbp.Neo.Core;
using Idvbp.Neo.Services;
using Idvbp.Neo.Views;

namespace Idvbp.Neo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = AppHost.Current.GetRequiredService<MainWindow>();
            GlobalExceptionHandler.Initialize(desktop.MainWindow);

            _ = HandleBackendSelectionAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task HandleBackendSelectionAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var prefService = AppHost.Current.GetRequiredService<BackendPreferenceService>();
            var pref = await prefService.GetAsync();

            if (pref.BackendMode == BackendMode.NotSet)
            {
                var dialog = new BackendSelectionDialog();
                var result = await dialog.ShowDialog<BackendMode>(desktop.MainWindow!);

                if (result == BackendMode.Native || result == BackendMode.Web)
                {
                    await prefService.SetAsync(result);
                    pref = await prefService.GetAsync();
                }
                else
                {
                    await prefService.SetAsync(BackendMode.Native);
                    pref = await prefService.GetAsync();
                }
            }

            if (pref.BackendMode == BackendMode.Web)
            {
                var config = AppHost.Current.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                var serverUrl = config["Server:Urls"] ?? "http://localhost:5000";
                var localBpUrl = $"{serverUrl.TrimEnd('/')}/local-bp/local-bp.html";

                var webWindow = new WebBackendWindow("Idvbp.Neo - Web 后台", localBpUrl);
                webWindow.Show();
                webWindow.Closed += (_, _) =>
                {
                    desktop.MainWindow?.Close();
                };
            }
        }
        catch
        {
        }
    }
}

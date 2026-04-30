using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Idvbp.Neo.Client;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.Service;
using Idvbp.Neo.Server.Services;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.ViewModels;
using Idvbp.Neo.ViewModels.Pages;
using Idvbp.Neo.Views;
using Idvbp.Neo.Views.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo;

public partial class App
{
    /// <summary>
    /// Registers desktop-side services, windows, pages, and view models.
    /// </summary>
    /// <param name="context">The host builder context.</param>
    /// <param name="services">The application service collection.</param>
    public static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        var databasePath = context.Configuration.GetValue<string>("LiteDb:DatabasePath") ?? "data/idvbp-neo.db";
        var resourcesPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Resources");
        var wwwrootPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "wwwroot");
        if (!System.IO.Directory.Exists(wwwrootPath))
        {
            wwwrootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot");
        }

        // TODO: register Avalonia services here
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<INavigationPageFactory, NavigationPageFactory>();
        services.AddSingleton(_ => new BpApiClient(context.Configuration["Server:Urls"] ?? "http://localhost:5000"));
        services.AddSingleton<RoomRealtimeClient>();
        services.AddSingleton<BpRoomWorkspace>();
        services.AddSingleton<IProxyPageConfigRepository>(_ => new LiteDbProxyPageConfigRepository(databasePath));
        services.AddSingleton<IResourceCatalogService>(_ => new ResourceCatalogService(resourcesPath));
        services.AddSingleton<IFrontendPackageService>(_ => new FrontendPackageService(wwwrootPath));
        services.AddSingleton<IOfficialCharacterModelService>(sp =>
            new OfficialCharacterModelService(wwwrootPath, sp.GetRequiredService<IResourceCatalogService>()));

        // TODO: register windows here
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<INavigationPageFactory>())
            {
                DataContext = sp.GetRequiredService<MainWindowViewModel>(),
            });

        // TODO: register view models here
        services.AddSingleton<MainWindowViewModel>();

        AddPage<HomePage, HomePageViewModel>(services);
        AddPage<TeamInfo, TeamInfoPageViewModel>(services);
        AddPage<MapBp, MapBpPageViewModel>(services);
        AddPage<BanHunPage, BanHunPageViewModel>(services);
        AddPage<BanSurPage, BanSurPageViewModel>(services);
        AddPage<PickPage, PickPageViewModel>(services);
        AddPage<TalentPage, TalentPageViewModel>(services);
        AddPage<ScorePage, ScorePageViewModel>(services);
        AddPage<GameDataPage, GameDataPageViewModel>(services);
        AddPage<SmartBpPage, SmartBpPageViewModel>(services);
        AddPage<PluginPage, PluginPageViewModel>(services);
        AddPage<FrontManagePage, FrontManagePageViewModel>(services);
        AddPage<WebProxyPage, WebProxyPageViewModel>(services);
        AddPage<SettingPage, SettingPageViewModel>(services);

        // TODO: register plugins here later
    }

    /// <summary>
    /// Registers a page and its view model as transient services.
    /// </summary>
    /// <typeparam name="TView">The Avalonia page control type.</typeparam>
    /// <typeparam name="TViewModel">The page view model type.</typeparam>
    /// <param name="services">The application service collection.</param>
    private static void AddPage<TView, TViewModel>(IServiceCollection services)
        where TView : Control, new()
        where TViewModel : class
    {
        services.AddTransient<TViewModel>();
        services.AddTransient<TView>(sp => new TView
        {
            DataContext = sp.GetRequiredService<TViewModel>(),
        });
    }
}

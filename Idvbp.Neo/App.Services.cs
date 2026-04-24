using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.Service;
using Idvbp.Neo.ViewModels;
using Idvbp.Neo.ViewModels.Pages;
using Idvbp.Neo.Views;
using Idvbp.Neo.Views.Pages;
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
        // TODO: register Avalonia services here
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<INavigationPageFactory, NavigationPageFactory>();

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

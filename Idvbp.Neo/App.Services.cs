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

/// <summary>
/// 应用程序依赖注入配置部分类，注册桌面端服务、窗口、页面及视图模型。
/// </summary>
public partial class App
{
    /// <summary>
    /// 注册桌面端服务、窗口、页面和视图模型到依赖注入容器。
    /// </summary>
    /// <param name="context">主机构建上下文。</param>
    /// <param name="services">应用程序服务集合。</param>
    public static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        // 读取配置：数据库路径、资源目录、Web 根目录
        var databasePath = context.Configuration.GetValue<string>("LiteDb:DatabasePath") ?? "data/idvbp-neo.db";
        var resourcesPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Resources");
        var wwwrootPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "wwwroot");
        if (!System.IO.Directory.Exists(wwwrootPath))
        {
            wwwrootPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "wwwroot");
        }

        // 注册 Avalonia 导航相关服务
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<INavigationPageFactory, NavigationPageFactory>();

        // 注册客户端与实时通信服务
        services.AddSingleton(_ => new BpApiClient(context.Configuration["Server:Urls"] ?? "http://localhost:5000"));
        services.AddSingleton<RoomRealtimeClient>();
        services.AddSingleton<BpRoomWorkspace>();

        // 注册数据持久化与资源服务
        services.AddSingleton<IProxyPageConfigRepository>(_ => new LiteDbProxyPageConfigRepository(databasePath));
        services.AddSingleton<IResourceCatalogService>(_ => new ResourceCatalogService(resourcesPath));
        services.AddSingleton<IFrontendPackageService>(_ => new FrontendPackageService(wwwrootPath));
        services.AddSingleton<IOfficialCharacterModelService>(sp =>
            new OfficialCharacterModelService(wwwrootPath, sp.GetRequiredService<IResourceCatalogService>()));

        // 注册主窗口（单例）
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<INavigationPageFactory>())
            {
                DataContext = sp.GetRequiredService<MainWindowViewModel>(),
            });

        // 注册主窗口视图模型（单例）
        services.AddSingleton<MainWindowViewModel>();

        // 注册各功能页面及其视图模型
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

        services.AddTransient<LogViewerViewModel>();

        // 后续可在此处注册插件服务
    }

    /// <summary>
    /// 将页面及其视图模型注册为瞬态服务。
    /// </summary>
    /// <typeparam name="TView">Avalonia 页面控件类型。</typeparam>
    /// <typeparam name="TViewModel">页面对应的视图模型类型。</typeparam>
    /// <param name="services">应用程序服务集合。</param>
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

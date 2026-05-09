using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using Idvbp.Neo.Core;
using Idvbp.Neo.Services;
using Idvbp.Neo.Views;

namespace Idvbp.Neo;

/// <summary>
/// Avalonia 应用程序根类，负责初始化 XAML 资源并在框架初始化完成后设置主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 加载应用程序的 XAML 资源字典。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 当 Avalonia 框架初始化完成时调用，设置桌面生命周期并指定主窗口。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 设置关闭模式：主窗口关闭时应用退出
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            // 从 DI 容器解析主窗口实例
            desktop.MainWindow = AppHost.Current.GetRequiredService<MainWindow>();
            GlobalExceptionHandler.Initialize(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.Models;
using Idvbp.Neo.ViewModels;
using NavigationViewItem = Idvbp.Neo.Controls.NavigationViewItem;

namespace Idvbp.Neo.Views;

/// <summary>
/// 主桌面窗口壳层，承载全局控件与页面导航框架。
/// </summary>
public partial class MainWindow : AppWindow
{
    private readonly INavigationService? _navigationService;
    private bool _initialNavigationDone;
    private bool _isCloseConfirmed;

    /// <summary>
    /// 初始化主窗口，配置自定义标题栏行为。
    /// </summary>
    public MainWindow()
    {
        // AppWindow 控制原生标题栏行为；XAML 中的标题栏为自定义内容，
        // 因此命中测试保持复杂模式，以区分窗口按钮与可拖动区域。
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        InitializeComponent();
    }

    /// <summary>
    /// 使用导航服务与页面工厂初始化主窗口。
    /// </summary>
    /// <param name="navigationService">导航服务实例。</param>
    /// <param name="navigationPageFactory">页面工厂实例。</param>
    public MainWindow(INavigationService navigationService, INavigationPageFactory navigationPageFactory)
        : this()
    {
        _navigationService = navigationService;

        ContentFrame.NavigationPageFactory = navigationPageFactory;
        navigationService.SetNavigationControl(RootNavigation);
        navigationService.SetFrameControl(ContentFrame);

        RootNavigation.ItemInvoked += RootNavigation_ItemInvoked;
        Loaded += MainWindow_Loaded;
    }

    /// <summary>
    /// 窗口加载完成后执行初始导航。
    /// </summary>
    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_initialNavigationDone)
        {
            return;
        }

        _initialNavigationDone = true;

        if (DataContext is MainWindowViewModel { NavigationSelectedItem.TargetPageType: { } pageType })
        {
            _navigationService?.Navigate(pageType);
        }
    }

    /// <summary>
    /// 导航项被调用时，根据目标页面类型执行导航。
    /// </summary>
    private void RootNavigation_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem { TargetPageType: { } pageType })
        {
            _navigationService?.Navigate(pageType);
        }
    }

    private async void RoomSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.AddedItems.Count == 0)
        {
            if (sender is ComboBox comboBox && viewModel.Workspace.SelectedRoom is not null)
            {
                comboBox.SelectedItem = viewModel.Workspace.SelectedRoom;
            }

            return;
        }

        if (e.AddedItems[0] is not BpRoom room)
        {
            return;
        }

        if (string.Equals(room.RoomId, viewModel.Workspace.SelectedRoom?.RoomId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await viewModel.Workspace.SwitchRoomAsync(room.RoomId);
    }

    /// <summary>
    /// 关闭窗口前显示确认对话框。
    /// </summary>
    /// <param name="e">窗口关闭事件参数。</param>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isCloseConfirmed)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        var messageBox = new ContentDialog
        {
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            Title = "退出确认",
            Content = "是否退出程序？",
            IsPrimaryButtonEnabled = true,
            IsSecondaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await messageBox.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _isCloseConfirmed = true;
            ShutdownApplication();
        }
    }

    /// <summary>
    /// 关闭应用程序，优先使用桌面生命周期接口。
    /// </summary>
    private void ShutdownApplication()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Close();
    }
}

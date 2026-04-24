using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Idvbp.Neo.Core.Abstractions.Services;
using Idvbp.Neo.ViewModels;
using NavigationViewItem = Idvbp.Neo.Controls.NavigationViewItem;

namespace Idvbp.Neo.Views;

/// <summary>
/// Main desktop shell that hosts the global controls and page navigation frame.
/// </summary>
public partial class MainWindow : AppWindow
{
    private readonly INavigationService? _navigationService;
    private bool _initialNavigationDone;
    private bool _isCloseConfirmed;

    public MainWindow()
    {
        // AppWindow owns the native title bar behavior. The XAML title bar is custom content,
        // so hit testing stays complex to keep window buttons and draggable areas distinct.
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        InitializeComponent();
    }

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

    private void RootNavigation_ItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (e.InvokedItemContainer is NavigationViewItem { TargetPageType: { } pageType })
        {
            _navigationService?.Navigate(pageType);
        }
    }

    /// <summary>
    /// Shows a confirmation dialog before closing the main window.
    /// </summary>
    /// <param name="e">The window closing event args.</param>
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

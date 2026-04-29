using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Idvbp.Neo.ViewModels.Pages;
using Idvbp.Neo.Views;

namespace Idvbp.Neo.Views.Pages;

public partial class FrontManagePage : UserControl
{
    public FrontManagePage()
    {
        InitializeComponent();
    }

    private async void ImportPackageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrontManagePageViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入前台 ZIP 包",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZIP 前台包")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } path)
        {
            await viewModel.ImportPackageAsync(path);
        }
    }

    private void OpenPackageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPackageItemViewModel package })
        {
            OpenUrl(package.Name, package.LaunchUrl);
        }
    }

    private void OpenPageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPageItemViewModel page })
        {
            OpenUrl(page.Name, page.LaunchUrl);
        }
    }

    private void OpenUrl(string title, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return;
        }

        var window = new WebProxyBrowserWindow(title, url);
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
    }
}

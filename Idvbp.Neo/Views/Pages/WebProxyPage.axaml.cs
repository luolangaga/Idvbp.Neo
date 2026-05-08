using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Idvbp.Neo.Core;
using Idvbp.Neo.Server.Services;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Idvbp.Neo.ViewModels.Pages;
using Idvbp.Neo.Views;
using TextMateSharp.Grammars;

namespace Idvbp.Neo.Views.Pages;

public partial class WebProxyPage : UserControl
{
    public WebProxyPage()
    {
        InitializeComponent();
    }

    private async void EditRouteConfigButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ProxyRouteItemViewModel route } ||
            DataContext is not WebProxyPageViewModel viewModel)
        {
            return;
        }

        var latestConfig = await viewModel.RefreshRoutePageConfigAsync(route);
        await ShowEditConfigWindowAsync(
            $"编辑页面配置: {route.Name}",
            latestConfig,
            text => viewModel.UpdateRoutePageConfigAsync(route, text),
            () => viewModel.RefreshRoutePageConfigAsync(route),
            viewModel);
    }

    private void OpenInWebViewButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: ProxyRouteItemViewModel route } ||
            !Uri.TryCreate(route.PublicUrl, UriKind.Absolute, out _))
        {
            return;
        }

        var window = new WebProxyBrowserWindow(route.Name, route.PublicUrl);
        ShowBrowserWindow(window);
    }

    private async void ImportFrontendPackageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WebProxyPageViewModel viewModel ||
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
            await viewModel.ImportFrontendPackageAsync(path);
        }
    }

    private void OpenFrontendPackageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPackageItemViewModel package } &&
            Uri.TryCreate(package.LaunchUrl, UriKind.Absolute, out _))
        {
            ShowBrowserWindow(new WebProxyBrowserWindow(package.Name, package.LaunchUrl));
        }
    }

    private void OpenFrontendPageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPageItemViewModel page } &&
            Uri.TryCreate(page.LaunchUrl, UriKind.Absolute, out _))
        {
            ShowBrowserWindow(new WebProxyBrowserWindow(page.Name, page.LaunchUrl, page.ViewportWidth, page.ViewportHeight));
        }
    }

    private void OpenFrontendStoreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var window = new FrontendPackageStoreWindow(
            AppHost.GetRequiredService<IFrontendPackageStoreService>(),
            AppHost.GetRequiredService<IFrontendPackageService>());
        ShowBrowserWindow(window);
    }

    private async void EditFrontendPageConfigButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FrontendPageItemViewModel page } ||
            DataContext is not WebProxyPageViewModel viewModel)
        {
            return;
        }

        var latestConfig = await viewModel.RefreshFrontendPageConfigAsync(page);
        var configTargets = viewModel.GetFrontendPageConfigTargets(page);
        await ShowEditConfigWindowAsync(
            $"编辑页面配置: {page.Name}",
            latestConfig,
            text => viewModel.UpdateFrontendPageConfigAsync(page, text),
            () => viewModel.RefreshFrontendPageConfigAsync(page),
            viewModel,
            configTargets,
            (target, text) => viewModel.UpdateFrontendConfigTargetAsync(page, target, text),
            target => viewModel.RefreshFrontendConfigTargetAsync(target));
    }

    private void EditFrontendLayoutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPageItemViewModel page } &&
            DataContext is WebProxyPageViewModel viewModel &&
            Uri.TryCreate(page.EditUrl, UriKind.Absolute, out _))
        {
            ShowBrowserWindow(new WebLayoutEditorWindow(
                $"布局编辑: {page.Name}",
                page.EditUrl,
                page.ViewportWidth,
                page.ViewportHeight,
                (width, height) => viewModel.UpdateFrontendPageViewportAsync(page, width, height)));
        }
    }

    private void OpenComponentDesignerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FrontendPageItemViewModel page } &&
            Uri.TryCreate(page.DesignerUrl, UriKind.Absolute, out _))
        {
            ShowBrowserWindow(new WebSimpleBrowserWindow(
                $"组件设计器: {page.Name}",
                page.DesignerUrl));
        }
    }

    private async void ExportFrontendPackageButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FrontendPackageItemViewModel package } ||
            DataContext is not WebProxyPageViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出前台 ZIP 包",
            SuggestedFileName = $"{package.Id}.zip",
            FileTypeChoices =
            [
                new FilePickerFileType("ZIP 前台包")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"]
                }
            ]
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            await viewModel.ExportFrontendPackageAsync(package, path);
        }
    }

    private void ShowBrowserWindow(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
    }

    private async Task ShowEditConfigWindowAsync(
        string title,
        string initialText,
        Func<string, Task<bool>> saveAsync,
        Func<Task<string>> reloadAsync,
        WebProxyPageViewModel viewModel,
        IReadOnlyList<FrontendConfigTargetItemViewModel>? configTargets = null,
        Func<FrontendConfigTargetItemViewModel, string, Task<bool>>? saveTargetAsync = null,
        Func<FrontendConfigTargetItemViewModel, Task<string>>? reloadTargetAsync = null)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var ownerWidth = owner?.Bounds.Width ?? 1400;
        var ownerHeight = owner?.Bounds.Height ?? 900;
        var windowWidth = Math.Clamp(ownerWidth * 0.78, 820, 1280);
        var windowHeight = Math.Clamp(ownerHeight * 0.82, 620, 920);

        var formatText = new TextBlock
        {
            Text = $"格式: {ProxyPageConfigTextHelper.DetectFormat(initialText)}",
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        };

        var statusText = new TextBlock
        {
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var currentTarget = configTargets?.FirstOrDefault();
        if (currentTarget is not null)
        {
            initialText = currentTarget.Config;
        }

        var editor = new TextEditor
        {
            Text = initialText,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Consolas, Cascadia Code, JetBrains Mono, monospace"),
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            Foreground = Brushes.Gainsboro,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Padding = new Thickness(8),
            WordWrap = false
        };
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#264F78"));
        editor.TextArea.Caret.CaretBrush = Brushes.White;

        RegistryOptions? registryOptions = null;
        TextMate.Installation? textMateInstallation = null;
        string? activeScope = null;

        try
        {
            registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            textMateInstallation = editor.InstallTextMate(registryOptions);
        }
        catch (Exception ex)
        {
            statusText.Text = $"高亮已禁用: {ex.Message}";
        }

        void ApplyWordWrap(bool enabled)
        {
            editor.WordWrap = enabled;
            editor.HorizontalScrollBarVisibility = enabled
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }

        void RefreshLanguage()
        {
            var format = ProxyPageConfigTextHelper.DetectFormat(editor.Text);
            formatText.Text = $"格式: {format}";

            if (registryOptions is null || textMateInstallation is null)
            {
                return;
            }

            try
            {
                var extension = ProxyPageConfigTextHelper.GetSuggestedExtension(format);
                var language = registryOptions.GetLanguageByExtension(extension);
                if (language is null)
                {
                    return;
                }

                var scope = registryOptions.GetScopeByLanguageId(language.Id);
                if (string.Equals(scope, activeScope, StringComparison.Ordinal))
                {
                    return;
                }

                textMateInstallation.SetGrammar(scope);
                activeScope = scope;
            }
            catch (Exception ex)
            {
                registryOptions = null;
                textMateInstallation = null;
                statusText.Text = $"高亮已回退到纯文本: {ex.Message}";
            }
        }

        editor.TextChanged += (_, _) =>
        {
            try
            {
                RefreshLanguage();
            }
            catch (Exception ex)
            {
                statusText.Text = $"格式识别失败，已保留纯文本编辑: {ex.Message}";
            }
        };

        RefreshLanguage();
        ApplyWordWrap(false);

        var wrapSwitch = new CheckBox
        {
            Content = "自动换行",
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = false
        };
        wrapSwitch.IsCheckedChanged += (_, _) => ApplyWordWrap(wrapSwitch.IsChecked == true);

        var targetSelector = new ComboBox
        {
            ItemsSource = configTargets,
            SelectedItem = currentTarget,
            MinWidth = 220,
            MaxWidth = 360,
            IsVisible = configTargets is { Count: > 0 },
            VerticalAlignment = VerticalAlignment.Center
        };
        targetSelector.SelectionChanged += (_, _) =>
        {
            if (targetSelector.SelectedItem is not FrontendConfigTargetItemViewModel selected)
            {
                return;
            }

            currentTarget = selected;
            editor.Text = selected.Config;
            RefreshLanguage();
            statusText.Text = selected.Kind == "component"
                ? $"正在编辑组件配置: {selected.Id}"
                : "正在编辑页面配置";
        };

        var formatButton = new Button
        {
            Content = "自动格式化",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        formatButton.Click += (_, _) =>
        {
            if (ProxyPageConfigTextHelper.TryFormat(editor.Text, out var formatted))
            {
                editor.Text = formatted;
                statusText.Text = "已自动格式化";
            }
            else
            {
                statusText.Text = "当前格式不支持自动格式化";
            }
        };

        var reloadButton = new Button
        {
            Content = "刷新",
            MinWidth = 80
        };
        reloadButton.Click += async (_, _) =>
        {
            try
            {
                reloadButton.IsEnabled = false;
                var latest = currentTarget is not null && reloadTargetAsync is not null
                    ? await reloadTargetAsync(currentTarget)
                    : await reloadAsync();
                editor.Text = latest;
                RefreshLanguage();
                statusText.Text = "已重新读取最新配置";
            }
            catch (Exception ex)
            {
                statusText.Text = $"刷新失败: {ex.Message}";
            }
            finally
            {
                reloadButton.IsEnabled = true;
            }
        };

        var saveButton = new Button
        {
            Content = "保存",
            MinWidth = 96
        };

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 96
        };

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 10
        };
        toolbar.Children.Add(formatText);
        toolbar.Children.Add(targetSelector);
        toolbar.Children.Add(statusText);
        toolbar.Children.Add(wrapSwitch);
        toolbar.Children.Add(reloadButton);
        toolbar.Children.Add(formatButton);
        Grid.SetColumn(targetSelector, 1);
        Grid.SetColumn(statusText, 2);
        Grid.SetColumn(wrapSwitch, 3);
        Grid.SetColumn(reloadButton, 4);
        Grid.SetColumn(formatButton, 5);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        footer.Children.Add(cancelButton);
        footer.Children.Add(saveButton);

        var root = new Grid
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12
        };
        root.Children.Add(toolbar);
        root.Children.Add(editor);
        root.Children.Add(footer);
        Grid.SetRow(editor, 1);
        Grid.SetRow(footer, 2);

        var window = new Window
        {
            Title = title,
            Width = windowWidth,
            Height = windowHeight,
            MinWidth = 720,
            MinHeight = 520,
            CanResize = true,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Content = root
        };

        var tcs = new TaskCompletionSource<bool>();

        saveButton.Click += async (_, _) =>
        {
            var updated = currentTarget is not null && saveTargetAsync is not null
                ? await saveTargetAsync(currentTarget, editor.Text ?? string.Empty)
                : await saveAsync(editor.Text ?? string.Empty);
            if (!updated)
            {
                statusText.Text = string.IsNullOrWhiteSpace(viewModel.Status) ? "保存失败" : viewModel.Status;
                return;
            }

            tcs.TrySetResult(true);
            window.Close();
        };

        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(false);
            window.Close();
        };

        window.Closed += (_, _) => tcs.TrySetResult(false);

        if (owner is not null)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        await tcs.Task;
    }
}

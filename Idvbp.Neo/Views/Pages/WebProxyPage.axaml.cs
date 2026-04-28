using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Idvbp.Neo.ViewModels.Pages;
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

        await ShowEditRouteConfigWindowAsync(route, viewModel);
    }

    private async Task ShowEditRouteConfigWindowAsync(ProxyRouteItemViewModel route, WebProxyPageViewModel viewModel)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var ownerWidth = owner?.Bounds.Width ?? 1400;
        var ownerHeight = owner?.Bounds.Height ?? 900;
        var windowWidth = Math.Clamp(ownerWidth * 0.78, 820, 1280);
        var windowHeight = Math.Clamp(ownerHeight * 0.82, 620, 920);

        var initialText = route.PageConfig;

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
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10
        };
        toolbar.Children.Add(formatText);
        toolbar.Children.Add(statusText);
        toolbar.Children.Add(wrapSwitch);
        toolbar.Children.Add(formatButton);
        Grid.SetColumn(statusText, 1);
        Grid.SetColumn(wrapSwitch, 2);
        Grid.SetColumn(formatButton, 3);

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
            Title = $"编辑页面配置: {route.Name}",
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
            var updated = await viewModel.UpdateRoutePageConfigAsync(route, editor.Text ?? string.Empty);
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

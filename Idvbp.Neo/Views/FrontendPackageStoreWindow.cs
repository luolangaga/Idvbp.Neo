using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Idvbp.Neo.Server.Services;

namespace Idvbp.Neo.Views;

public sealed class FrontendPackageStoreWindow : Window
{
    private readonly IFrontendPackageStoreService _storeService;
    private readonly IFrontendPackageService _frontendPackageService;
    private readonly ObservableCollection<StorePackageItem> _storeItems = [];
    private readonly ObservableCollection<FrontendPackageInfo> _localPackages = [];
    private readonly ListBox _storeList = new();
    private readonly ComboBox _localPackageSelector = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _subtitleText = new();
    private readonly TextBlock _metaText = new();
    private readonly TextBlock _statsText = new();
    private readonly ItemsControl _pagesList = new();
    private readonly Button _installButton = new();
    private readonly Button _githubButton = new();
    private string _selectedFileName = "";
    private string _selectedHtmlUrl = "";

    public FrontendPackageStoreWindow(
        IFrontendPackageStoreService storeService,
        IFrontendPackageService frontendPackageService)
    {
        _storeService = storeService;
        _frontendPackageService = frontendPackageService;
        Title = "页面包商店";
        Width = 1180;
        Height = 760;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RequestedThemeVariant = ThemeVariant.Dark;
        Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020");
        Content = BuildContent();
        Opened += async (_, _) => await ReloadAsync();
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020")
        };

        root.Children.Add(BuildHeader());
        var body = new Grid
        {
            Margin = new Avalonia.Thickness(12),
            ColumnDefinitions = new ColumnDefinitions("340,*"),
            ColumnSpacing = 12
        };
        Grid.SetRow(body, 1);
        body.Children.Add(BuildStoreListPanel());
        var details = BuildDetailsPanel();
        Grid.SetColumn(details, 1);
        body.Children.Add(details);
        root.Children.Add(body);
        return root;
    }

    private Control BuildHeader()
    {
        var header = new Border { Padding = new Avalonia.Thickness(10, 10, 10, 0) };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 20
        };
        var copy = new StackPanel { Spacing = 6 };
        copy.Children.Add(new TextBlock
        {
            Text = "页面包商店",
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
            FontSize = 28,
            FontWeight = FontWeight.SemiBold
        });
        copy.Children.Add(new TextBlock
        {
            Text = "从 GitHub 仓库发现、安装和发布页面管理页面包",
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7"),
            FontSize = 14
        });
        grid.Children.Add(copy);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(CreateHeaderButton("刷新", async (_, _) => await ReloadAsync()));
        actions.Children.Add(CreateHeaderButton("上传 ZIP", async (_, _) => await UploadZipAsync()));
        grid.Children.Add(actions);
        Grid.SetColumn(actions, 1);
        header.Child = grid;
        return header;
    }

    private Control BuildStoreListPanel()
    {
        var panel = CreateCard();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 14
        };

        grid.Children.Add(new TextBlock
        {
            Text = "商店资源",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });

        _statusText.Text = "准备加载商店";
        _statusText.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#607083");
        Grid.SetRow(_statusText, 1);
        grid.Children.Add(_statusText);

        _storeList.ItemsSource = _storeItems;
        _storeList.SelectionChanged += async (_, _) => await SelectCurrentStoreItemAsync();
        _storeList.ItemTemplate = new FuncDataTemplate<StorePackageItem>((item, _) =>
        {
            var root = new Grid
            {
                Margin = new Avalonia.Thickness(0, 0, 0, 10),
                ColumnDefinitions = new ColumnDefinitions("52,*"),
                ColumnSpacing = 12
            };
            root.Children.Add(new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new Avalonia.CornerRadius(8),
                Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
                Child = new TextBlock
                {
                    Text = "ZIP",
                    Foreground = ResourceBrush("AccentTextFillColorPrimaryBrush", "#2F6F8F"),
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var texts = new StackPanel { Spacing = 3 };
            texts.Children.Add(new TextBlock
            {
                Text = item?.DisplayName ?? "",
                FontWeight = FontWeight.SemiBold,
                Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = item?.FileName ?? "",
                Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#718093"),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            texts.Children.Add(new TextBlock
            {
                Text = item?.SizeText ?? "",
                Foreground = ResourceBrush("AccentTextFillColorPrimaryBrush", "#2C7A7B"),
                FontSize = 12
            });
            Grid.SetColumn(texts, 1);
            root.Children.Add(texts);
            return root;
        });
        Grid.SetRow(_storeList, 2);
        grid.Children.Add(_storeList);

        var publishPanel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 8
        };
        publishPanel.Children.Add(new TextBlock
        {
            Text = "发布本地页面包",
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        var publishRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        _localPackageSelector.ItemsSource = _localPackages;
        _localPackageSelector.ItemTemplate = new FuncDataTemplate<FrontendPackageInfo>((item, _) =>
            new TextBlock { Text = item is null ? "" : $"{item.Name} ({item.Id})" });
        publishRow.Children.Add(_localPackageSelector);
        var publishButton = CreatePrimaryButton("发布", async (_, _) => await PublishLocalAsync());
        Grid.SetColumn(publishButton, 1);
        publishRow.Children.Add(publishButton);
        Grid.SetRow(publishRow, 1);
        publishPanel.Children.Add(publishRow);
        Grid.SetRow(publishPanel, 3);
        grid.Children.Add(publishPanel);

        panel.Child = grid;
        return panel;
    }

    private Control BuildDetailsPanel()
    {
        var panel = CreateCard();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 16
        };

        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12
        };
        var titleStack = new StackPanel { Spacing = 5 };
        _titleText.Text = "选择一个页面包";
        _titleText.FontSize = 28;
        _titleText.FontWeight = FontWeight.SemiBold;
        _titleText.Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3");
        _subtitleText.Text = "查看页面、组件和资源信息，然后下载安装到本机。";
        _subtitleText.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#657589");
        titleStack.Children.Add(_titleText);
        titleStack.Children.Add(_subtitleText);
        titleRow.Children.Add(titleStack);
        _installButton.Content = "下载安装";
        _installButton.IsEnabled = false;
        _installButton.Click += async (_, _) => await InstallSelectedAsync();
        StylePrimaryButton(_installButton);
        Grid.SetColumn(_installButton, 1);
        titleRow.Children.Add(_installButton);
        grid.Children.Add(titleRow);

        var metaBand = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };
        metaBand.Children.Add(CreateInfoTile("文件与入口", _metaText));
        var statsTile = CreateInfoTile("结构统计", _statsText);
        Grid.SetColumn(statsTile, 1);
        metaBand.Children.Add(statsTile);
        Grid.SetRow(metaBand, 1);
        grid.Children.Add(metaBand);

        grid.Children.Add(new TextBlock
        {
            Text = "页面信息",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        Grid.SetRow(grid.Children[^1], 2);

        _pagesList.ItemTemplate = new FuncDataTemplate<FrontendPackageStorePageDetails>((page, _) =>
        {
            var item = new Border
            {
                Margin = new Avalonia.Thickness(0, 0, 0, 10),
                Padding = new Avalonia.Thickness(14),
                CornerRadius = new Avalonia.CornerRadius(8),
                BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#DFE7EE"),
                BorderThickness = new Avalonia.Thickness(1),
                Background = ResourceBrush("LayerFillColorAltBrush", "#242424")
            };
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = page?.Name ?? "",
                FontWeight = FontWeight.SemiBold,
                Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
            });
            stack.Children.Add(new TextBlock
            {
                Text = page is null ? "" : $"ID: {page.Id} · Layout: {page.Layout}",
                Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#6B7A8A"),
                FontSize = 12
            });
            item.Child = stack;
            return item;
        });
        var pagesScroll = new ScrollViewer { Content = _pagesList };
        Grid.SetRow(pagesScroll, 3);
        grid.Children.Add(pagesScroll);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        _githubButton.Content = "复制 GitHub 地址";
        _githubButton.IsEnabled = false;
        _githubButton.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedHtmlUrl) && Clipboard is not null)
            {
                await Clipboard.SetTextAsync(_selectedHtmlUrl);
                _statusText.Text = "已复制 GitHub 地址";
            }
        };
        footer.Children.Add(_githubButton);
        Grid.SetRow(footer, 4);
        grid.Children.Add(footer);

        panel.Child = grid;
        return panel;
    }

    private async Task ReloadAsync()
    {
        _statusText.Text = "正在连接 GitHub 商店...";
        _storeItems.Clear();
        _localPackages.Clear();

        foreach (var package in _frontendPackageService.GetPackages())
        {
            _localPackages.Add(package);
        }

        if (_localPackages.Count > 0)
        {
            _localPackageSelector.SelectedIndex = 0;
        }

        var status = _storeService.GetStatus();
        if (!status.Configured)
        {
            _statusText.Text = "商店仓库未配置";
            _titleText.Text = "需要配置 GitHub 仓库";
            _subtitleText.Text = "在 appsettings.json 中填写 FrontendPackageStore 后即可使用。";
            _installButton.IsEnabled = false;
            return;
        }

        try
        {
            foreach (var item in await _storeService.GetPackagesAsync())
            {
                _storeItems.Add(new StorePackageItem(item));
            }

            _statusText.Text = $"{status.Owner}/{status.Repository} · {_storeItems.Count} 个页面包";
            if (_storeItems.Count > 0)
            {
                _storeList.SelectedIndex = 0;
            }
            else
            {
                _titleText.Text = "商店还是空的";
                _subtitleText.Text = "上传 ZIP 或发布本地页面包后，这里会显示资源。";
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"加载失败：{ex.Message}";
        }
    }

    private async Task SelectCurrentStoreItemAsync()
    {
        if (_storeList.SelectedItem is not StorePackageItem item)
        {
            return;
        }

        _selectedFileName = item.FileName;
        _selectedHtmlUrl = item.HtmlUrl;
        _titleText.Text = item.DisplayName;
        _subtitleText.Text = "正在读取页面包详情...";
        _metaText.Text = item.FileName;
        _statsText.Text = item.SizeText;
        _pagesList.ItemsSource = Array.Empty<FrontendPackageStorePageDetails>();
        _installButton.IsEnabled = false;
        _githubButton.IsEnabled = !string.IsNullOrWhiteSpace(item.HtmlUrl);

        try
        {
            var details = await _storeService.GetPackageDetailsAsync(item.FileName);
            _titleText.Text = details.Name;
            _subtitleText.Text = string.IsNullOrWhiteSpace(details.Version)
                ? $"{details.PackageId} · {details.Type}"
                : $"{details.PackageId} · v{details.Version} · {details.Type}";
            _metaText.Text = $"文件：{details.FileName}\n入口：{details.EntryLayout}\n大小：{FormatBytes(details.SizeBytes)}";
            _statsText.Text = $"页面：{details.Pages.Count}\n组件文件：{details.ComponentFileCount}\n资源文件：{details.AssetFileCount}";
            _pagesList.ItemsSource = details.Pages;
            _installButton.IsEnabled = true;
            _statusText.Text = $"已读取 {details.Name}";
        }
        catch (Exception ex)
        {
            _subtitleText.Text = $"详情读取失败：{ex.Message}";
            _installButton.IsEnabled = true;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedFileName))
        {
            return;
        }

        try
        {
            _installButton.IsEnabled = false;
            _statusText.Text = $"正在安装 {_selectedFileName}...";
            var package = await _storeService.InstallPackageAsync(_selectedFileName);
            _statusText.Text = $"已安装 {package.Name}";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"安装失败：{ex.Message}";
        }
        finally
        {
            _installButton.IsEnabled = true;
        }
    }

    private async Task UploadZipAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "上传页面包 ZIP",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("ZIP 页面包")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            _statusText.Text = "正在上传 ZIP...";
            var options = await PromptUploadOptionsAsync("上传 ZIP 到页面包商店");
            if (options is null)
            {
                _statusText.Text = "已取消上传";
                return;
            }

            var result = await _storeService.UploadFileAsync(path, options);
            _statusText.Text = BuildUploadResultText(result);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"上传失败：{ex.Message}";
        }
    }

    private async Task PublishLocalAsync()
    {
        if (_localPackageSelector.SelectedItem is not FrontendPackageInfo package)
        {
            _statusText.Text = "请选择要发布的本地页面包";
            return;
        }

        try
        {
            _statusText.Text = $"正在发布 {package.Name}...";
            var options = await PromptUploadOptionsAsync($"发布 {package.Name}");
            if (options is null)
            {
                _statusText.Text = "已取消发布";
                return;
            }

            var result = await _storeService.UploadLocalPackageAsync(package.Id, options);
            _statusText.Text = BuildUploadResultText(result);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"发布失败：{ex.Message}";
        }
    }

    private static Border CreateCard()
    {
        var card = new Border
        {
            Margin = new Avalonia.Thickness(0, 0, 10, 10),
            Padding = new Avalonia.Thickness(14),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", "#2B2B2B"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
            BorderThickness = new Avalonia.Thickness(1)
        };
        return card;
    }

    private static Border CreateInfoTile(string title, TextBlock value)
    {
        value.Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3");
        value.FontSize = 13;
        value.TextWrapping = TextWrapping.Wrap;
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#6C7A89"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(value);
        return new Border
        {
            Padding = new Avalonia.Thickness(14),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = stack
        };
    }

    private static Button CreateHeaderButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 38,
            Padding = new Avalonia.Thickness(14, 0),
            CornerRadius = new Avalonia.CornerRadius(7)
        };
        button.Click += click;
        return button;
    }

    private static Button CreatePrimaryButton(string text, EventHandler<RoutedEventArgs> click)
    {
        var button = new Button { Content = text };
        StylePrimaryButton(button);
        button.Click += click;
        return button;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.MinHeight = 38;
        button.Padding = new Avalonia.Thickness(14, 0);
        button.CornerRadius = new Avalonia.CornerRadius(7);
        button.Classes.Add("accent");
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = Math.Max(0, (double)value);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }

    private async Task<FrontendPackageStoreUploadOptions?> PromptUploadOptionsAsync(string title)
    {
        var authState = _storeService.GetAuthState();
        var tokenBox = new TextBox
        {
            Watermark = "也可以手动粘贴 GitHub Token",
            PasswordChar = '*',
            MinWidth = 520,
            IsEnabled = !authState.HasSavedToken
        };

        var status = new TextBlock
        {
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#6B7A8A"),
            TextWrapping = TextWrapping.Wrap,
            Text = authState.HasSavedToken
                ? "已存在本地授权。可以直接使用，也可以清除后重新授权。"
                : "推荐使用 GitHub 快速授权。授权可保存到本地；直推失败会自动创建 PR，审核合并后商店同步。"
        };

        var help = new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#CBE3EA"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = status
        };

        var saveTokenCheck = new CheckBox
        {
            Content = "保存授权到本地，下次上传不再询问",
            IsChecked = true
        };
        var submit = CreatePrimaryButton(authState.HasSavedToken ? "使用已保存授权" : "提交", (_, _) => { });
        Button oauthButton = null!;
        oauthButton = CreateHeaderButton("GitHub 快速授权", async (_, _) =>
        {
            try
            {
                oauthButton.IsEnabled = false;
                status.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7");
                status.Text = "正在向 GitHub 请求授权码...";
                var code = await _storeService.BeginDeviceAuthorizationAsync();
                if (Clipboard is not null)
                {
                    await Clipboard.SetTextAsync(code.UserCode);
                }

                status.Text = $"浏览器会打开 GitHub 授权页。验证码 {code.UserCode} 已复制，粘贴后完成授权。";
                Process.Start(new ProcessStartInfo
                {
                    FileName = code.VerificationUri,
                    UseShellExecute = true
                });

                var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, code.ExpiresIn));
                while (DateTimeOffset.UtcNow < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, code.Interval)));
                    var result = await _storeService.PollDeviceAuthorizationAsync(code.DeviceCode);
                    if (result.Success)
                    {
                        tokenBox.Text = result.AccessToken;
                        tokenBox.IsEnabled = false;
                        status.Foreground = ResourceBrush("AccentTextFillColorPrimaryBrush", "#8AB4F8");
                        status.Text = "GitHub 授权成功。点击提交即可上传。";
                        return;
                    }

                    if (result.Error is not "authorization_pending" and not "slow_down")
                    {
                        status.Foreground = Brush.Parse("#FCA5A5");
                        status.Text = $"GitHub 授权失败：{result.Error}";
                        return;
                    }
                }

                status.Foreground = Brush.Parse("#FCA5A5");
                status.Text = "GitHub 授权超时，请重新点击快速授权。";
            }
            catch (Exception ex)
            {
                status.Foreground = Brush.Parse("#FCA5A5");
                status.Text = $"快速授权不可用：{ex.Message}";
            }
            finally
            {
                oauthButton.IsEnabled = true;
            }
        });
        oauthButton.IsEnabled = authState.OAuthConfigured;
        var clearSavedButton = CreateHeaderButton("清除本地授权", async (_, _) =>
        {
            await _storeService.ClearSavedTokenAsync();
            tokenBox.IsEnabled = true;
            tokenBox.Text = string.Empty;
            submit.Content = "提交";
            status.Text = "已清除本地授权。请快速授权或手动粘贴 Token。";
        });
        clearSavedButton.IsEnabled = authState.HasSavedToken;
        var cancel = new Button
        {
            Content = "取消",
            MinHeight = 38,
            MinWidth = 88
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        footer.Children.Add(clearSavedButton);
        footer.Children.Add(cancel);
        footer.Children.Add(submit);

        var stack = new StackPanel
        {
            Margin = new Avalonia.Thickness(18),
            Spacing = 14
        };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        stack.Children.Add(help);
        stack.Children.Add(new TextBlock
        {
            Text = authState.OAuthConfigured
                ? "小白步骤：1. 点 GitHub 快速授权；2. 在浏览器输入验证码；3. 回到这里点提交；4. 如果生成 PR，等管理员合并。"
                : "未配置 OAuth ClientId，无法快速授权。请手动粘贴有 Contents 写权限的 GitHub Token，然后点击提交。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#334155")
        });
        var authActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        authActions.Children.Add(oauthButton);
        authActions.Children.Add(saveTokenCheck);
        stack.Children.Add(authActions);
        stack.Children.Add(tokenBox);
        stack.Children.Add(footer);

        var dialog = new Window
        {
            Title = title,
            Width = 640,
            Height = 430,
            MinWidth = 560,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = stack,
            RequestedThemeVariant = ThemeVariant.Dark,
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020")
        };

        var completion = new TaskCompletionSource<FrontendPackageStoreUploadOptions?>();
        submit.Click += async (_, _) =>
        {
            if (authState.HasSavedToken && string.IsNullOrWhiteSpace(tokenBox.Text))
            {
                completion.TrySetResult(new FrontendPackageStoreUploadOptions(null, true));
                dialog.Close();
                return;
            }

            if (string.IsNullOrWhiteSpace(tokenBox.Text))
            {
                status.Text = "请先粘贴 GitHub Token。";
                status.Foreground = Brush.Parse("#FCA5A5");
                return;
            }

            var token = tokenBox.Text.Trim();
            if (saveTokenCheck.IsChecked == true)
            {
                await _storeService.SaveTokenAsync(token);
            }

            completion.TrySetResult(new FrontendPackageStoreUploadOptions(token, true));
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            completion.TrySetResult(null);
            dialog.Close();
        };
        dialog.Closed += (_, _) => completion.TrySetResult(null);

        await dialog.ShowDialog(this);
        return await completion.Task;
    }

    private static string BuildUploadResultText(FrontendPackageStoreUploadResult result)
    {
        if (result.SubmittedPullRequest)
        {
            return string.IsNullOrWhiteSpace(result.PullRequestUrl)
                ? $"已提交 PR：{result.FileName}，等待审核合并后同步到商店"
                : $"已提交 PR：{result.FileName}，等待审核合并后同步到商店。{result.PullRequestUrl}";
        }

        return $"{(result.Updated ? "已更新" : "已上传")} {result.FileName}";
    }

    private sealed class StorePackageItem(FrontendPackageStoreItem source)
    {
        public string FileName { get; } = source.FileName;
        public string DisplayName { get; } = string.IsNullOrWhiteSpace(source.Name) ? source.FileName : source.Name;
        public string HtmlUrl { get; } = source.HtmlUrl;
        public string SizeText { get; } = FormatBytes(source.SizeBytes);
    }

    private static IBrush ResourceBrush(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, ThemeVariant.Dark, out var value) == true &&
            value is IBrush brush)
        {
            return brush;
        }

        return Brush.Parse(fallback);
    }
}

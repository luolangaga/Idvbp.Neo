using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Idvbp.Neo.Server.Services;

namespace Idvbp.Neo.Views;

public sealed class FrontendPackageStoreWindow : Window
{
    private readonly IFrontendPackageStoreService _storeService;
    private readonly IFrontendPackageService _frontendPackageService;
    private readonly ObservableCollection<StorePackageItem> _storeItems = [];
    private readonly ObservableCollection<FrontendPackageInfo> _localPackages = [];
    private static readonly HttpClient ImageHttpClient = new();
    private readonly ListBox _storeList = new();
    private readonly ComboBox _localPackageSelector = new();
    private readonly TextBlock _statusText = new();
    private readonly TextBlock _titleText = new();
    private readonly TextBlock _subtitleText = new();
    private readonly TextBlock _descriptionText = new();
    private readonly TextBlock _authorText = new();
    private readonly TextBlock _metaText = new();
    private readonly TextBlock _statsText = new();
    private readonly Image _screenshotImage = new();
    private readonly TextBlock _screenshotPlaceholder = new();
    private readonly Button _websiteButton = new();
    private readonly Button _contactButton = new();
    private readonly ItemsControl _pagesList = new();
    private readonly Button _installButton = new();
    private readonly Button _githubButton = new();
    private readonly Border _loadingOverlay = new();
    private readonly TextBlock _loadingTitle = new();
    private readonly TextBlock _loadingSubtitle = new();
    private string _selectedPackageId = "";
    private string _selectedHtmlUrl = "";
    private string _selectedWebsite = "";
    private string _selectedContact = "";
    private int _detailsRequestVersion;

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
        var host = new Grid();
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
        host.Children.Add(root);
        host.Children.Add(BuildLoadingOverlay());
        return host;
    }

    private Control BuildLoadingOverlay()
    {
        _loadingOverlay.IsVisible = false;
        _loadingOverlay.Background = Brush.Parse("#E61B1B1B");
        _loadingOverlay.Child = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new Border
                {
                    Width = 420,
                    Padding = new Avalonia.Thickness(26),
                    CornerRadius = new Avalonia.CornerRadius(10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", "#2B2B2B"),
                    BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
                    BorderThickness = new Avalonia.Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 16,
                        Children =
                        {
                            new ProgressBar
                            {
                                IsIndeterminate = true,
                                Height = 4,
                                Foreground = ResourceBrush("AccentFillColorDefaultBrush", "#4CC2FF")
                            },
                            _loadingTitle,
                            _loadingSubtitle
                        }
                    }
                }
            }
        };
        _loadingTitle.Text = "正在加载页面包商店";
        _loadingTitle.FontSize = 22;
        _loadingTitle.FontWeight = FontWeight.SemiBold;
        _loadingTitle.TextAlignment = TextAlignment.Center;
        _loadingTitle.Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3");
        _loadingSubtitle.Text = "正在从 GitHub 读取 packages 目录和 store.json";
        _loadingSubtitle.TextAlignment = TextAlignment.Center;
        _loadingSubtitle.TextWrapping = TextWrapping.Wrap;
        _loadingSubtitle.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7");
        return _loadingOverlay;
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

    private Control BuildScreenshotCard()
    {
        _screenshotImage.Stretch = Stretch.UniformToFill;
        _screenshotImage.IsVisible = false;
        _screenshotPlaceholder.Text = "暂无截图";
        _screenshotPlaceholder.HorizontalAlignment = HorizontalAlignment.Center;
        _screenshotPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        _screenshotPlaceholder.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7");
        _screenshotPlaceholder.FontSize = 16;

        return new Border
        {
            Height = 180,
            CornerRadius = new Avalonia.CornerRadius(10),
            ClipToBounds = true,
            Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = new Grid
            {
                Children =
                {
                    _screenshotImage,
                    _screenshotPlaceholder
                }
            }
        };
    }

    private static void OpenExternal(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private void ShowLoading(string title, string subtitle)
    {
        _loadingTitle.Text = title;
        _loadingSubtitle.Text = subtitle;
        _loadingOverlay.IsVisible = true;
    }

    private void HideLoading()
    {
        _loadingOverlay.IsVisible = false;
    }

    private Control BuildDetailsPanel()
    {
        var panel = CreateCard();
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"),
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

        var overview = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*"),
            ColumnSpacing = 16
        };
        overview.Children.Add(BuildScreenshotCard());
        var overviewText = new Border
        {
            Padding = new Avalonia.Thickness(16),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
            BorderThickness = new Avalonia.Thickness(1)
        };
        var overviewStack = new StackPanel { Spacing = 12 };
        _descriptionText.Text = "选择资源后会从 store.json 读取简介、作者、截图、网站和联系方式。";
        _descriptionText.TextWrapping = TextWrapping.Wrap;
        _descriptionText.FontSize = 15;
        _descriptionText.Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3");
        _authorText.Text = "作者信息会显示在这里";
        _authorText.TextWrapping = TextWrapping.Wrap;
        _authorText.Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7");
        overviewStack.Children.Add(_descriptionText);
        overviewStack.Children.Add(_authorText);
        overviewText.Child = overviewStack;
        Grid.SetColumn(overviewText, 1);
        overview.Children.Add(overviewText);
        Grid.SetRow(overview, 1);
        grid.Children.Add(overview);

        var metaBand = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };
        metaBand.Children.Add(CreateInfoTile("文件与入口", _metaText));
        var statsTile = CreateInfoTile("结构统计", _statsText);
        Grid.SetColumn(statsTile, 1);
        metaBand.Children.Add(statsTile);
        Grid.SetRow(metaBand, 2);
        grid.Children.Add(metaBand);

        grid.Children.Add(new TextBlock
        {
            Text = "页面信息",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        Grid.SetRow(grid.Children[^1], 3);

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
        Grid.SetRow(pagesScroll, 4);
        grid.Children.Add(pagesScroll);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        _websiteButton.Content = "打开网站";
        _websiteButton.IsEnabled = false;
        _websiteButton.Click += (_, _) => OpenExternal(_selectedWebsite);
        _contactButton.Content = "复制联系方式";
        _contactButton.IsEnabled = false;
        _contactButton.Click += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_selectedContact) && Clipboard is not null)
            {
                await Clipboard.SetTextAsync(_selectedContact);
                _statusText.Text = "已复制作者联系方式";
            }
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
        footer.Children.Add(_websiteButton);
        footer.Children.Add(_contactButton);
        footer.Children.Add(_githubButton);
        Grid.SetRow(footer, 5);
        grid.Children.Add(footer);

        panel.Child = grid;
        return panel;
    }

    private async Task ReloadAsync()
    {
        ShowLoading("正在加载页面包商店", "正在从 GitHub 读取 packages 目录和 store.json");
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
            HideLoading();
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
        finally
        {
            HideLoading();
        }
    }

    private async Task SelectCurrentStoreItemAsync()
    {
        if (_storeList.SelectedItem is not StorePackageItem item)
        {
            return;
        }

        _selectedPackageId = item.PackageId;
        _selectedHtmlUrl = item.HtmlUrl;
        _selectedWebsite = item.Website;
        _selectedContact = item.Contact;
        _titleText.Text = item.DisplayName;
        _subtitleText.Text = item.PackageId;
        _descriptionText.Text = string.IsNullOrWhiteSpace(item.Description) ? "这个页面包暂时没有简介。" : item.Description;
        _authorText.Text = BuildAuthorText(item.AuthorName, item.Contact);
        _metaText.Text = item.FileName;
        _statsText.Text = $"大小：{item.SizeText}\n结构信息后台解析中";
        _pagesList.ItemsSource = Array.Empty<FrontendPackageStorePageDetails>();
        _installButton.IsEnabled = true;
        _githubButton.IsEnabled = !string.IsNullOrWhiteSpace(item.HtmlUrl);
        _websiteButton.IsEnabled = !string.IsNullOrWhiteSpace(item.Website);
        _contactButton.IsEnabled = !string.IsNullOrWhiteSpace(item.Contact);
        _ = LoadScreenshotAsync(item.ScreenshotUrl);
        _statusText.Text = $"已读取 {item.DisplayName} 的商店配置，正在后台解析 ZIP 结构";
        var requestVersion = ++_detailsRequestVersion;

        try
        {
            var details = await _storeService.GetPackageDetailsAsync(item.PackageId);
            if (requestVersion != _detailsRequestVersion || _selectedPackageId != item.PackageId)
            {
                return;
            }

            _titleText.Text = details.Name;
            _subtitleText.Text = string.IsNullOrWhiteSpace(details.Version)
                ? $"{details.StorePackageId} · {details.Type}"
                : $"{details.StorePackageId} · v{details.Version} · {details.Type}";
            _descriptionText.Text = string.IsNullOrWhiteSpace(details.Description) ? "这个页面包暂时没有简介。" : details.Description;
            _authorText.Text = BuildAuthorText(details.AuthorName, details.Contact);
            _selectedWebsite = details.Website;
            _selectedContact = details.Contact;
            _websiteButton.IsEnabled = !string.IsNullOrWhiteSpace(details.Website);
            _contactButton.IsEnabled = !string.IsNullOrWhiteSpace(details.Contact);
            await LoadScreenshotAsync(details.ScreenshotUrl);
            _metaText.Text = $"包 ID：{details.StorePackageId}\n页面 ID：{details.PageId}\n文件：{details.FileName}\n入口：{details.EntryLayout}\n大小：{FormatBytes(details.SizeBytes)}";
            _statsText.Text = $"页面：{details.Pages.Count}\n组件文件：{details.ComponentFileCount}\n资源文件：{details.AssetFileCount}";
            _pagesList.ItemsSource = details.Pages;
            _installButton.IsEnabled = true;
            _statusText.Text = $"已解析 {details.Name} 的页面结构";
        }
        catch (Exception ex)
        {
            if (requestVersion == _detailsRequestVersion && _selectedPackageId == item.PackageId)
            {
                _statsText.Text = $"大小：{item.SizeText}\n结构解析失败：{ex.Message}";
            }

            _installButton.IsEnabled = true;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedPackageId))
        {
            return;
        }

        try
        {
            _installButton.IsEnabled = false;
            var packageId = _selectedPackageId;
            _statusText.Text = $"正在下载 {packageId}...";
            var package = await RunDownloadWithProgressAsync(
                "下载安装页面包",
                packageId,
                progress => _storeService.InstallPackageAsync(packageId, progress));
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

    private async Task LoadScreenshotAsync(string url)
    {
        _screenshotImage.IsVisible = false;
        _screenshotImage.Source = null;
        _screenshotPlaceholder.IsVisible = true;
        _screenshotPlaceholder.Text = string.IsNullOrWhiteSpace(url) ? "暂无截图" : "正在加载截图...";
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var bytes = await ImageHttpClient.GetByteArrayAsync(url);
            _screenshotImage.Source = new Bitmap(new MemoryStream(bytes));
            _screenshotImage.IsVisible = true;
            _screenshotPlaceholder.IsVisible = false;
        }
        catch
        {
            _screenshotPlaceholder.Text = "截图加载失败";
        }
    }

    private static string BuildAuthorText(string authorName, string contact)
    {
        var author = string.IsNullOrWhiteSpace(authorName) ? "未填写作者" : $"作者：{authorName}";
        return string.IsNullOrWhiteSpace(contact) ? author : $"{author}\n联系方式：{contact}";
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
            var preview = ReadZipPreview(path);
            if (!await PromptZipPreviewAsync(preview))
            {
                _statusText.Text = "已取消上传";
                return;
            }

            var metadata = await PromptMetadataAsync(new FrontendPackageStoreMetadata(
                CreateSafeId(preview.ManifestId),
                preview.FirstPageId,
                preview.ManifestName,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty));
            if (metadata is null)
            {
                _statusText.Text = "已取消上传";
                return;
            }

            var options = await PromptUploadOptionsAsync("上传 ZIP 到页面包商店", metadata);
            if (options is null)
            {
                _statusText.Text = "已取消上传";
                return;
            }

            var result = await RunUploadWithProgressAsync(
                "上传页面包",
                preview.FileName,
                preview.SizeBytes,
                () => _storeService.UploadFileAsync(path, options));
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
            var metadata = await PromptMetadataAsync(new FrontendPackageStoreMetadata(
                CreateSafeId(package.Id),
                package.Pages.FirstOrDefault()?.Id ?? string.Empty,
                package.Name,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty));
            if (metadata is null)
            {
                _statusText.Text = "已取消发布";
                return;
            }

            var options = await PromptUploadOptionsAsync($"发布 {package.Name}", metadata);
            if (options is null)
            {
                _statusText.Text = "已取消发布";
                return;
            }

            var result = await RunUploadWithProgressAsync(
                $"发布 {package.Name}",
                package.Id,
                null,
                () => _storeService.UploadLocalPackageAsync(package.Id, options));
            _statusText.Text = BuildUploadResultText(result);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"发布失败：{ex.Message}";
        }
    }

    private static ZipPreviewInfo ReadZipPreview(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ZipPreviewEntry(entry.FullName.Replace('\\', '/'), entry.Length))
            .ToArray();

        var manifestId = Path.GetFileNameWithoutExtension(path);
        var manifestName = manifestId;
        var firstPageId = string.Empty;
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is not null)
        {
            using var stream = manifestEntry.Open();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                manifestId = id.GetString() ?? manifestId;
            }

            if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                manifestName = name.GetString() ?? manifestName;
            }

            if (root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array)
            {
                var first = pages.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("id", out var pageId) &&
                    pageId.ValueKind == JsonValueKind.String)
                {
                    firstPageId = pageId.GetString() ?? string.Empty;
                }
            }
        }

        return new ZipPreviewInfo(
            Path.GetFileName(path),
            new FileInfo(path).Length,
            manifestId,
            manifestName,
            firstPageId,
            entries);
    }

    private async Task<bool> PromptZipPreviewAsync(ZipPreviewInfo preview)
    {
        var list = new ListBox
        {
            ItemsSource = preview.Entries.Take(160).ToArray(),
            Height = 320,
            ItemTemplate = new FuncDataTemplate<ZipPreviewEntry>((entry, _) =>
            {
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    ColumnSpacing = 12,
                    Margin = new Avalonia.Thickness(0, 0, 0, 6)
                };
                row.Children.Add(new TextBlock
                {
                    Text = entry?.Path ?? string.Empty,
                    Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var size = new TextBlock
                {
                    Text = entry is null ? string.Empty : FormatBytes(entry.SizeBytes),
                    Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7"),
                    FontSize = 12
                };
                Grid.SetColumn(size, 1);
                row.Children.Add(size);
                return row;
            })
        };

        var summary = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10
        };
        summary.Children.Add(CreatePreviewStat("文件", preview.FileName));
        var sizeTile = CreatePreviewStat("大小", FormatBytes(preview.SizeBytes));
        Grid.SetColumn(sizeTile, 1);
        summary.Children.Add(sizeTile);
        var manifestTile = CreatePreviewStat("Manifest", preview.ManifestId);
        Grid.SetColumn(manifestTile, 2);
        summary.Children.Add(manifestTile);

        var submit = CreatePrimaryButton("继续填写信息", (_, _) => { });
        var cancel = new Button { Content = "取消", MinWidth = 88, MinHeight = 38 };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        footer.Children.Add(cancel);
        footer.Children.Add(submit);

        var stack = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14 };
        stack.Children.Add(new TextBlock
        {
            Text = "预览页面包文件",
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"将从 ZIP 读取 manifest 作为默认信息。共 {preview.Entries.Count} 个文件，列表最多显示前 160 个。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
        });
        stack.Children.Add(summary);
        stack.Children.Add(list);
        stack.Children.Add(footer);

        var dialog = new Window
        {
            Title = "预览页面包文件",
            Width = 780,
            Height = 600,
            MinWidth = 680,
            MinHeight = 520,
            RequestedThemeVariant = ThemeVariant.Dark,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020"),
            Content = stack
        };
        var completion = new TaskCompletionSource<bool>();
        submit.Click += (_, _) =>
        {
            completion.TrySetResult(true);
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            completion.TrySetResult(false);
            dialog.Close();
        };
        dialog.Closed += (_, _) => completion.TrySetResult(false);
        await dialog.ShowDialog(this);
        return await completion.Task;
    }

    private async Task<T> RunUploadWithProgressAsync<T>(
        string title,
        string target,
        long? totalBytes,
        Func<Task<T>> action)
    {
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 8,
            Height = 8,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", "#4CC2FF")
        };
        var status = new TextBlock
        {
            Text = "正在准备提交到 GitHub...",
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
            TextWrapping = TextWrapping.Wrap
        };
        var speed = new TextBlock
        {
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
        };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 16 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        stack.Children.Add(new TextBlock
        {
            Text = target,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
        });
        stack.Children.Add(progress);
        stack.Children.Add(status);
        stack.Children.Add(speed);

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 260,
            MinWidth = 480,
            MinHeight = 240,
            RequestedThemeVariant = ThemeVariant.Dark,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020"),
            Content = stack,
            CanResize = false
        };
        var completion = new TaskCompletionSource<T>();
        var startedAt = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        timer.Tick += (_, _) =>
        {
            var elapsed = Math.Max(0.2, (DateTimeOffset.UtcNow - startedAt).TotalSeconds);
            progress.Value = Math.Min(94, progress.Value + 1.7);
            var speedText = totalBytes is > 0
                ? $"估算速度：{FormatBytes((long)(totalBytes.Value / elapsed))}/s · 已用时：{elapsed:0.0}s"
                : $"正在打包并上传 · 已用时：{elapsed:0.0}s";
            speed.Text = speedText;
            status.Text = progress.Value < 35
                ? "正在准备 ZIP 和 store.json..."
                : progress.Value < 75
                    ? "正在调用 GitHub Contents API 上传..."
                    : "正在等待 GitHub 返回提交结果...";
        };
        dialog.Opened += async (_, _) =>
        {
            timer.Start();
            try
            {
                var result = await action();
                progress.Value = 100;
                status.Text = "上传完成";
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                timer.Stop();
                await Task.Delay(240);
                dialog.Close();
            }
        };
        await dialog.ShowDialog(this);
        return await completion.Task;
    }

    private async Task<T> RunDownloadWithProgressAsync<T>(
        string title,
        string target,
        Func<IProgress<FrontendPackageTransferProgress>, Task<T>> action)
    {
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 8,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", "#4CC2FF")
        };
        var status = new TextBlock
        {
            Text = "正在连接下载地址...",
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
            TextWrapping = TextWrapping.Wrap
        };
        var speed = new TextBlock
        {
            Text = "等待 GitHub 返回文件大小",
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
        };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 16 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3")
        });
        stack.Children.Add(new TextBlock
        {
            Text = target,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
        });
        stack.Children.Add(progressBar);
        stack.Children.Add(status);
        stack.Children.Add(speed);

        var dialog = new Window
        {
            Title = title,
            Width = 540,
            Height = 260,
            MinWidth = 500,
            MinHeight = 240,
            RequestedThemeVariant = ThemeVariant.Dark,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020"),
            Content = stack,
            CanResize = false
        };
        var completion = new TaskCompletionSource<T>();
        var startedAt = DateTimeOffset.UtcNow;
        var reporter = new Progress<FrontendPackageTransferProgress>(value =>
        {
            var elapsed = Math.Max(0.2, (DateTimeOffset.UtcNow - startedAt).TotalSeconds);
            var total = value.TotalBytes;
            if (total is > 0)
            {
                progressBar.IsIndeterminate = false;
                progressBar.Value = Math.Clamp(value.BytesReceived * 100d / total.Value, 0, 100);
                speed.Text = $"{FormatBytes(value.BytesReceived)} / {FormatBytes(total.Value)} · {FormatBytes((long)(value.BytesReceived / elapsed))}/s · {elapsed:0.0}s";
            }
            else
            {
                progressBar.IsIndeterminate = true;
                speed.Text = $"{FormatBytes(value.BytesReceived)} · {FormatBytes((long)(value.BytesReceived / elapsed))}/s · {elapsed:0.0}s";
            }

            status.Text = value.Stage;
        });

        dialog.Opened += async (_, _) =>
        {
            try
            {
                var result = await action(reporter);
                progressBar.IsIndeterminate = false;
                progressBar.Value = 100;
                status.Text = "下载和安装完成";
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                await Task.Delay(260);
                dialog.Close();
            }
        };
        await dialog.ShowDialog(this);
        return await completion.Task;
    }

    private static Border CreatePreviewStat(string title, string value)
    {
        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", "#F3F3F3"),
            TextWrapping = TextWrapping.Wrap
        });
        return new Border
        {
            Padding = new Avalonia.Thickness(12),
            CornerRadius = new Avalonia.CornerRadius(8),
            Background = ResourceBrush("LayerFillColorAltBrush", "#242424"),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", "#3A3A3A"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = stack
        };
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

    private async Task<FrontendPackageStoreMetadata?> PromptMetadataAsync(FrontendPackageStoreMetadata defaults)
    {
        var packageId = new TextBox { Text = defaults.PackageId, Watermark = "包 ID，例如 asg-director-bp" };
        var pageId = new TextBox { Text = defaults.PageId, Watermark = "默认页面 ID，例如 default / bp" };
        var name = new TextBox { Text = defaults.Name, Watermark = "商店展示名称" };
        var description = new TextBox { Text = defaults.Description, Watermark = "简介：这个页面包适合什么场景", AcceptsReturn = true, MinHeight = 84, TextWrapping = TextWrapping.Wrap };
        var author = new TextBox { Text = defaults.AuthorName, Watermark = "作者名字" };
        var screenshot = new TextBox { Text = defaults.ScreenshotUrl, Watermark = "页面截图 URL，可留空" };
        var website = new TextBox { Text = defaults.Website, Watermark = "网站 / 项目地址，可留空" };
        var contact = new TextBox { Text = defaults.Contact, Watermark = "作者联系方式，可留空" };

        var form = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 10
        };
        AddFormRow(form, 0, "包 ID", packageId);
        AddFormRow(form, 1, "页面 ID", pageId);
        AddFormRow(form, 2, "名称", name);
        AddFormRow(form, 3, "简介", description);
        AddFormRow(form, 4, "作者", author);
        AddFormRow(form, 5, "截图 URL", screenshot);
        AddFormRow(form, 6, "网站", website);
        AddFormRow(form, 7, "联系方式", contact);

        var status = new TextBlock
        {
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7"),
            Text = "这些信息会写入 store.json，并和 ZIP 一起提交到 packages/{packageId}/。"
        };
        var submit = CreatePrimaryButton("下一步", (_, _) => { });
        var cancel = new Button { Content = "取消", MinWidth = 88, MinHeight = 38 };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        footer.Children.Add(cancel);
        footer.Children.Add(submit);
        var stack = new StackPanel { Margin = new Avalonia.Thickness(18), Spacing = 14 };
        stack.Children.Add(new TextBlock { Text = "填写商店信息", FontSize = 22, FontWeight = FontWeight.SemiBold });
        stack.Children.Add(status);
        stack.Children.Add(form);
        stack.Children.Add(footer);

        var dialog = new Window
        {
            Title = "填写商店信息",
            Width = 720,
            Height = 620,
            MinWidth = 640,
            MinHeight = 560,
            RequestedThemeVariant = ThemeVariant.Dark,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = ResourceBrush("ApplicationPageBackgroundThemeBrush", "#202020"),
            Content = stack
        };
        var completion = new TaskCompletionSource<FrontendPackageStoreMetadata?>();
        submit.Click += (_, _) =>
        {
            var id = CreateSafeId(packageId.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name.Text))
            {
                status.Foreground = Brush.Parse("#FCA5A5");
                status.Text = "包 ID 和名称必填。";
                return;
            }

            completion.TrySetResult(new FrontendPackageStoreMetadata(
                id,
                pageId.Text?.Trim() ?? string.Empty,
                name.Text.Trim(),
                description.Text?.Trim() ?? string.Empty,
                author.Text?.Trim() ?? string.Empty,
                screenshot.Text?.Trim() ?? string.Empty,
                website.Text?.Trim() ?? string.Empty,
                contact.Text?.Trim() ?? string.Empty));
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

    private static void AddFormRow(Grid form, int row, string label, Control editor)
    {
        var container = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("112,*"),
            ColumnSpacing = 12
        };
        container.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7"),
            FontWeight = FontWeight.SemiBold
        });
        Grid.SetColumn(editor, 1);
        container.Children.Add(editor);
        Grid.SetRow(container, row);
        form.Children.Add(container);
    }

    private static string CreateSafeId(string value)
    {
        var id = string.Concat((value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'))
            .Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(id) ? "package" : id;
    }

    private async Task<FrontendPackageStoreUploadOptions?> PromptUploadOptionsAsync(string title, FrontendPackageStoreMetadata metadata)
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
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", "#C7C7C7")
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
                completion.TrySetResult(new FrontendPackageStoreUploadOptions(null, true, metadata));
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

            completion.TrySetResult(new FrontendPackageStoreUploadOptions(token, true, metadata));
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
        public string PackageId { get; } = source.PackageId;
        public string FileName { get; } = source.FileName;
        public string DisplayName { get; } = string.IsNullOrWhiteSpace(source.Name) ? source.FileName : source.Name;
        public string Description { get; } = source.Description;
        public string AuthorName { get; } = source.AuthorName;
        public string ScreenshotUrl { get; } = source.ScreenshotUrl;
        public string Website { get; } = source.Website;
        public string Contact { get; } = source.Contact;
        public string HtmlUrl { get; } = source.HtmlUrl;
        public string SizeText { get; } = FormatBytes(source.SizeBytes);
    }

    private sealed record ZipPreviewInfo(
        string FileName,
        long SizeBytes,
        string ManifestId,
        string ManifestName,
        string FirstPageId,
        IReadOnlyCollection<ZipPreviewEntry> Entries);

    private sealed record ZipPreviewEntry(string Path, long SizeBytes);

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

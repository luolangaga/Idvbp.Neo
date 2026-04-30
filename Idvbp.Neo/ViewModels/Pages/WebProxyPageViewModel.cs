using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Server.Middleware;
using Idvbp.Neo.Server.Services;
using Microsoft.Extensions.Configuration;

namespace Idvbp.Neo.ViewModels.Pages;

public partial class ProxyRouteItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _sourceType = "";

    [ObservableProperty]
    private string _publicUrl = "";

    [ObservableProperty]
    private string _copyStatus = "";

    [ObservableProperty]
    private string _pageConfig = "";

    public string ConfigFormat => ProxyPageConfigTextHelper.DetectFormat(PageConfig);

    public string PageConfigSummary => ProxyPageConfigTextHelper.BuildSummary(PageConfig);

    public static ProxyRouteItemViewModel FromRoute(ReverseProxyRoute route, string serverUrl)
    {
        var baseUrl = serverUrl.TrimEnd('/');
        var prefix = route.PathPrefix.StartsWith('/') ? route.PathPrefix : "/" + route.PathPrefix;

        return new ProxyRouteItemViewModel
        {
            Id = route.Id,
            Enabled = route.Enabled,
            Name = string.IsNullOrWhiteSpace(route.Name) ? prefix : route.Name,
            SourceType = string.IsNullOrWhiteSpace(route.StaticRoot) ? "HTTP 反代" : "静态页面",
            PublicUrl = baseUrl + prefix
        };
    }

    partial void OnPageConfigChanged(string value)
    {
        OnPropertyChanged(nameof(PageConfigSummary));
        OnPropertyChanged(nameof(ConfigFormat));
    }

    [RelayCommand]
    private async Task CopyUrlAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard == null)
        {
            CopyStatus = "复制失败";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(PublicUrl);
        CopyStatus = "已复制";
    }
}

public partial class WebProxyPageViewModel : ObservableObject
{
    private readonly string _serverUrl;
    private readonly IProxyPageConfigRepository _pageConfigRepository;
    private readonly IFrontendPackageService _frontendPackageService;

    [ObservableProperty]
    private string _configPath = "";

    [ObservableProperty]
    private string _status = "";

    public ObservableCollection<ProxyRouteItemViewModel> Routes { get; } = [];
    public ObservableCollection<FrontendPackageItemViewModel> FrontendPackages { get; } = [];

    public string RouteCountText => Routes.Count == 0 ? "暂无代理" : $"{Routes.Count} 个代理";
    public string FrontendPackageCountText => FrontendPackages.Count == 0 ? "暂无前台包" : $"{FrontendPackages.Count} 个前台包";

    public WebProxyPageViewModel(
        IConfiguration configuration,
        IProxyPageConfigRepository pageConfigRepository,
        IFrontendPackageService frontendPackageService)
    {
        _pageConfigRepository = pageConfigRepository;
        _frontendPackageService = frontendPackageService;
        _serverUrl = FirstServerUrl(configuration["Server:Urls"] ?? "http://localhost:5000");
        ConfigPath = ReverseProxyConfigLoader.ResolveConfigPath();
        LoadConfig();
        LoadFrontendPackages();
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        LoadConfig();
        LoadFrontendPackages();
    }

    [RelayCommand]
    private void OpenConfigFile()
    {
        if (!File.Exists(ConfigPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task CopyFrontendUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard == null)
        {
            Status = "复制前台地址失败";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(url);
        Status = "已复制前台地址";
    }

    public async Task<bool> ImportFrontendPackageAsync(string filePath)
    {
        try
        {
            var package = await _frontendPackageService.ImportAsync(filePath);
            LoadFrontendPackages();
            Status = $"已导入前台包 {package.Id}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"导入前台包失败：{ex.Message}";
            return false;
        }
    }

    public async Task<bool> ExportFrontendPackageAsync(FrontendPackageItemViewModel package, string filePath)
    {
        try
        {
            await using var output = File.Create(filePath);
            await _frontendPackageService.WritePackageZipAsync(package.Id, output);
            Status = $"已导出前台包 {package.Id}";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"导出前台包失败：{ex.Message}";
            return false;
        }
    }

    public async Task<bool> UpdateFrontendPageConfigAsync(FrontendPageItemViewModel page, string pageConfig)
    {
        if (string.IsNullOrWhiteSpace(page.ConfigKey))
        {
            Status = "该前台页面没有配置 key，无法编辑配置";
            return false;
        }

        try
        {
            await Task.Run(() => _pageConfigRepository.Upsert(page.ConfigKey, pageConfig));
            page.PageConfig = pageConfig;
            Status = $"已更新 {page.Name} 的页面配置";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"保存失败：{ex.Message}";
            return false;
        }
    }

    public async Task<string> RefreshFrontendPageConfigAsync(FrontendPageItemViewModel page)
    {
        if (string.IsNullOrWhiteSpace(page.ConfigKey))
        {
            return page.PageConfig;
        }

        var latest = await Task.Run(() => _pageConfigRepository.GetValueOrDefault(page.ConfigKey));
        page.PageConfig = latest;
        return latest;
    }

    public IReadOnlyList<FrontendConfigTargetItemViewModel> GetFrontendPageConfigTargets(FrontendPageItemViewModel page)
    {
        var targets = new List<FrontendConfigTargetItemViewModel>
        {
            new()
            {
                Id = "",
                Name = "页面配置",
                Kind = "page",
                ConfigKey = page.ConfigKey,
                Config = _pageConfigRepository.GetValueOrDefault(page.ConfigKey)
            }
        };

        var package = _frontendPackageService.GetPackage(page.PackageId);
        if (package is null)
        {
            return targets;
        }

        var layoutPath = Path.GetFullPath(Path.Combine(
            package.PhysicalPath,
            page.Layout.Replace('/', Path.DirectorySeparatorChar)));
        var packagePath = Path.GetFullPath(package.PhysicalPath);
        if (!layoutPath.StartsWith(packagePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(layoutPath))
        {
            return targets;
        }

        var configs = _pageConfigRepository.GetAll();
        foreach (var node in ReadLayoutNodes(layoutPath))
        {
            var configKey = BuildFrontendComponentConfigKey(page.PackageId, page.Id, node.Id);
            targets.Add(new FrontendConfigTargetItemViewModel
            {
                Id = node.Id,
                Name = $"{node.Id} ({node.Type})",
                Kind = "component",
                ConfigKey = configKey,
                Config = configs.TryGetValue(configKey, out var config) ? config : string.Empty
            });
        }

        return targets;
    }

    public async Task<string> RefreshFrontendConfigTargetAsync(FrontendConfigTargetItemViewModel target)
    {
        if (string.IsNullOrWhiteSpace(target.ConfigKey))
        {
            return target.Config;
        }

        var latest = await Task.Run(() => _pageConfigRepository.GetValueOrDefault(target.ConfigKey));
        target.Config = latest;
        return latest;
    }

    public async Task<bool> UpdateFrontendConfigTargetAsync(FrontendPageItemViewModel page, FrontendConfigTargetItemViewModel target, string config)
    {
        if (string.IsNullOrWhiteSpace(target.ConfigKey))
        {
            Status = "该配置没有 key，无法保存";
            return false;
        }

        try
        {
            await Task.Run(() => _pageConfigRepository.Upsert(target.ConfigKey, config));
            target.Config = config;
            if (target.Kind == "page")
            {
                page.PageConfig = config;
            }
            Status = $"已更新 {target.Name} 配置";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"保存失败：{ex.Message}";
            return false;
        }
    }

    public async Task<bool> UpdateRoutePageConfigAsync(ProxyRouteItemViewModel route, string pageConfig)
    {
        if (string.IsNullOrWhiteSpace(route.Id))
        {
            Status = "该代理未配置 id，无法编辑页面配置";
            return false;
        }

        try
        {
            await Task.Run(() => _pageConfigRepository.Upsert(route.Id, pageConfig));
            route.PageConfig = pageConfig;
            Status = $"已更新 {route.Id} 的页面配置";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"保存失败：{ex.Message}";
            return false;
        }
    }

    public async Task<string> RefreshRoutePageConfigAsync(ProxyRouteItemViewModel route)
    {
        if (string.IsNullOrWhiteSpace(route.Id))
        {
            return route.PageConfig;
        }

        var latest = await Task.Run(() => _pageConfigRepository.GetValueOrDefault(route.Id));
        route.PageConfig = latest;
        return latest;
    }

    private void LoadConfig()
    {
        Routes.Clear();

        if (!File.Exists(ConfigPath))
        {
            Status = "未找到 proxies.json";
            OnPropertyChanged(nameof(RouteCountText));
            return;
        }

        try
        {
            var config = ReverseProxyConfigLoader.Load(ConfigPath);
            var pageConfigs = _pageConfigRepository.GetAll();
            if (!config.Enabled)
            {
                Status = "代理配置已禁用";
                OnPropertyChanged(nameof(RouteCountText));
                return;
            }

            foreach (var route in config.Routes)
            {
                var item = ProxyRouteItemViewModel.FromRoute(route, _serverUrl);
                item.PageConfig = pageConfigs.TryGetValue(item.Id, out var pageConfig) ? pageConfig : string.Empty;
                Routes.Add(item);
            }

            Status = "";
        }
        catch (Exception ex)
        {
            Status = $"配置读取失败：{ex.Message}";
        }

        OnPropertyChanged(nameof(RouteCountText));
    }

    private void LoadFrontendPackages()
    {
        FrontendPackages.Clear();
        var pageConfigs = _pageConfigRepository.GetAll();
        foreach (var package in _frontendPackageService.GetPackages())
        {
            FrontendPackages.Add(ToFrontendPackageItem(package, pageConfigs));
        }

        OnPropertyChanged(nameof(FrontendPackageCountText));
    }

    private FrontendPackageItemViewModel ToFrontendPackageItem(FrontendPackageInfo package, System.Collections.Generic.IReadOnlyDictionary<string, string> pageConfigs)
    {
        var launchUrl = BuildAbsoluteUrl(package.LaunchUrl);
        return new FrontendPackageItemViewModel
        {
            Id = package.Id,
            Name = package.Name,
            Version = package.Version,
            Type = package.Type,
            EntryLayout = package.EntryLayout,
            LaunchUrl = launchUrl,
            Pages = new ObservableCollection<FrontendPageItemViewModel>(package.Pages.Select(page =>
            {
                var configKey = BuildFrontendConfigKey(package.Id, page.Id);
                return new FrontendPageItemViewModel
                {
                    Id = page.Id,
                    PackageId = package.Id,
                    Name = page.Name,
                    Layout = page.Layout,
                    LaunchUrl = $"{launchUrl}&page={Uri.EscapeDataString(page.Id)}",
                    EditUrl = $"{launchUrl}&page={Uri.EscapeDataString(page.Id)}&edit=1",
                    ConfigKey = configKey,
                    PageConfig = pageConfigs.TryGetValue(configKey, out var config) ? config : string.Empty
                };
            }))
        };
    }

    private static string BuildFrontendConfigKey(string packageId, string pageId)
        => $"frontend:{packageId}:{pageId}";

    private static string BuildFrontendComponentConfigKey(string packageId, string pageId, string componentId)
        => $"{BuildFrontendConfigKey(packageId, pageId)}:component:{componentId}";

    private static IReadOnlyList<LayoutNodeSummary> ReadLayoutNodes(string layoutPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(layoutPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!document.RootElement.TryGetProperty("nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<LayoutNodeSummary>();
            VisitLayoutNodes(nodes, result);
            return result;
        }
        catch
        {
            return [];
        }
    }

    private static void VisitLayoutNodes(JsonElement nodes, List<LayoutNodeSummary> result)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            var id = node.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            var type = node.TryGetProperty("type", out var typeElement) ? typeElement.GetString() ?? "" : "";
            if (!string.IsNullOrWhiteSpace(id))
            {
                result.Add(new LayoutNodeSummary(id, string.IsNullOrWhiteSpace(type) ? "unknown" : type));
            }

            if (node.TryGetProperty("children", out var children) &&
                children.ValueKind == JsonValueKind.Array)
            {
                VisitLayoutNodes(children, result);
            }
        }
    }

    private string BuildAbsoluteUrl(string relativeUrl)
        => _serverUrl.TrimEnd('/') + (relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl);

    private static string FirstServerUrl(string urls)
    {
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return first.Replace("0.0.0.0", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("*", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("+", "localhost", StringComparison.OrdinalIgnoreCase);
    }
}

public partial class FrontendConfigTargetItemViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string ConfigKey { get; init; } = "";

    [ObservableProperty]
    private string _config = "";

    public override string ToString() => Name;
}

internal sealed record LayoutNodeSummary(string Id, string Type);

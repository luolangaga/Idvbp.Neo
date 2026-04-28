using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
            SourceType = string.IsNullOrWhiteSpace(route.StaticRoot) ? "HTTP反代" : "静态页面",
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

    [ObservableProperty]
    private string _configPath = "";

    [ObservableProperty]
    private string _status = "";

    public ObservableCollection<ProxyRouteItemViewModel> Routes { get; } = [];

    public string RouteCountText => Routes.Count == 0 ? "暂无代理" : $"{Routes.Count} 个代理";

    public WebProxyPageViewModel(IConfiguration configuration, IProxyPageConfigRepository pageConfigRepository)
    {
        _pageConfigRepository = pageConfigRepository;
        _serverUrl = FirstServerUrl(configuration["Server:Urls"] ?? "http://localhost:5000");
        ConfigPath = ReverseProxyConfigLoader.ResolveConfigPath();
        LoadConfig();
    }

    [RelayCommand]
    private void ReloadConfig()
    {
        LoadConfig();
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

    private static string FirstServerUrl(string urls)
    {
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return first.Replace("0.0.0.0", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("*", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("+", "localhost", StringComparison.OrdinalIgnoreCase);
    }
}

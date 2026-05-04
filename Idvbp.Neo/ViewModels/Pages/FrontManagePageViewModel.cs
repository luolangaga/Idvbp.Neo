using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Idvbp.Neo.Server.Services;
using Microsoft.Extensions.Configuration;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 前台页面项视图模型。
/// </summary>
public partial class FrontendPageItemViewModel : ObservableObject
{
    public string PackageId { get; init; } = "";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Layout { get; init; } = "";
    public string LaunchUrl { get; init; } = "";
    public string EditUrl { get; init; } = "";
    public string DesignerUrl { get; init; } = "";
    public string ConfigKey { get; init; } = "";
    public string ViewportConfigKey { get; init; } = "";

    [ObservableProperty]
    private string _pageConfig = "";

    [ObservableProperty]
    private int _viewportWidth = 1280;

    [ObservableProperty]
    private int _viewportHeight = 720;

    public string ConfigFormat => ProxyPageConfigTextHelper.DetectFormat(PageConfig);

    public string PageConfigSummary => ProxyPageConfigTextHelper.BuildSummary(PageConfig);

    public string ViewportSummary => $"Viewport: {ViewportWidth} x {ViewportHeight}";

    partial void OnPageConfigChanged(string value)
    {
        OnPropertyChanged(nameof(PageConfigSummary));
        OnPropertyChanged(nameof(ConfigFormat));
    }

    partial void OnViewportWidthChanged(int value)
    {
        OnPropertyChanged(nameof(ViewportSummary));
    }

    partial void OnViewportHeightChanged(int value)
    {
        OnPropertyChanged(nameof(ViewportSummary));
    }
}

/// <summary>
/// 前台包项视图模型。
/// </summary>
public partial class FrontendPackageItemViewModel : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Type { get; init; } = "";
    public string EntryLayout { get; init; } = "";
    public string LaunchUrl { get; init; } = "";
    public ObservableCollection<FrontendPageItemViewModel> Pages { get; init; } = [];
}

/// <summary>
/// 前台管理页面视图模型。
/// </summary>
public partial class FrontManagePageViewModel : ViewModelBase
{
    private readonly IFrontendPackageService _frontendPackageService;
    private readonly string _serverUrl;

    [ObservableProperty]
    private string _status = "";

    public ObservableCollection<FrontendPackageItemViewModel> Packages { get; } = [];

    /// <summary>
    /// 包数量文本。
    /// </summary>
    public string PackageCountText => Packages.Count == 0 ? "暂无前台包" : $"{Packages.Count} 个前台包";

    /// <summary>
    /// 初始化前台管理页面视图模型。
    /// </summary>
    public FrontManagePageViewModel(IFrontendPackageService frontendPackageService, IConfiguration configuration)
    {
        _frontendPackageService = frontendPackageService;
        _serverUrl = FirstServerUrl(configuration["Server:Urls"] ?? "http://localhost:5000");
        ReloadPackages();
    }

    /// <summary>
    /// 重新加载包命令。
    /// </summary>
    [RelayCommand]
    public void ReloadPackages()
    {
        Packages.Clear();
        foreach (var package in _frontendPackageService.GetPackages())
        {
            Packages.Add(ToViewModel(package));
        }

        Status = "";
        OnPropertyChanged(nameof(PackageCountText));
    }

    /// <summary>
    /// 复制 URL 命令。
    /// </summary>
    [RelayCommand]
    public async Task CopyUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow?.Clipboard == null)
        {
            Status = "复制失败。";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(url);
        Status = "已复制打开地址。";
    }

    /// <summary>
    /// 导入包。
    /// </summary>
    public async Task<bool> ImportPackageAsync(string filePath)
    {
        try
        {
            var package = await _frontendPackageService.ImportAsync(filePath);
            ReloadPackages();
            Status = $"已导入前台包 {package.Id}。";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"导入失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 转换为视图模型。
    /// </summary>
    private FrontendPackageItemViewModel ToViewModel(FrontendPackageInfo package)
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
            Pages = new ObservableCollection<FrontendPageItemViewModel>(package.Pages.Select(page => new FrontendPageItemViewModel
            {
                PackageId = package.Id,
                Id = page.Id,
                Name = page.Name,
                Layout = page.Layout,
                LaunchUrl = $"{launchUrl}&page={Uri.EscapeDataString(page.Id)}",
                EditUrl = $"{launchUrl}&page={Uri.EscapeDataString(page.Id)}&edit=1",
                DesignerUrl = $"{_serverUrl.TrimEnd('/')}/runtime/component-designer/index.html?frontend={Uri.EscapeDataString(package.Id)}&page={Uri.EscapeDataString(page.Id)}&layout={Uri.EscapeDataString(page.Layout)}"
            }))
        };
    }

    /// <summary>
    /// 构建绝对 URL。
    /// </summary>
    private string BuildAbsoluteUrl(string relativeUrl)
        => _serverUrl.TrimEnd('/') + (relativeUrl.StartsWith('/') ? relativeUrl : "/" + relativeUrl);

    /// <summary>
    /// 获取第一个服务器 URL。
    /// </summary>
    private static string FirstServerUrl(string urls)
    {
        var first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return first.Replace("0.0.0.0", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("*", "localhost", StringComparison.OrdinalIgnoreCase)
            .Replace("+", "localhost", StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

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

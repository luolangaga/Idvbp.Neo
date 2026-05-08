using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Idvbp.Neo.Services;

namespace Idvbp.Neo.Views;

public partial class ErrorReportWindow : Window
{
    private readonly ErrorReportViewModel _viewModel;

    public ErrorReportWindow()
        : this(new InvalidOperationException("未提供错误信息。"), "错误报告窗口")
    {
    }

    public ErrorReportWindow(Exception exception, string source)
    {
        InitializeComponent();
        _viewModel = new ErrorReportViewModel(exception, source, new ErrorReportService());
        DataContext = _viewModel;
    }

    private async void CopyError_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard == null)
        {
            _viewModel.StatusText = "复制失败：无法访问剪贴板。";
            return;
        }

        await Clipboard.SetTextAsync(_viewModel.ErrorText);
        _viewModel.StatusText = "错误信息已复制到剪贴板。";
    }

    private void PackageError_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.PackageError();
    }

    private void ReportIssue_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel.ReportIssue();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

public partial class ErrorReportViewModel : ObservableObject
{
    private readonly Exception _exception;
    private readonly string _source;
    private readonly ErrorReportService _reportService;

    public ErrorReportViewModel(Exception exception, string source, ErrorReportService reportService)
    {
        _exception = exception;
        _source = source;
        _reportService = reportService;
        ErrorTitle = $"{exception.GetType().Name}: {exception.Message}";
        ErrorSubtitle = $"来源：{source}    时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        ErrorText = reportService.BuildErrorText(exception, source);
    }

    public string ErrorTitle { get; }

    public string ErrorSubtitle { get; }

    public string ErrorText { get; }

    [ObservableProperty]
    private string _statusText = "建议先一键打包；提交 Issue 时把生成的 zip 文件拖入 GitHub 页面。";

    [ObservableProperty]
    private string _packagePath = "";

    public void PackageError()
    {
        PackagePath = _reportService.PackageError(_exception, _source);
        StatusText = $"错误包已生成：{PackagePath}";
    }

    public void ReportIssue()
    {
        if (string.IsNullOrWhiteSpace(PackagePath))
        {
            PackageError();
        }

        _reportService.OpenGitHubIssue(_exception, _source, PackagePath);
        StatusText = $"已打开 GitHub Issue 页面。错误包：{PackagePath}";
    }
}

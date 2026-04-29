using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Idvbp.Neo.Views;

public sealed class WebLayoutEditorWindow : Window
{
    private readonly NativeWebView _webView;

    public WebLayoutEditorWindow(string title, string url)
    {
        Title = title;
        Width = 1440;
        Height = 900;
        MinWidth = 960;
        MinHeight = 640;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        _webView = new NativeWebView
        {
            Source = new Uri(url, UriKind.Absolute),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Content = BuildLayout(title, url);
    }

    private Control BuildLayout(string title, string url)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var urlText = new TextBlock
        {
            Text = url,
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var reloadButton = new Button
        {
            Content = "刷新",
            MinWidth = 72,
            Height = 30,
            Padding = new Thickness(10, 0)
        };
        reloadButton.Click += (_, _) => _webView.Source = new Uri(AddReloadToken(url), UriKind.Absolute);

        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 72,
            Height = 30,
            Padding = new Thickness(10, 0)
        };
        closeButton.Click += (_, _) => Close();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10
        };

        header.Children.Add(titleText);
        header.Children.Add(urlText);
        header.Children.Add(reloadButton);
        header.Children.Add(closeButton);
        Grid.SetColumn(urlText, 1);
        Grid.SetColumn(reloadButton, 2);
        Grid.SetColumn(closeButton, 3);

        var headerHost = new Border
        {
            Height = 40,
            Background = new SolidColorBrush(Color.Parse("#111111")),
            Padding = new Thickness(12, 5, 8, 5),
            Child = header
        };
        headerHost.PointerPressed += Header_OnPointerPressed;

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*")
        };
        root.Children.Add(headerHost);
        root.Children.Add(_webView);
        Grid.SetRow(_webView, 1);

        return root;
    }

    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private static string AddReloadToken(string url)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}_reload={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }
}

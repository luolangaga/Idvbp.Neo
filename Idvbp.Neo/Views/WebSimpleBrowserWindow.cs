using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Idvbp.Neo.Views;

public sealed class WebSimpleBrowserWindow : Window
{
    private readonly NativeWebView _webView;
    private readonly string _url;

    public WebSimpleBrowserWindow(string title, string url)
    {
        _url = url;
        Title = title;
        Width = 1280;
        Height = 800;
        MinWidth = 800;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
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
            Content = "\u5237\u65b0",
            MinWidth = 72,
            Height = 30,
            Padding = new Thickness(10, 0)
        };
        reloadButton.Click += (_, _) =>
        {
            var separator = _url.Contains('?') ? '&' : '?';
            _webView.Source = new Uri($"{_url}{separator}_reload={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", UriKind.Absolute);
        };

        var closeButton = new Button
        {
            Content = "\u5173\u95ed",
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
        if (e.Source is not Control control ||
            control.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

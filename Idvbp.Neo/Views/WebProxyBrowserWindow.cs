using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Idvbp.Neo.Views;

public sealed class WebProxyBrowserWindow : Window
{
    private readonly NativeWebView _webView;

    public WebProxyBrowserWindow(string title, string url)
    {
        Title = title;
        Width = 1280;
        Height = 720;
        MinWidth = 720;
        MinHeight = 420;
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

        var minimizeButton = new Button
        {
            Content = "_",
            Width = 42,
            Height = 30,
            Padding = new Thickness(0)
        };
        minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;

        var closeButton = new Button
        {
            Content = "X",
            Width = 42,
            Height = 30,
            Padding = new Thickness(0)
        };
        closeButton.Click += (_, _) => Close();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10
        };

        header.Children.Add(titleText);
        header.Children.Add(urlText);
        header.Children.Add(minimizeButton);
        header.Children.Add(closeButton);
        Grid.SetColumn(urlText, 1);
        Grid.SetColumn(minimizeButton, 2);
        Grid.SetColumn(closeButton, 3);

        var headerHost = new Border
        {
            Height = 36,
            Background = new SolidColorBrush(Color.Parse("#111111")),
            Padding = new Thickness(12, 3, 6, 3),
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
}

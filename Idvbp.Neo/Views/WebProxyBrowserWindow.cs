using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Idvbp.Neo.Views;

public sealed class WebProxyBrowserWindow : Window
{
    private const double HeaderHeight = 36;
    private const int MoveStep = 40;

    private readonly NativeWebView _webView;
    private readonly int _viewportWidth;
    private readonly int _viewportHeight;

    public WebProxyBrowserWindow(string title, string url)
        : this(title, url, 1280, 720)
    {
    }

    public WebProxyBrowserWindow(string title, string url, int viewportWidth, int viewportHeight)
    {
        _viewportWidth = Math.Clamp(viewportWidth, 320, 7680);
        _viewportHeight = Math.Clamp(viewportHeight, 240, 4320);

        Title = title;
        Width = _viewportWidth;
        Height = _viewportHeight + HeaderHeight;
        MinWidth = _viewportWidth;
        MinHeight = _viewportHeight + HeaderHeight;
        MaxWidth = _viewportWidth;
        MaxHeight = _viewportHeight + HeaderHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Black;
        KeyDown += Window_OnKeyDown;

        _webView = new NativeWebView
        {
            Source = new Uri(url, UriKind.Absolute),
            Width = _viewportWidth,
            Height = _viewportHeight,
            MinWidth = _viewportWidth,
            MinHeight = _viewportHeight,
            MaxWidth = _viewportWidth,
            MaxHeight = _viewportHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        Content = BuildLayout(title, url);
        Opened += (_, _) => ApplyViewportWindowSize();
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10
        };

        var leftButton = CreateMoveButton("←", -MoveStep, 0);
        var upButton = CreateMoveButton("↑", 0, -MoveStep);
        var downButton = CreateMoveButton("↓", 0, MoveStep);
        var rightButton = CreateMoveButton("→", MoveStep, 0);

        header.Children.Add(titleText);
        header.Children.Add(urlText);
        header.Children.Add(leftButton);
        header.Children.Add(upButton);
        header.Children.Add(downButton);
        header.Children.Add(rightButton);
        header.Children.Add(minimizeButton);
        header.Children.Add(closeButton);
        Grid.SetColumn(urlText, 1);
        Grid.SetColumn(leftButton, 2);
        Grid.SetColumn(upButton, 3);
        Grid.SetColumn(downButton, 4);
        Grid.SetColumn(rightButton, 5);
        Grid.SetColumn(minimizeButton, 6);
        Grid.SetColumn(closeButton, 7);

        var headerHost = new Border
        {
            Height = HeaderHeight,
            Background = new SolidColorBrush(Color.Parse("#111111")),
            Padding = new Thickness(12, 3, 6, 3),
            Child = header
        };
        headerHost.PointerPressed += Header_OnPointerPressed;

        var root = new Grid
        {
            Width = _viewportWidth,
            Height = _viewportHeight + HeaderHeight,
            MinWidth = _viewportWidth,
            MinHeight = _viewportHeight + HeaderHeight,
            MaxWidth = _viewportWidth,
            MaxHeight = _viewportHeight + HeaderHeight,
            RowDefinitions = new RowDefinitions($"{HeaderHeight},{_viewportHeight}")
        };
        root.Children.Add(headerHost);
        root.Children.Add(_webView);
        Grid.SetRow(_webView, 1);

        return root;
    }

    private void ApplyViewportWindowSize()
    {
        Width = _viewportWidth;
        Height = _viewportHeight + HeaderHeight;
        _webView.Width = _viewportWidth;
        _webView.Height = _viewportHeight;
        ClampWindowPositionToScreen();
        Focus();
    }

    private Button CreateMoveButton(string content, int deltaX, int deltaY)
    {
        var button = new Button
        {
            Content = content,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0)
        };
        button.Click += (_, _) => MoveWindow(deltaX, deltaY);
        return button;
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                MoveWindow(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : -MoveStep, 0);
                e.Handled = true;
                break;
            case Key.Right:
                MoveWindow(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : MoveStep, 0);
                e.Handled = true;
                break;
            case Key.Up:
                MoveWindow(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : -MoveStep);
                e.Handled = true;
                break;
            case Key.Down:
                MoveWindow(0, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : MoveStep);
                e.Handled = true;
                break;
        }
    }

    private void MoveWindow(int deltaX, int deltaY)
    {
        Position = new PixelPoint(Position.X + deltaX, Position.Y + deltaY);
    }

    private void ClampWindowPositionToScreen()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
        {
            return;
        }

        var bounds = screen.WorkingArea;
        var x = Math.Clamp(Position.X, bounds.X, Math.Max(bounds.X, bounds.Right - _viewportWidth));
        var y = Math.Clamp(Position.Y, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - (_viewportHeight + (int)HeaderHeight)));
        Position = new PixelPoint(x, y);
    }

    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
#pragma warning disable CS8625
        _webView.Source = default;
#pragma warning restore CS8625
        base.OnClosed(e);
    }
}

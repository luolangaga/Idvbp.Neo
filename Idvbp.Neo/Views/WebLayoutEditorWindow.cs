using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Idvbp.Neo.Views;

public sealed class WebLayoutEditorWindow : Window
{
    private readonly NativeWebView _webView;
    private readonly Func<int, int, Task<bool>>? _saveViewportAsync;
    private readonly string _baseUrl;
    private ComboBox? _resolutionSelector;
    private TextBox? _widthBox;
    private TextBox? _heightBox;
    private TextBlock? _viewportStatusText;
    private int _viewportWidth;
    private int _viewportHeight;
    private string _currentUrl;

    public WebLayoutEditorWindow(
        string title,
        string url,
        int viewportWidth,
        int viewportHeight,
        Func<int, int, Task<bool>>? saveViewportAsync = null)
    {
        _baseUrl = url;
        _currentUrl = url;
        _viewportWidth = Math.Clamp(viewportWidth, 320, 7680);
        _viewportHeight = Math.Clamp(viewportHeight, 240, 4320);
        _saveViewportAsync = saveViewportAsync;

        Title = title;
        Width = 1480;
        Height = 920;
        MinWidth = 960;
        MinHeight = 640;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        _webView = new NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Content = BuildLayout(title, url);
        LoadEditorShell();
    }

    public WebLayoutEditorWindow(string title, string url)
        : this(title, url, 1280, 720)
    {
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

        _resolutionSelector = new ComboBox
        {
            ItemsSource = ResolutionPreset.Presets,
            SelectedItem = ResolutionPreset.Find(_viewportWidth, _viewportHeight),
            Width = 142,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center
        };
        _resolutionSelector.SelectionChanged += (_, _) =>
        {
            if (_resolutionSelector.SelectedItem is ResolutionPreset { IsCustom: false } preset)
            {
                ApplyViewportSize((int)preset.Width, (int)preset.Height);
            }
        };

        _widthBox = new TextBox
        {
            Width = 72,
            Height = 30,
            Text = _viewportWidth.ToString(CultureInfo.InvariantCulture),
            Watermark = "W",
            VerticalContentAlignment = VerticalAlignment.Center
        };

        _heightBox = new TextBox
        {
            Width = 72,
            Height = 30,
            Text = _viewportHeight.ToString(CultureInfo.InvariantCulture),
            Watermark = "H",
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var applySizeButton = new Button
        {
            Content = "Apply",
            MinWidth = 64,
            Height = 30,
            Padding = new Thickness(10, 0)
        };
        applySizeButton.Click += (_, _) => ApplyViewportSizeFromTextBoxes();

        var saveSizeButton = new Button
        {
            Content = "Save Viewport",
            MinWidth = 112,
            Height = 30,
            Padding = new Thickness(10, 0),
            IsEnabled = _saveViewportAsync is not null
        };
        saveSizeButton.Click += async (_, _) => await SaveViewportAsync(saveSizeButton);

        var reloadButton = new Button
        {
            Content = "\u5237\u65b0",
            MinWidth = 72,
            Height = 30,
            Padding = new Thickness(10, 0)
        };
        reloadButton.Click += (_, _) =>
        {
            _currentUrl = AddReloadToken(_baseUrl);
            LoadEditorShell();
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 10
        };

        header.Children.Add(titleText);
        header.Children.Add(urlText);
        header.Children.Add(_resolutionSelector);
        header.Children.Add(_widthBox);
        header.Children.Add(_heightBox);
        header.Children.Add(applySizeButton);
        header.Children.Add(saveSizeButton);
        header.Children.Add(reloadButton);
        header.Children.Add(closeButton);
        Grid.SetColumn(urlText, 1);
        Grid.SetColumn(_resolutionSelector, 2);
        Grid.SetColumn(_widthBox, 3);
        Grid.SetColumn(_heightBox, 4);
        Grid.SetColumn(applySizeButton, 5);
        Grid.SetColumn(saveSizeButton, 6);
        Grid.SetColumn(reloadButton, 7);
        Grid.SetColumn(closeButton, 8);

        var headerHost = new Border
        {
            Height = 40,
            Background = new SolidColorBrush(Color.Parse("#111111")),
            Padding = new Thickness(12, 5, 8, 5),
            Child = header
        };
        headerHost.PointerPressed += Header_OnPointerPressed;

        _viewportStatusText = new TextBlock
        {
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusHost = new Border
        {
            Height = 28,
            Background = new SolidColorBrush(Color.Parse("#181818")),
            Child = _viewportStatusText
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*")
        };
        root.Children.Add(headerHost);
        root.Children.Add(statusHost);
        root.Children.Add(_webView);
        Grid.SetRow(statusHost, 1);
        Grid.SetRow(_webView, 2);

        return root;
    }

    private void ApplyViewportSizeFromTextBoxes()
    {
        if (_widthBox is null || _heightBox is null)
        {
            return;
        }

        if (!TryParseViewportSize(_widthBox.Text, _heightBox.Text, out var width, out var height))
        {
            SetStatus("Viewport size must be between 320x240 and 7680x4320.");
            return;
        }

        if (_resolutionSelector is not null)
        {
            _resolutionSelector.SelectedItem = ResolutionPreset.Custom;
        }

        ApplyViewportSize(width, height);
    }

    private async Task SaveViewportAsync(Button saveButton)
    {
        if (_saveViewportAsync is null)
        {
            return;
        }

        ApplyViewportSizeFromTextBoxes();
        saveButton.IsEnabled = false;
        try
        {
            var saved = await _saveViewportAsync(_viewportWidth, _viewportHeight);
            SetStatus(saved
                ? $"Viewport saved: {_viewportWidth} x {_viewportHeight}"
                : "Viewport save failed.");
        }
        catch (Exception ex)
        {
            SetStatus($"Viewport save failed: {ex.Message}");
        }
        finally
        {
            saveButton.IsEnabled = true;
        }
    }

    private void ApplyViewportSize(int width, int height)
    {
        _viewportWidth = Math.Clamp(width, 320, 7680);
        _viewportHeight = Math.Clamp(height, 240, 4320);

        if (_widthBox is not null)
        {
            _widthBox.Text = _viewportWidth.ToString(CultureInfo.InvariantCulture);
        }

        if (_heightBox is not null)
        {
            _heightBox.Text = _viewportHeight.ToString(CultureInfo.InvariantCulture);
        }

        LoadEditorShell();
    }

    private void LoadEditorShell()
    {
        _webView.NavigateToString(BuildEditorShellHtml(_currentUrl, _viewportWidth, _viewportHeight), new Uri(_currentUrl));
        SetStatus($"Viewport: {_viewportWidth} x {_viewportHeight} - page boundary is shown by the red frame");
    }

    private static string BuildEditorShellHtml(string url, int width, int height)
    {
        var encodedUrl = WebUtility.HtmlEncode(url);
        return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<style>
html, body {
  margin: 0;
  width: 100%;
  height: 100%;
  overflow: hidden;
  background: #050505;
  color: #cfcfcf;
  font-family: "Segoe UI", sans-serif;
}
.stage {
  box-sizing: border-box;
  width: 100vw;
  height: 100vh;
  overflow: auto;
  padding: 24px;
  background:
    linear-gradient(45deg, #111 25%, transparent 25%) 0 0 / 24px 24px,
    linear-gradient(45deg, transparent 75%, #111 75%) 0 0 / 24px 24px,
    #050505;
}
.frame {
  width: {{width}}px;
  height: {{height}}px;
  border: 2px solid #ff4d4f;
  outline: 1px dashed rgba(255, 255, 255, .55);
  background: #000;
  box-shadow: 0 0 0 1px rgba(0, 0, 0, .85), 0 12px 42px rgba(0, 0, 0, .5);
}
.label {
  position: sticky;
  left: 24px;
  top: 0;
  display: inline-block;
  margin: 0 0 8px 0;
  padding: 4px 8px;
  background: rgba(15, 15, 15, .92);
  border: 1px solid rgba(255, 77, 79, .75);
  color: #f2f2f2;
  font-size: 12px;
  z-index: 2;
}
iframe {
  width: 100%;
  height: 100%;
  border: 0;
  display: block;
  background: #000;
}
</style>
</head>
<body>
  <div class="stage">
    <div class="label">Page boundary: {{width}} x {{height}}</div>
    <div class="frame">
      <iframe src="{{encodedUrl}}" allow="fullscreen"></iframe>
    </div>
  </div>
</body>
</html>
""";
    }

    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control control ||
            control.FindAncestorOfType<Button>() is not null ||
            control.FindAncestorOfType<TextBox>() is not null ||
            control.FindAncestorOfType<ComboBox>() is not null)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void SetStatus(string text)
    {
        if (_viewportStatusText is not null)
        {
            _viewportStatusText.Text = text;
        }
    }

    private static bool TryParseViewportSize(string? widthText, string? heightText, out int width, out int height)
    {
        var validWidth = int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out width);
        var validHeight = int.TryParse(heightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);
        return validWidth &&
               validHeight &&
               width is >= 320 and <= 7680 &&
               height is >= 240 and <= 4320;
    }

    private static string AddReloadToken(string url)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}_reload={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private sealed record ResolutionPreset(string Name, double Width, double Height)
    {
        public static readonly ResolutionPreset Custom = new("Custom", 0, 0);

        public static IReadOnlyList<ResolutionPreset> Presets { get; } =
        [
            new("1280 x 720", 1280, 720),
            new("1366 x 768", 1366, 768),
            new("1600 x 900", 1600, 900),
            new("1920 x 1080", 1920, 1080),
            new("2560 x 1440", 2560, 1440),
            new("3840 x 2160", 3840, 2160),
            Custom
        ];

        public bool IsCustom => ReferenceEquals(this, Custom);

        public static ResolutionPreset Find(double width, double height)
            => Presets.FirstOrDefault(preset =>
                !preset.IsCustom &&
                Math.Abs(preset.Width - width) < 0.5 &&
                Math.Abs(preset.Height - height) < 0.5) ?? Custom;

        public override string ToString() => Name;
    }
}

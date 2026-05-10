using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using Idvbp.Neo.Services;

namespace Idvbp.Neo.Views;

public partial class BackendSelectionDialog : AppWindow
{
    private BackendMode _selectedMode = BackendMode.NotSet;

    public BackendMode SelectedMode => _selectedMode;

    public BackendSelectionDialog()
    {
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        InitializeComponent();
    }

    private void NativeCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectCard(BackendMode.Native);
    }

    private void WebCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SelectCard(BackendMode.Web);
    }

    private void SelectCard(BackendMode mode)
    {
        _selectedMode = mode;

        var nativeCard = this.FindControl<Border>("NativeCard");
        var webCard = this.FindControl<Border>("WebCard");
        var nativeCheckmark = this.FindControl<Border>("NativeCheckmark");
        var webCheckmark = this.FindControl<Border>("WebCheckmark");
        var confirmBtn = this.FindControl<Button>("ConfirmButton");

        if (nativeCard is null || webCard is null || confirmBtn is null) return;

        var nativeAccent = this.Find<SolidColorBrush>("NativeAccentBrush");
        var webAccent = this.Find<SolidColorBrush>("WebAccentBrush");
        var nativeSelectedBg = this.Find<SolidColorBrush>("NativeCardSelectedBrush");
        var webSelectedBg = this.Find<SolidColorBrush>("WebCardSelectedBrush");
        var dimBrush = this.Find<SolidColorBrush>("UnselectedDimBrush");
        var defaultBorderBrush = this.Find<SolidColorBrush>("CardStrokeColorDefaultBrush")
                              ?? new SolidColorBrush(Color.Parse("#0F000000"));

        if (mode == BackendMode.Native)
        {
            nativeCard.BorderBrush = nativeAccent;
            nativeCard.BorderThickness = new Thickness(3);
            nativeCard.Background = nativeSelectedBg;
            nativeCard.Opacity = 1.0;

            webCard.BorderBrush = defaultBorderBrush;
            webCard.BorderThickness = new Thickness(2);
            webCard.Background = dimBrush;
            webCard.Opacity = 0.6;

            if (nativeCheckmark != null) nativeCheckmark.IsVisible = true;
            if (webCheckmark != null) webCheckmark.IsVisible = false;
        }
        else
        {
            webCard.BorderBrush = webAccent;
            webCard.BorderThickness = new Thickness(3);
            webCard.Background = webSelectedBg;
            webCard.Opacity = 1.0;

            nativeCard.BorderBrush = defaultBorderBrush;
            nativeCard.BorderThickness = new Thickness(2);
            nativeCard.Background = dimBrush;
            nativeCard.Opacity = 0.6;

            if (webCheckmark != null) webCheckmark.IsVisible = true;
            if (nativeCheckmark != null) nativeCheckmark.IsVisible = false;
        }

        confirmBtn.IsEnabled = true;
    }

    private void NativeCard_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_selectedMode != BackendMode.Native && sender is Border border)
        {
            border.Background = this.Find<SolidColorBrush>("NativeCardHoverBrush");
        }
    }

    private void NativeCard_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_selectedMode != BackendMode.Native && sender is Border border)
        {
            border.Background = this.Find<SolidColorBrush>("UnselectedDimBrush");
        }
    }

    private void WebCard_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (_selectedMode != BackendMode.Web && sender is Border border)
        {
            border.Background = this.Find<SolidColorBrush>("WebCardHoverBrush");
        }
    }

    private void WebCard_PointerExited(object? sender, PointerEventArgs e)
    {
        if (_selectedMode != BackendMode.Web && sender is Border border)
        {
            border.Background = this.Find<SolidColorBrush>("UnselectedDimBrush");
        }
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedMode == BackendMode.NotSet) return;
        Close(_selectedMode);
    }
}

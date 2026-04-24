using System;
using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Controls;

/// <summary>
/// Application frame wrapper that exposes FluentAvalonia frame navigation through the core abstraction.
/// </summary>
public class Frame : FluentAvalonia.UI.Controls.Frame, IFrame
{
    // Keep FluentAvalonia's built-in template. Without this override Avalonia searches for
    // a theme keyed by Idvbp.Neo.Controls.Frame and the frame can render without its template.
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.Frame);

    /// <inheritdoc />
    public new bool Navigate(Type pageType)
    {
        return base.Navigate(pageType);
    }

    /// <summary>
    /// Navigates from a target object for compatibility with callers that still pass object values.
    /// </summary>
    /// <param name="content">A navigation target understood by FluentAvalonia's frame.</param>
    public void Navigate(object? content)
    {
        NavigateFromObject(content);
    }
}

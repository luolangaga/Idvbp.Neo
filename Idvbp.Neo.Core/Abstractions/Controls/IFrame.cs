using System;

namespace Idvbp.Neo.Core.Abstractions.Controls;

/// <summary>
/// Abstraction over the application's navigation frame.
/// </summary>
public interface IFrame
{
    /// <summary>
    /// Navigates to a page represented by its view type.
    /// </summary>
    /// <param name="pageType">The Avalonia control type to navigate to.</param>
    /// <returns><see langword="true"/> when navigation was accepted by the underlying frame.</returns>
    public bool Navigate(Type pageType);

    /// <summary>
    /// Gets whether the frame has an entry available in its back stack.
    /// </summary>
    public bool CanGoBack { get; }

    /// <summary>
    /// Navigates to the previous frame entry.
    /// </summary>
    public void GoBack();
}

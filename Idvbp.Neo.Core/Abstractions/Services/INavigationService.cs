using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Core.Abstractions.Services;

/// <summary>
/// Coordinates top-level page navigation between the navigation view and the content frame.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets or sets the navigation view associated with the main window.
    /// </summary>
    public INavigationView? NavigationControl { get; set; }

    /// <summary>
    /// Gets or sets the frame that hosts navigated pages.
    /// </summary>
    public IFrame? FrameControl { get; set; }

    /// <summary>
    /// Associates the service with the main navigation view.
    /// </summary>
    /// <param name="navigationControl">The navigation control to track.</param>
    public void SetNavigationControl(INavigationView navigationControl);

    /// <summary>
    /// Associates the service with the frame that performs page navigation.
    /// </summary>
    /// <param name="frameControl">The frame control to track.</param>
    public void SetFrameControl(IFrame frameControl);

    /// <summary>
    /// Navigates to the specified page type when it is not <see langword="null"/>.
    /// </summary>
    /// <param name="pageType">The target page type, or <see langword="null"/> to ignore the request.</param>
    /// <returns><see langword="true"/> when navigation was performed.</returns>
    public bool Navigate(Type? pageType);

    /// <summary>
    /// Navigates backward when the frame has a previous entry.
    /// </summary>
    /// <returns><see langword="true"/> when the frame navigated backward.</returns>
    public bool GoBack();
}

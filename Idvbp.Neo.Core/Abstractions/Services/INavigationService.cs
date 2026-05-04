using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Core.Abstractions.Services;

/// <summary>
/// 导航服务接口，协调导航视图与内容框架之间的顶级页面导航。
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 获取或设置与主窗口关联的导航视图。
    /// </summary>
    public INavigationView? NavigationControl { get; set; }

    /// <summary>
    /// 获取或设置承载导航页面的框架。
    /// </summary>
    public IFrame? FrameControl { get; set; }

    /// <summary>
    /// 将服务与主导航视图关联。
    /// </summary>
    /// <param name="navigationControl">要跟踪的导航控件。</param>
    public void SetNavigationControl(INavigationView navigationControl);

    /// <summary>
    /// 将服务与执行页面导航的框架关联。
    /// </summary>
    /// <param name="frameControl">要跟踪的框架控件。</param>
    public void SetFrameControl(IFrame frameControl);

    /// <summary>
    /// 导航到指定页面类型；若为 <see langword="null"/> 则忽略请求。
    /// </summary>
    /// <param name="pageType">目标页面类型，或为 <see langword="null"/> 以忽略请求。</param>
    /// <returns>导航成功时返回 <see langword="true"/>。</returns>
    public bool Navigate(Type? pageType);

    /// <summary>
    /// 当框架存在上一页时向后导航。
    /// </summary>
    /// <returns>向后导航成功时返回 <see langword="true"/>。</returns>
    public bool GoBack();
}

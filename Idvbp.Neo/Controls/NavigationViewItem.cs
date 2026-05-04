using System;

namespace Idvbp.Neo.Controls;

/// <summary>
/// 导航项，直接携带目标页面类型，而非使用字符串标签。
/// </summary>
public class NavigationViewItem : FluentAvalonia.UI.Controls.NavigationViewItem
{
    // 保留 FluentAvalonia 的控件主题，确保派生类型正确渲染。
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.NavigationViewItem);

    /// <summary>
    /// 获取导航项被调用时应显示的目标页面类型。
    /// </summary>
    public Type? TargetPageType { get; init; }
}

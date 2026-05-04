using System;
using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Controls;

/// <summary>
/// 应用程序框架导航包装类，通过核心抽象暴露 FluentAvalonia 的框架导航能力。
/// </summary>
public class Frame : FluentAvalonia.UI.Controls.Frame, IFrame
{
    // 保留 FluentAvalonia 的内置模板；若无此重写，Avalonia 会查找以 Idvbp.Neo.Controls.Frame 为键的主题，
    // 可能导致框架无法正确渲染模板。
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.Frame);

    /// <inheritdoc />
    public new bool Navigate(Type pageType)
    {
        return base.Navigate(pageType);
    }

    /// <summary>
    /// 从目标对象进行导航，兼容仍传递对象值的调用方。
    /// </summary>
    /// <param name="content">FluentAvalonia 框架可理解的导航目标。</param>
    public void Navigate(object? content)
    {
        NavigateFromObject(content);
    }
}

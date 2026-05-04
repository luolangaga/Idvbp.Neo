using System;
using Idvbp.Neo.Core.Abstractions.Controls;

namespace Idvbp.Neo.Controls;

/// <summary>
/// 应用程序导航视图包装类，供导航服务使用。
/// </summary>
public class NavigationView : FluentAvalonia.UI.Controls.NavigationView, INavigationView
{
    // 保留 FluentAvalonia 的控件主题，确保派生类型正确渲染。
    protected override Type StyleKeyOverride => typeof(FluentAvalonia.UI.Controls.NavigationView);
}

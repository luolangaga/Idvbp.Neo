using CommunityToolkit.Mvvm.ComponentModel;

namespace Idvbp.Neo.ViewModels.Pages;

/// <summary>
/// 首页视图模型。
/// </summary>
public partial class HomePageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private string _releaseNotes = """
        # 更新日志

        ## v1.0.0 (2026-04-24)

        ### 新增功能
        - 全新的现代化 UI 界面
        - 支持地图 BP 功能
        - 支持队伍信息管理
        - 支持 JSON 导入队伍数据

        ### 优化
        - 优化界面交互体验
        - 提升应用启动速度

        ### 修复
        - 修复若干已知问题

        ---

        更多更新内容请关注官方文档。
        """;
}

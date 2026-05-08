using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Idvbp.Neo.Views;

namespace Idvbp.Neo.Services;

public static class GlobalExceptionHandler
{
    private static Window? _owner;
    private static bool _isShowing;

    public static void Initialize(Window owner)
    {
        _owner = owner;

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            args.Handled = true;
            Show(args.Exception, "Avalonia UI 线程");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            Show(args.Exception, "后台任务");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Show(exception, args.IsTerminating ? "未处理异常（程序即将退出）" : "未处理异常");
            }
        };
    }

    public static void Show(Exception exception, string source)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isShowing)
            {
                return;
            }

            _isShowing = true;
            var window = new ErrorReportWindow(exception, source);
            window.Closed += (_, _) => _isShowing = false;

            if (_owner?.IsVisible == true)
            {
                window.Show(_owner);
            }
            else
            {
                window.Show();
            }
        });
    }
}

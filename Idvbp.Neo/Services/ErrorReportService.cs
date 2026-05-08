using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Idvbp.Neo.Services;

public sealed class ErrorReportService
{
    public const string RepositoryIssueUrl = "https://github.com/AyaSlinc/Idvbp.Neo/issues/new";

    public string BuildErrorText(Exception exception, string source)
    {
        var now = DateTime.Now;
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version?.ToString() ?? "unknown";

        var builder = new StringBuilder();
        builder.AppendLine("# Idvbp.Neo 错误报告");
        builder.AppendLine();
        builder.AppendLine("## 摘要");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 时间: {now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 来源: {source}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 类型: {exception.GetType().FullName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 消息: {exception.Message}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 程序版本: {version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- .NET: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 系统: {RuntimeInformation.OSDescription}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 架构: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 工作目录: {Directory.GetCurrentDirectory()}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 进程: {Environment.ProcessId}");
        builder.AppendLine();
        builder.AppendLine("## 异常详情");
        builder.AppendLine("```text");
        builder.AppendLine(exception.ToString());
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("## 最近运行日志");
        builder.AppendLine("```text");
        builder.AppendLine(ReadRecentRuntimeLogs(240));
        builder.AppendLine("```");
        return builder.ToString();
    }

    public string PackageError(Exception exception, string source)
    {
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "bug-reports");
        Directory.CreateDirectory(outputDir);

        var packagePath = Path.Combine(outputDir, $"idvbp-neo-error-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        WriteEntry(archive, "error-report.md", BuildErrorText(exception, source));
        WriteEntry(archive, "exception.txt", exception.ToString());
        WriteEntry(archive, "recent-runtime-logs.txt", ReadRecentRuntimeLogs(600));
        WriteEntry(archive, "environment.txt", BuildEnvironmentText());
        return packagePath;
    }

    public void OpenGitHubIssue(Exception exception, string source, string packagePath)
    {
        var title = Uri.EscapeDataString($"错误报告: {exception.GetType().Name}");
        var body = BuildIssueBody(exception, source, packagePath);
        var url = $"{RepositoryIssueUrl}?title={title}&body={Uri.EscapeDataString(body)}";

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private string BuildIssueBody(Exception exception, string source, string packagePath)
    {
        var errorPreview = exception.ToString();
        if (errorPreview.Length > 8000)
        {
            errorPreview = errorPreview[..8000] + Environment.NewLine + "...内容过长，完整信息请附加 zip 文件...";
        }

        return $"""
        ## 问题描述
        请补充你刚才正在执行的操作，以及是否可以稳定复现。

        ## 错误摘要
        - 来源: {source}
        - 类型: {exception.GetType().FullName}
        - 消息: {exception.Message}

        ## 附件
        已在本机生成错误包：{packagePath}

        GitHub 网页无法自动上传本地文件，请在提交前把该 zip 文件拖入 Issue。

        ## 错误预览
        ```text
        {errorPreview}
        ```
        """;
    }

    private static string BuildEnvironmentText()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return $"""
        Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}
        Version: {assembly.GetName().Version?.ToString() ?? "unknown"}
        Framework: {RuntimeInformation.FrameworkDescription}
        OS: {RuntimeInformation.OSDescription}
        OS Architecture: {RuntimeInformation.OSArchitecture}
        Process Architecture: {RuntimeInformation.ProcessArchitecture}
        Current Directory: {Directory.GetCurrentDirectory()}
        Base Directory: {AppContext.BaseDirectory}
        Machine Name: {Environment.MachineName}
        User Name: {Environment.UserName}
        """;
    }

    private static string ReadRecentRuntimeLogs(int maxLines)
    {
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs", "runtime");
        if (!Directory.Exists(logDir))
        {
            return "未找到运行日志目录。";
        }

        try
        {
            var lines = Directory
                .GetFiles(logDir, "runtime-*.log")
                .OrderByDescending(x => x)
                .Take(3)
                .SelectMany(File.ReadLines)
                .TakeLast(maxLines)
                .ToList();

            return lines.Count == 0 ? "运行日志为空。" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return $"读取运行日志失败: {ex.Message}";
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }
}

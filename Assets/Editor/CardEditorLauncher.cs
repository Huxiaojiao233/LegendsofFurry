#if UNITY_EDITOR_WIN
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 Unity Tools 菜单提供卡牌维护工具入口，方便开发期策划从工程内直接启动 WPF。
/// </summary>
public static class CardEditorLauncher
{
    /// <summary>
    /// 直接启动已发布的 WPF 程序；发布文件缺失时先执行一次 Release 构建。
    /// </summary>
    [MenuItem("Tools/Legends Of Furry/打开卡牌维护工具")]
    private static void OpenCardEditor()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
        string editorProject = Path.Combine(projectRoot, "Tools", "CardEditor.Wpf", "CardEditor.Wpf.csproj");
        string publishedApplication = Path.Combine(projectRoot, "Tools", "CardEditor.Wpf", "bin", "Release", "publish-v2", "CardEditor.Wpf.exe");
        string buildApplication = Path.Combine(projectRoot, "Tools", "CardEditor.Wpf", "bin", "Release", "net8.0-windows", "CardEditor.Wpf.exe");
        if (!File.Exists(publishedApplication) && !File.Exists(buildApplication) && !TryBuildEditor(editorProject, projectRoot))
        {
            return;
        }

        string applicationPath = File.Exists(publishedApplication) ? publishedApplication : buildApplication;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = applicationPath,
                WorkingDirectory = projectRoot,
                UseShellExecute = true
            });
        }
        catch (System.Exception exception)
        {
            EditorUtility.DisplayDialog("卡牌维护工具", $"启动失败：\n{exception.Message}\n\n程序路径：\n{applicationPath}", "确定");
        }
    }

    /// <summary>同步调用 dotnet 构建 WPF 项目，并在失败时显示完整输出。</summary>
    /// <param name="editorProject">WPF 项目文件的绝对路径。</param>
    /// <param name="projectRoot">Unity 项目根目录。</param>
    /// <returns>构建进程成功退出时返回 true。</returns>
    private static bool TryBuildEditor(string editorProject, string projectRoot)
    {
        if (!File.Exists(editorProject))
        {
            EditorUtility.DisplayDialog("卡牌维护工具", $"找不到工具项目：\n{editorProject}", "确定");
            return false;
        }

        try
        {
            Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{editorProject}\" --configuration Release --nologo",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return true;
            EditorUtility.DisplayDialog("卡牌维护工具", $"工具构建失败：\n{output}\n{error}", "确定");
            return false;
        }
        catch (System.Exception exception)
        {
            EditorUtility.DisplayDialog("卡牌维护工具", $"无法调用 dotnet 构建工具：\n{exception.Message}", "确定");
            return false;
        }
    }
}
#endif

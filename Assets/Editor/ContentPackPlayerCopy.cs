#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 把正式投放的 lofe_core.lofepackage 拷进玩家构建。Unity 不会自动带上工程根目录的 Content/Packs。
/// </summary>
public sealed class ContentPackPlayerCopy : IPostprocessBuildWithReport
{
    public const string CorePackId = "lofe_core";
    public const string PackageExtension = ".lofepackage";

    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report == null || report.summary.result == BuildResult.Failed)
        {
            return;
        }

        string outputPath = report.summary.outputPath;
        if (string.IsNullOrEmpty(outputPath))
        {
            return;
        }

        string exeDirectory = Directory.Exists(outputPath)
            ? outputPath
            : Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(exeDirectory))
        {
            throw new BuildFailedException("无法解析构建输出目录，内容包没有打进玩家包。");
        }

        string source = ResolveCorePackagePath();
        string dataDirectory = Path.Combine(
            exeDirectory,
            Path.GetFileNameWithoutExtension(outputPath) + "_Data");
        string streamingPacks = Path.Combine(dataDirectory, "StreamingAssets", "Content", "Packs");
        Directory.CreateDirectory(streamingPacks);
        string destination = Path.Combine(streamingPacks, Path.GetFileName(source));
        File.Copy(source, destination, true);
        Debug.Log($"已把 {CorePackId} 拷入玩家包：{destination}");
    }

    [MenuItem("Tools/Legends Of Furry/复制核心内容包到 StreamingAssets（调试）")]
    private static void CopyToStreamingAssetsForDebug()
    {
        string source = ResolveCorePackagePath();
        string destination = Path.Combine(Application.dataPath, "StreamingAssets", "Content", "Packs", Path.GetFileName(source));
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, true);
        AssetDatabase.Refresh();
        Debug.Log($"已复制到 {destination}。正式构建会在打包后自动拷贝，不必提交这份副本。");
    }

    internal static string ResolveCorePackagePath()
    {
        string packsRoot = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Content", "Packs");
        string packageFile = Path.Combine(packsRoot, CorePackId + PackageExtension);
        if (File.Exists(packageFile)) return packageFile;
        string folder = Path.Combine(packsRoot, CorePackId);
        if (Directory.Exists(folder) && File.Exists(Path.Combine(folder, "pack.json")))
            throw new BuildFailedException($"核心内容包仍是旧版文件夹：{folder}。请先导出 {CorePackId}{PackageExtension}。");
        throw new BuildFailedException($"缺少要打进玩家包的内容包：{packageFile}");
    }
}
#endif

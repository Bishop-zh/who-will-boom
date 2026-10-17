using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Reproducible single-thread WebGL build for static GitHub Pages hosting.</summary>
public static class PortfolioWebGLBuild
{
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-portfolioOutput");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -portfolioOutput");
        string output = Path.GetFullPath(args[index + 1]);
        var compression = PlayerSettings.WebGL.compressionFormat;
        bool fallback = PlayerSettings.WebGL.decompressionFallback;
        try
        {
            // Pages cannot supply custom Content-Encoding headers. Unity's loader decompresses these files.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                target = BuildTarget.WebGL,
                locationPathName = output,
                options = BuildOptions.None
            });
            File.WriteAllText(Path.Combine(output, "build-report.txt"),
                "Result: " + report.summary.result + "\nUnity: " + Application.unityVersion +
                "\nBytes: " + report.summary.totalSize + "\nErrors: " + report.summary.totalErrors + "\n");
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception("WebGL build failed: " + report.summary.result);
            Debug.Log("PORTFOLIO_WEBGL_BUILD_OK " + output);
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = compression;
            PlayerSettings.WebGL.decompressionFallback = fallback;
            AssetDatabase.SaveAssets();
        }
    }
}

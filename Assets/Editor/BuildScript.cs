using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MergeUp.Editor
{
    /// <summary>
    /// Menu items and batch-mode entry points. The game builds itself at runtime (see Bootstrap),
    /// so the scene this creates is deliberately empty.
    ///
    /// Batch mode:  Unity -batchmode -quit -projectPath . -executeMethod MergeUp.Editor.BuildScript.BuildWebGL
    /// </summary>
    public static class BuildScript
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Merge Up/Create Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.Refresh();
        }

        [MenuItem("Merge Up/Build WebGL")]
        public static void BuildWebGL()
        {
            // Compression off so the folder works on any static host (GitHub Pages does not serve .br/.gz correctly).
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            Build(BuildTarget.WebGL, CustomBuildPath() ?? "Builds/WebGL");
        }

        [MenuItem("Merge Up/Build Windows")]
        public static void BuildWindows()
        {
            string dir = CustomBuildPath() ?? "Builds/Windows";
            Build(BuildTarget.StandaloneWindows64, Path.Combine(dir, "MergeUp.exe"));
        }

        /// <summary>game-ci passes the output folder as -customBuildPath; use it when present.</summary>
        private static string CustomBuildPath()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-customBuildPath") return args[i + 1];
            return null;
        }

        private static void Build(BuildTarget target, string output)
        {
            CreateScene();
            PlayerSettings.productName = "Merge Up";
            PlayerSettings.companyName = "Supp3r-Man";

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log("Build " + summary.result + ": " + summary.totalSize + " bytes, " + summary.totalErrors + " errors");

            if (summary.result != BuildResult.Succeeded)
            {
                // Non-zero exit so CI notices.
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw new System.Exception("Merge Up build failed: " + summary.result);
            }
        }
    }
}

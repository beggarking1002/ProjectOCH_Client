using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class MultiplayerBuildAndRun
{
    private const int WindowWidth = 400;
    private const int WindowHeight = 400;

    private static readonly List<Process> Processes = new();

    [MenuItem("Tools/Project OCH/Multiplayer/Run Existing/1 Player")]
    private static void RunExisting1() => RunExisting(1);

    [MenuItem("Tools/Project OCH/Multiplayer/Run Existing/2 Players")]
    private static void RunExisting2() => RunExisting(2);

    [MenuItem("Tools/Project OCH/Multiplayer/Run Existing/3 Players")]
    private static void RunExisting3() => RunExisting(3);

    [MenuItem("Tools/Project OCH/Multiplayer/Run Existing/4 Players")]
    private static void RunExisting4() => RunExisting(4);

    [MenuItem("Tools/Project OCH/Multiplayer/Build Client Only")]
    private static void BuildClientOnly() => BuildClient();

    [MenuItem("Tools/Project OCH/Multiplayer/Build And Run/2 Players")]
    private static void BuildAndRun2() => BuildAndRun(2);

    [MenuItem("Tools/Project OCH/Multiplayer/Build And Run/3 Players")]
    private static void BuildAndRun3() => BuildAndRun(3);

    [MenuItem("Tools/Project OCH/Multiplayer/Build And Run/4 Players")]
    private static void BuildAndRun4() => BuildAndRun(4);

    [MenuItem("Tools/Project OCH/Multiplayer/Stop Launched Players")]
    private static void StopLaunchedPlayers()
    {
        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            Process process = Processes[i];
            if (process == null)
                continue;

            try
            {
                if (process.HasExited == false)
                    process.Kill();
            }
            catch
            {
                // The process may already be gone or owned by another context.
            }
        }

        Processes.Clear();
    }

    private static void BuildAndRun(int playerCount)
    {
        string exePath = BuildClient();
        if (string.IsNullOrWhiteSpace(exePath))
            return;

        RunPlayers(playerCount, exePath);
    }

    private static void RunExisting(int playerCount)
    {
        string exePath = GetBuildExePath();
        if (File.Exists(exePath) == false)
        {
            Debug.LogError($"Build executable not found: {exePath}");
            return;
        }

        RunPlayers(playerCount, exePath);
    }

    private static string BuildClient()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);
        }

        PlayerSettings.defaultScreenWidth = WindowWidth;
        PlayerSettings.defaultScreenHeight = WindowHeight;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;

        if (BuildAddressables() == false)
            return null;

        string exePath = GetBuildExePath();
        Directory.CreateDirectory(Path.GetDirectoryName(exePath));

        BuildPlayerOptions options = new()
        {
            scenes = GetEnabledScenePaths(),
            locationPathName = exePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Client build failed: {report.summary.result}");
            return null;
        }

        Debug.Log($"Client build succeeded: {exePath}");
        return exePath;
    }

    private static bool BuildAddressables()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("Addressables settings not found.");
            return false;
        }

        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        if (string.IsNullOrEmpty(result.Error))
            return true;

        Debug.LogError($"Addressables build failed: {result.Error}");
        return false;
    }

    private static void RunPlayers(int playerCount, string exePath)
    {
        Directory.CreateDirectory(GetPlayerLogDirectory());

        for (int i = 1; i <= playerCount; i++)
        {
            Processes.Add(RunProcess(exePath, i));
        }
    }

    private static Process RunProcess(string exePath, int playerIndex)
    {
        string logPath = Path.Combine(GetPlayerLogDirectory(), $"Client_{playerIndex}.log");

        Process process = new()
        {
            StartInfo =
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath),
                Arguments = $"-playerIndex {playerIndex} -screen-width {WindowWidth} -screen-height {WindowHeight} -screen-fullscreen 0 -logFile \"{logPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            },
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) => Processes.Remove(process);
        process.Start();

        return process;
    }

    private static string GetBuildExePath()
    {
        string projectName = GetProjectName();
        return Path.Combine("Builds", "Win64", projectName, $"{projectName}.exe");
    }

    private static string GetPlayerLogDirectory()
    {
        return Path.Combine("Builds", "Win64", "Logs");
    }

    private static string GetProjectName()
    {
        return new DirectoryInfo(Application.dataPath).Parent?.Name ?? Application.productName;
    }

    private static string[] GetEnabledScenePaths()
    {
        List<string> scenes = new();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                scenes.Add(scene.path);
        }

        return scenes.ToArray();
    }
}

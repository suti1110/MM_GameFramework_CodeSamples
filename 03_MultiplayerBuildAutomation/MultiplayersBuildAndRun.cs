using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

public class MultiplayersBuildAndRun
{
    [MenuItem("Tools/Run Multiplayer/2 Players")]
    static void PerformWin64Build2() => PerformWin64Build(2);

    [MenuItem("Tools/Run Multiplayer/3 Players")]
    static void PerformWin64Build3() => PerformWin64Build(3);

    [MenuItem("Tools/Run Multiplayer/4 Players")]
    static void PerformWin64Build4() => PerformWin64Build(4);

    static void PerformWin64Build(int playerCount)
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(
            BuildTargetGroup.Standalone,
            BuildTarget.StandaloneWindows64
        );

        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        // 1. 빌드할 경로 안전하게 설정 (예: 프로젝트폴더/Builds/프로젝트이름/프로젝트이름.exe)
        string buildFolder = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            "Builds",
            GetProjectName()
        );
        string exePath = Path.Combine(buildFolder, GetProjectName() + ".exe");

        // 2. [핵심] 빌드는 딱 1번만 실행! (AutoRunPlayer 옵션 제거)
        EditorLog.Log("멀티플레이어 빌드 시작...");
        var buildReport = BuildPipeline.BuildPlayer(
            GetScenePaths(),
            exePath,
            BuildTarget.StandaloneWindows64,
            BuildOptions.Development
        );

        // 3. 빌드가 성공적으로 끝났을 때만 실행
        if (buildReport.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            EditorLog.Log($"빌드 성공! 클라이언트 {playerCount}개를 실행합니다.");

            // 4. 만들어진 똑같은 .exe 파일을 원하는 인원수만큼 띄우기
            for (int i = 0; i < playerCount; i++)
            {
                ProcessStartInfo info = new ProcessStartInfo(exePath);

                info.Arguments = "-screen-fullscreen 0 -screen-width 800 -screen-height 600";

                Process.Start(info);
            }
        }
        else
        {
            EditorLog.LogError("빌드에 실패하여 실행을 취소합니다.");
        }
    }

    static string GetProjectName()
    {
        // 경로 구분자에 상관없이 안전하게 폴더명을 가져오는 방법
        return new DirectoryInfo(Application.dataPath).Parent.Name;
    }

    static string[] GetScenePaths()
    {
        List<string> scenes = new List<string>();

        foreach (var scene in EditorBuildSettings.scenes)
        {
            // Build Settings에서 체크(활성화)된 씬만 추가
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }

        return scenes.ToArray();
    }
}

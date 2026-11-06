using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Batchmode builds. The shipped build is IL2CPP/ARM64; a separate x86_64 build exists
    /// because the Android emulator's ARM translation cannot execute the BTI instructions
    /// Unity emits (it dies with SIGILL a few seconds in).
    /// </summary>
    public static class BuildSmoke
    {
        const string OutDir = "Builds/Android";
        const string SampleScene = "Assets/Scenes/SampleScene.unity";
        const string M0Scene = "Assets/_Project/Scenes/Run.unity";

        public static string SceneToBuild() => File.Exists(M0Scene) ? M0Scene : SampleScene;

        [MenuItem("Train Survivor/Build (Android ARM64)")]
        public static void BuildArm64()
        {
            Build(AndroidArchitecture.ARM64, "TrainSurvivor-smoke.apk");
        }

        [MenuItem("Train Survivor/Build (Emulator x86_64)")]
        public static void BuildEmulator()
        {
            Build(AndroidArchitecture.X86_64, "TrainSurvivor-emulator.apk");
        }

        /// <summary>Kept for tools/deploy.sh compatibility.</summary>
        public static void BuildAndroid()
        {
            BuildArm64();
        }

        static void Build(AndroidArchitecture arch, string fileName)
        {
            Directory.CreateDirectory(OutDir);

            PlayerSettings.companyName = "TrainSurvivor";
            PlayerSettings.productName = "Train Survivor";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.trainsurvivor.game");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = arch;
            // Emulator (gfxstream) and low-end devices are far more stable on GLES3 than Vulkan.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            var scenes = new[] { SceneToBuild() };
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenes[0], true) };

            if (scenes[0] == SampleScene) DisablePostProcessing(scenes[0]);

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutDir, fileName),
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary s = report.summary;

            Debug.Log($"[BUILD] arch={arch} file={fileName} result={s.result} size={s.totalSize} bytes errors={s.totalErrors} warnings={s.totalWarnings} time={s.totalTime}");

            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    Debug.Log($"[BUILD-STEP] {step.name}: " + string.Join(" | ", System.Linq.Enumerable.Select(step.messages, m => m.type + ":" + m.content)));
                EditorApplication.Exit(1);
            }

            EditorApplication.Exit(0);
        }

        /// <summary>Emulators choke on URP post-processing; the game ships without it anyway.</summary>
        static void DisablePostProcessing(string scenePath)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in cams)
            {
                var data = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = false;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SMOKE] post-processing disabled on {cams.Length} camera(s)");
        }

        /// <summary>Adds the project's gameplay packages at whatever version is compatible.</summary>
        public static void AddPackages()
        {
            var wanted = new[]
            {
                "com.unity.splines",
                "com.unity.cinemachine",
                "com.unity.addressables",
                "com.unity.mobile.android-logcat"
            };

            int pending = wanted.Length;
            foreach (var id in wanted)
            {
                var req = UnityEditor.PackageManager.Client.Add(id);
                EditorApplication.update += () =>
                {
                    if (!req.IsCompleted) return;
                    if (req.Status == UnityEditor.PackageManager.StatusCode.Failure)
                        Debug.LogError($"[PKG] {id} failed: {req.Error?.message}");
                    else
                        Debug.Log($"[PKG] {id} -> {req.Result?.version}");
                    if (--pending == 0) EditorApplication.Exit(0);
                };
            }
        }
    }
}

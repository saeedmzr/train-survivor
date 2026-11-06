using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TrainSurvivor.Game;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Generates the M0 scene and its materials as real assets, so the scene is reproducible
    /// from a batch build and there is no hand-edited YAML to maintain.
    /// </summary>
    public static class M0SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Run.unity";
        const string MatDir = "Assets/Resources/M0";

        [MenuItem("Train Survivor/Build M0 Scene")]
        public static void BuildScene()
        {
            EnsureMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<M0Bootstrap>();

            Directory.CreateDirectory("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[M0] scene written to {ScenePath}");
        }

        static void EnsureMaterials()
        {
            Directory.CreateDirectory(MatDir);

            Mat("Ballast", Palette.Ballast);
            Mat("Rail", Palette.Rail, 0.75f);
            Mat("Sleeper", Palette.Sleeper);
            Mat("Ground", Palette.Ground);
            Mat("TrainBody", Palette.Body, 0.45f);
            Mat("TrainAccent", Palette.Accent, 0.35f);
            Mat("Metal", Palette.Metal, 0.8f);
            Mat("ZombieWalker", Palette.Walker, 0.1f);
            Mat("ZombieSprinter", Palette.Sprinter, 0.1f);
            Mat("ZombieTank", Palette.Tank, 0.1f);
            MatEmissive("Tracer", Palette.Neon);
            MatEmissive("Spark", Palette.Neon);

            AssetDatabase.SaveAssets();
        }

        static void Mat(string name, Color color, float smoothness = 0.25f)
        {
            string path = $"{MatDir}/{name}.mat";
            if (File.Exists(path)) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", smoothness > 0.7f ? 0.85f : 0.1f);
            AssetDatabase.CreateAsset(m, path);
        }

        static void MatEmissive(string name, Color color)
        {
            string path = $"{MatDir}/{name}.mat";
            if (File.Exists(path)) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 2.2f);
            AssetDatabase.CreateAsset(m, path);
        }
    }
}

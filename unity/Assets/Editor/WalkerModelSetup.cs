using UnityEditor;
using UnityEngine;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Configures the imported walker FBX: legacy clips named Walk/Attack/Death (so the runtime
    /// can play them by name without an AnimatorController graph), no imported materials, and a
    /// URP material bound to the albedo. Run from the menu or with -executeMethod in a batch build.
    /// </summary>
    public static class WalkerModelSetup
    {
        const string ModelPath = "Assets/Resources/Models/walker3d.fbx";
        const string TexPath = "Assets/Resources/Models/walker3d_albedo.jpg";
        const string MatPath = "Assets/Resources/Models/WalkerMat.mat";

        [MenuItem("TrainSurvivor/Setup Walker Model")]
        public static void Run()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[walker] no ModelImporter at " + ModelPath);
                return;
            }

            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.importAnimation = true;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.useFileScale = true;

            var takes = importer.defaultClipAnimations;
            Debug.Log("[walker] takes found: " + takes.Length);
            if (takes.Length > 0)
            {
                var clips = new ModelImporterClipAnimation[takes.Length];
                for (int i = 0; i < takes.Length; i++)
                {
                    var c = takes[i];
                    string n = (c.takeName ?? string.Empty).ToLowerInvariant();
                    string mapped;
                    if (n.Contains("shamble") || n.Contains("walk")) mapped = "Walk";
                    else if (n.Contains("attack") || n.Contains("melee")) mapped = "Attack";
                    else if (n.Contains("fatal") || n.Contains("dying") || n.Contains("death")) mapped = "Death";
                    else mapped = "Clip" + i;

                    c.name = mapped;
                    c.loopTime = mapped == "Walk";
                    c.loop = mapped == "Walk";

                    // Tripo's takes are 240 frames with long idle tails; the body only moves inside
                    // these windows (measured in Blender), so trim or an attack lasts 10 seconds.
                    if (mapped == "Attack") { c.firstFrame = 26f; c.lastFrame = 110f; }
                    else if (mapped == "Death") { c.firstFrame = 26f; c.lastFrame = 93f; }

                    clips[i] = c;
                    Debug.Log("[walker] take '" + c.takeName + "' -> " + mapped +
                              " frames " + c.firstFrame + "-" + c.lastFrame);
                }
                importer.clipAnimations = clips;
            }

            importer.SaveAndReimport();

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.shader = shader;
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[walker] material " + MatPath + " albedo bound: " + (tex != null));

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (go != null)
            {
                var clipsFound = Resources.LoadAll<AnimationClip>("Models");
                Debug.Log("[walker] runtime clips visible: " + clipsFound.Length);
                foreach (var cl in clipsFound) Debug.Log("[walker]   clip " + cl.name + " len " + cl.length);
            }
        }
    }
}

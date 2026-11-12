using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Renders the real walker mesh + real Walk clip driven by WalkerStride, from a low close
    /// camera that keeps both feet in frame, and writes a numbered PNG sequence. This is
    /// editor-rendered rig evidence (not a game capture); the numeric contact proof lives in
    /// WalkerStrideProbe.
    ///
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.WalkerGaitRender.Run -logFile Logs/gaitrender.log
    /// </summary>
    public static class WalkerGaitRender
    {
        const string PrefabPath = "Assets/Resources/Models/walker3d.fbx";
        const string OutDir = "evidence/2026-11-10-gait";
        const float Speed = 2.4f;
        const float ClipSpeed = 0.93f;
        const float Dt = 1f / 60f;
        const int Frames = 60;

        [MenuItem("Train Survivor/Diagnostics/Render Walker Gait")]
        public static void Run()
        {
            try { Render(); }
            catch (Exception e) { Debug.LogError("[gaitrender] failed: " + e); }
            EditorApplication.Exit(0);
        }

        static void Render()
        {
            Directory.CreateDirectory(OutDir);

            var clips = new List<AnimationClip>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
                if (a is AnimationClip c) clips.Add(c);
            var walk = clips.Find(c => c.name == "Walk");

            var host = new GameObject("GaitRender");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var model = UnityEngine.Object.Instantiate(prefab, host.transform);

            var rs = model.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float fit = 4.0f / Mathf.Max(0.01f, b.size.y);
            model.transform.localScale = Vector3.one * fit;
            if (Application.isBatchMode) model.transform.localScale = Vector3.one * fit;

            var anim = model.GetComponent<Animation>() ?? model.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var c in clips) anim.AddClip(c, c.name);
            anim.Play("Walk");
            anim.wrapMode = WrapMode.ClampForever;
            anim["Walk"].speed = 0f;
            anim.Sample();

            Transform F(string n)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name == n || t.name.EndsWith(":" + n)) return t;
                return null;
            }
            var stride = new WalkerStride();
            bool ok = stride.Bind(model.transform, F("Hips"), F("LeftUpLeg"), F("LeftLeg"), F("LeftFoot"),
                                  F("RightUpLeg"), F("RightLeg"), F("RightFoot"));

            // ground so the feet read against something, like the ballast they walk on
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 0.4f;
            ground.GetComponent<Renderer>().sharedMaterial = MakeMat(new Color(0.20f, 0.19f, 0.17f));

            var camGo = new GameObject("GaitCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 46f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.10f, 0.12f);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.6f;
            key.transform.rotation = Quaternion.Euler(42f, 35f, 0f);

            var rt = new RenderTexture(560, 560, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(560, 560, TextureFormat.RGB24, false);

            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            float playback = Speed / ClipSpeed;
            int shot = 0;

            for (int f = 0; f < Frames + 4; f++)
            {
                pos += (rot * Vector3.forward) * (Speed * Dt);
                host.transform.position = pos;
                host.transform.rotation = rot;
                var st = anim["Walk"];
                st.time = Mathf.Repeat(st.time + Dt * playback, walk.length);
                anim.Sample();
                stride.Step(Dt, Speed, pos, rot, true);

                // feet framed from the side and slightly ahead, camera low
                if (f > 3 && f % 3 == 0 && shot < 16)
                {
                    Vector3 look = pos + rot * new Vector3(-0.18f, 0f, 0f) + Vector3.up * 0.62f;
                    cam.transform.position = pos + rot * new Vector3(3.65f, 0.90f, 0.25f);
                    cam.transform.LookAt(look);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 560, 560), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(OutDir, string.Format("gait-{0:000}.png", shot)), tex.EncodeToPNG());
                    shot++;
                }
            }

            Debug.Log(string.Format("[gaitrender] {0} frames written to {1} (bind {2}, stride {3:0.00} m, weight {4:0.00})",
                shot, OutDir, ok, stride.Stride, stride.Weight));
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(ground);
        }

        static Material MakeMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}

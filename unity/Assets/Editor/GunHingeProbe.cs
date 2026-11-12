using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Renders the locomotive turret joint at its permitted extrema so the hinge can be
    /// judged from actual geometry instead of a description.
    ///
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.GunHingeProbe.Run -logFile Logs/hinge.log
    ///
    /// Poses: yaw 0/90/180 x pitch 0/+70 (max depression) x recoil 0/0.16 m. Writes PNGs to
    /// evidence/&lt;date&gt;-hinge/ and logs the mesh gap it can measure analytically.
    /// </summary>
    public static class GunHingeProbe
    {
        const int Size = 900;

        [MenuItem("Train Survivor/Diagnostics/Gun Hinge Probe")]
        public static void Run()
        {
            try { Probe(); }
            catch (Exception e) { Debug.LogError("[hinge] failed: " + e); }
            EditorApplication.Exit(0);
        }

        static void Probe()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../evidence/2026-11-10-hinge"));
            Directory.CreateDirectory(outDir);

            var root = new GameObject("HingeProbe");
            var train = root.AddComponent<TrainRig>();
            train.Build(1, 2);

            var guns = root.GetComponentsInChildren<GunRig>(true);
            Debug.Log("[hinge] guns " + guns.Length);
            if (guns.Length == 0) return;

            var sun = new GameObject("ProbeSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.transform.rotation = Quaternion.Euler(40f, 35f, 0f);

            var camGo = new GameObject("HingeCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.12f, 0.16f);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            foreach (var g in guns) g.enabled = false;

            int shot = 0;
            int[] yaws = { 0, 90, 180 };
            float[] pitches = { 0f, 70f };
            float[] recoils = { 0f, 0.16f };

            foreach (int yaw in yaws)
            {
                foreach (float pitch in pitches)
                {
                    foreach (float recoil in recoils)
                    {
                        var g = guns[0];
                        var mount = g.mount != null ? g.mount : g.transform;
                        Vector3 recoilBase = g.recoil != null ? g.recoil.localPosition : Vector3.zero;

                        if (g.yaw != null) g.yaw.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        if (g.pitch != null) g.pitch.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                        if (g.recoil != null)
                        {
                            Vector3 p = recoilBase;
                            p.z = recoilBase.z - recoil;
                            g.recoil.localPosition = p;
                        }

                        Vector3 hinge = g.pitch != null ? g.pitch.position : mount.position;
                        Quaternion yawRot = g.yaw != null ? g.yaw.rotation : mount.rotation;
                        cam.transform.position = hinge + yawRot * new Vector3(2.0f, 1.05f, -2.0f);
                        cam.transform.LookAt(hinge + yawRot * new Vector3(0f, 0.05f, 0f));

                        cam.Render();
                        RenderTexture.active = rt;
                        var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        tex.Apply();
                        string name = string.Format("gun-yaw{0}-pitch{1:0}-recoil{2:0.00}.png", yaw, pitch, recoil);
                        File.WriteAllBytes(Path.Combine(outDir, name), tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                        RenderTexture.active = null;
                        shot++;
                    }
                }
            }

            // Analytical gap: distance between the base plate top and the upper receiver's
            // lowest point at max depression, in the gun's local frame.
            var gun = guns[0];
            var renders = gun.GetComponentsInChildren<Renderer>();
            Debug.Log(string.Format("[hinge] shots {0} dir {1} renderers {2}", shot, outDir, renders.Length));
            foreach (var r in renders)
                Debug.Log(string.Format("[hinge] renderer {0} bounds {1} shader {2}",
                    r.name, r.bounds.size.ToString("0.00"), r.sharedMaterial != null && r.sharedMaterial.shader != null
                        ? r.sharedMaterial.shader.name : "none"));

            if (gun.pitch != null && gun.recoil != null)
            {
                Debug.Log(string.Format("[hinge] hinge local {0} recoil local {1}",
                    gun.pitch.localPosition.ToString("0.000"), gun.recoil.localPosition.ToString("0.000")));
            }

            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}

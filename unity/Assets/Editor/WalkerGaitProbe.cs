using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Measures the imported walk clip instead of trusting a note. Plays the legacy Walk
    /// take frame by frame on the real prefab (same fit scale the runtime uses), samples both
    /// foot bones in root space, and reports the stance windows, the distance a planted foot
    /// travels per stance, and the ground speed the clip implies at playback 1.
    ///
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.WalkerGaitProbe.Run -logFile Logs/gait.log
    /// </summary>
    public static class WalkerGaitProbe
    {
        const string PrefabPath = "Assets/Resources/Models/walker3d.fbx";
        const float TargetHeight = 4.0f;     // WalkerModelView.TargetHeight

        [MenuItem("Train Survivor/Diagnostics/Walker Gait Probe")]
        public static void Run()
        {
            try { Measure(); }
            catch (Exception e) { Debug.LogError("[gait] probe failed: " + e); }
            EditorApplication.Exit(0);
        }

        static void Measure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[gait] prefab not found at " + PrefabPath); return; }

            var clips = new List<AnimationClip>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
                if (a is AnimationClip c) clips.Add(c);

            AnimationClip walk = clips.Find(c => c.name == "Walk");
            if (walk == null) { Debug.LogError("[gait] no Walk clip in " + PrefabPath); return; }

            var root = new GameObject("GaitProbe");
            var go = UnityEngine.Object.Instantiate(prefab, root.transform);

            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float fit = b.size.y > 0.01f ? TargetHeight / b.size.y : 1f;
            go.transform.localScale = Vector3.one * fit;

            var anim = go.GetComponent<Animation>();
            if (anim == null) anim = go.AddComponent<Animation>();
            anim.playAutomatically = false;
            foreach (var c in clips) anim.AddClip(c, c.name);

            Transform lf = FindBone(go.transform, "LeftFoot");
            Transform rf = FindBone(go.transform, "RightFoot");
            Transform lt = FindBone(go.transform, "LeftToeBase");
            Transform rt = FindBone(go.transform, "RightToeBase");
            if (lf == null || rf == null) { Debug.LogError("[gait] foot bones not found"); return; }

            Debug.Log(string.Format("[gait] prefab bounds height {0:0.000} fit {1:0.000} scale {2:0.000} " +
                                    "clip {3} length {4:0.000}s frameRate {5:0.0} loop {6} " +
                                    "bones L={7} R={8} toeL={9} toeR={10}",
                b.size.y, fit, go.transform.localScale.x, walk.name, walk.length, walk.frameRate,
                walk.wrapMode, Path(lf), Path(rf), lt != null, rt != null));

            anim.Play("Walk");
            anim.wrapMode = WrapMode.ClampForever;

            int frames = Mathf.Max(1, Mathf.RoundToInt(walk.length * 60f));
            var leftZ = new float[frames + 1];
            var rightZ = new float[frames + 1];
            var leftY = new float[frames + 1];
            var rightY = new float[frames + 1];
            var leftX = new float[frames + 1];
            var rightX = new float[frames + 1];

            for (int i = 0; i <= frames; i++)
            {
                float t = walk.length * i / frames;
                var st = anim["Walk"];
                if (st != null) st.time = t;
                anim.Sample();

                Vector3 lp = root.transform.InverseTransformPoint(lf.position);
                Vector3 rp = root.transform.InverseTransformPoint(rf.position);
                leftX[i] = lp.x; leftY[i] = lp.y; leftZ[i] = lp.z;
                rightX[i] = rp.x; rightY[i] = rp.y; rightZ[i] = rp.z;
            }

            // Ground plane = lowest sampled foot height in this clip.
            float floor = Mathf.Min(Min(leftY), Min(rightY));
            Debug.Log(string.Format("[gait] floor {0:0.000} m  yRange L {1:0.000}..{2:0.000}  R {3:0.000}..{4:0.000}",
                floor, Min(leftY), Max(leftY), Min(rightY), Max(rightY)));

            ReportFoot("L", leftX, leftY, leftZ, floor, frames, walk.length, fit);
            ReportFoot("R", rightX, rightY, rightZ, floor, frames, walk.length, fit);

            // Whole-cycle travel of each foot: what the stride would be if the clip had root motion.
            float lTravel = Mathf.Abs(Max(leftZ) - Min(leftZ));
            float rTravel = Mathf.Abs(Max(rightZ) - Min(rightZ));
            Debug.Log(string.Format("[gait] foot Z span L {0:0.000} m R {1:0.000} m (scale {2:0.00})",
                lTravel, rTravel, fit));

            // Step cadence: count the descending zero crossings of the relative foot separation.
            int steps = 0;
            for (int i = 1; i < frames; i++)
            {
                float prev = leftZ[i - 1] - rightZ[i - 1];
                float cur = leftZ[i] - rightZ[i];
                if (prev > 0f && cur <= 0f) steps++;
                if (prev < 0f && cur >= 0f) steps++;
            }
            Debug.Log(string.Format("[gait] relative-Z crossings (steps) {0} over {1:0.000}s -> {2:0.00} steps/s",
                steps, walk.length, steps / Mathf.Max(0.001f, walk.length)));

            UnityEngine.Object.DestroyImmediate(root);
        }

        static void ReportFoot(string tag, float[] x, float[] y, float[] z, float floor,
                               int frames, float length, float fit)
        {
            // Tight contact band: the foot only counts as planted within 2 cm of the clip's
            // lowest point. The loose band below is reported for comparison because a shamble
            // keeps both feet low for much of the cycle.
            float dt = length / frames;
            var tight = new List<float>();
            var loose = new List<float>();
            for (int i = 1; i <= frames; i++)
            {
                float h = y[i] - floor;
                float v = (z[i] - z[i - 1]) / dt;
                if (h <= 0.02f) tight.Add(v);
                if (h <= 0.06f) loose.Add(v);
            }

            var sb = new StringBuilder();
            int runs = 0;
            float totalTravel = 0f, totalTime = 0f;
            bool inStance = false;
            int start = 0;
            for (int i = 0; i <= frames; i++)
            {
                bool on = y[i] - floor <= 0.06f;
                if (on && !inStance) { inStance = true; start = i; }
                if ((!on || i == frames) && inStance)
                {
                    inStance = false;
                    int end = i;
                    if (end - start < 3) continue;
                    float dur = length * (end - start) / frames;
                    float travel = z[end] - z[start];
                    if (travel < 0f) { totalTravel += -travel; totalTime += dur; runs++; }
                    sb.AppendFormat(" {0:0.0}-{1:0.0}s dz={2:0.00}m", length * start / frames,
                                    length * end / frames, travel);
                }
            }
            float avg = totalTime > 0f ? totalTravel / totalTime : 0f;

            // Travel the body must cover per cycle: the integral of the planted foot's
            // backward motion. Summed over both feet and divided by the clip length this is
            // exactly the ground speed the clip is authored for at playback 1.
            float back = 0f, fwd = 0f;
            for (int i = 1; i <= frames; i++)
            {
                float v = (z[i] - z[i - 1]) / dt;
                if (v < 0f) back += -v * dt; else fwd += v * dt;
            }

            Debug.Log(string.Format(
                "[gait] {0}: contact<=2cm n={1} median dz/dt {2:0.000} m/s p25 {3:0.000} p75 {4:0.000} | " +
                "loose<=6cm n={5} median {6:0.000} | backward stance runs {7} {8:0.00}s avg {9:0.000} m/s | " +
                "back-integral {10:0.000}m fwd {11:0.000}m per clip|{12}",
                tag, tight.Count, Median(tight), Percentile(tight, 0.25f), Percentile(tight, 0.75f),
                loose.Count, Median(loose), runs, totalTime, avg, back, fwd, sb.ToString()));
        }

        static float Median(List<float> a) => Percentile(a, 0.5f);

        static float Percentile(List<float> a, float q)
        {
            if (a.Count == 0) return 0f;
            var c = new List<float>(a);
            c.Sort();
            int i = Mathf.Clamp((int)(c.Count * q), 0, c.Count - 1);
            return c[i];
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.EndsWith(":" + name)) return t;
            return null;
        }

        static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        static float Min(float[] a) { float m = float.MaxValue; foreach (var v in a) m = Mathf.Min(m, v); return m; }
        static float Max(float[] a) { float m = float.MinValue; foreach (var v in a) m = Mathf.Max(m, v); return m; }
    }
}

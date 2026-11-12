using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Offline validation of planted-foot correction on the real imported Walk clip.
    ///
    /// The walker root travels at the measured game speed with the clip at the calibrated
    /// rate (0.93 m/s at playback 1, sim 2.4 m/s). A foot counts as in contact when it is
    /// within 8 cm of the clip's lowest point; for those frames the VISIBLE foot's world
    /// velocity is the skate (0 cm/s = planted, 240 cm/s = dragged at body speed).
    ///
    /// Compares:
    ///   baseline    no correction (what ships today)
    ///   offsetX     bounded positional correction of the visual root, slewed, released on lift
    ///   playbackX   bounded clip-rate servo holding the contact foot's speed
    ///
    /// Plus turn and attack interruptions. Numbers only; nothing is enabled at runtime here.
    ///
    ///   Unity.exe -batchmode -quit -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.WalkerGaitValidation.Run -logFile Logs/gait-validate.log
    /// </summary>
    public static class WalkerGaitValidation
    {
        const string PrefabPath = "Assets/Resources/Models/walker3d.fbx";
        const float TargetHeight = 4.0f;
        const float SimSpeed = 2.4f;
        const float ClipSpeed = 0.93f;
        const float Dt = 1f / 60f;
        const float ContactBand = 0.08f;

        [MenuItem("Train Survivor/Diagnostics/Walker Gait Validation")]
        public static void Run()
        {
            try { Validate(); }
            catch (Exception e) { Debug.LogError("[gaitval] failed: " + e); }
            EditorApplication.Exit(0);
        }

        sealed class Rig
        {
            public GameObject Root;
            public Animation Anim;
            public AnimationClip Walk, Attack;
            public Transform FootL, FootR;
            public float Floor;
        }

        static Rig Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var clips = new List<AnimationClip>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
                if (a is AnimationClip c) clips.Add(c);

            var rig = new Rig
            {
                Root = new GameObject("GaitValRoot"),
                Walk = clips.Find(c => c.name == "Walk"),
                Attack = clips.Find(c => c.name == "Attack")
            };
            var model = UnityEngine.Object.Instantiate(prefab, rig.Root.transform);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float fit = b.size.y > 0.01f ? TargetHeight / b.size.y : 1f;
            model.transform.localScale = Vector3.one * fit;

            rig.Anim = model.GetComponent<Animation>();
            if (rig.Anim == null) rig.Anim = model.AddComponent<Animation>();
            rig.Anim.playAutomatically = false;
            foreach (var c in clips) rig.Anim.AddClip(c, c.name);
            rig.FootL = FindBone(model.transform, "LeftFoot");
            rig.FootR = FindBone(model.transform, "RightFoot");

            rig.Anim.Play("Walk");
            rig.Anim.wrapMode = WrapMode.ClampForever;
            rig.Anim["Walk"].time = 0f;
            rig.Anim.Sample();
            rig.Floor = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y);

            Debug.Log(string.Format("[gaitval] clip {0:0.00}s fit {1:0.000} floor {2:0.000}m playback {3:0.000} band {4:0.00}m",
                rig.Walk.length, fit, rig.Floor, SimSpeed / ClipSpeed, ContactBand));
            return rig;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.EndsWith(":" + name)) return t;
            return null;
        }

        static void Validate()
        {
            var rig = Build();
            if (rig.Walk == null || rig.FootL == null || rig.FootR == null)
            {
                Debug.LogError("[gaitval] missing clip or foot bones");
                return;
            }

            Walk(rig, "baseline", Mode.Baseline, 16f, false, false, 0f);
            Walk(rig, "offset10", Mode.Offset, 16f, false, false, 0.10f);
            Walk(rig, "offset20", Mode.Offset, 16f, false, false, 0.20f);
            Walk(rig, "playback10", Mode.Playback, 16f, false, false, 0.10f);
            Walk(rig, "baseline+turn", Mode.Baseline, 9f, true, false, 0f);
            Walk(rig, "offset10+turn", Mode.Offset, 9f, true, false, 0.10f);
            Walk(rig, "baseline+attack", Mode.Baseline, 8f, false, true, 0f);
            Walk(rig, "offset10+attack", Mode.Offset, 8f, false, true, 0.10f);

            UnityEngine.Object.DestroyImmediate(rig.Root);
        }

        enum Mode { Baseline, Offset, Playback }

        static void Walk(Rig rig, string label, Mode mode, float seconds, bool turn, bool attack,
                         float clamp)
        {
            rig.Root.transform.position = Vector3.zero;
            rig.Root.transform.rotation = Quaternion.identity;
            rig.Anim.Play("Walk");
            rig.Anim["Walk"].time = 0f;
            rig.Anim["Walk"].speed = 0f;
            rig.Anim.Sample();

            float baseRate = SimSpeed / ClipSpeed;
            float t = 0f;
            float attackStart = attack ? seconds * 0.45f : float.MaxValue;
            float attackEnd = attackStart + 0.8f;
            bool attackStarted = false, attackFinished = false;

            Vector3 offset = Vector3.zero;
            float maxOffset = 0f, sumOffset = 0f, maxJump = 0f;

            bool stance = false, stanceLeft = false;
            Vector3 touchdownVis = Vector3.zero, touchdownRaw = Vector3.zero;
            int stances = 0, stanceFrames = 0;
            float sumDrift = 0f, maxDrift = 0f;

            int contactFrames = 0;
            var skate = new List<float>(2048);
            Vector3 prevVisL = Vector3.zero, prevVisR = Vector3.zero;
            Vector3 prevRawL = Vector3.zero, prevRawR = Vector3.zero;
            bool havePrev = false;

            while (t < seconds)
            {
                t += Dt;
                if (turn) rig.Root.transform.rotation *= Quaternion.Euler(0f, 60f * Dt, 0f);
                rig.Root.transform.position += rig.Root.transform.forward * (SimSpeed * Dt);

                bool attacking = attack && t >= attackStart && t < attackEnd;
                if (attacking && !attackStarted)
                {
                    attackStarted = true;
                    rig.Anim.Play("Attack");
                    rig.Anim["Attack"].time = 0f;
                    rig.Anim["Attack"].speed = 1f;
                    stance = false;
                }
                else if (attack && !attacking && attackStarted && !attackFinished)
                {
                    attackFinished = true;
                    rig.Anim.Play("Walk");
                    rig.Anim["Walk"].time = 0f;
                    rig.Anim["Walk"].speed = 0f;
                    stance = false;
                }

                if (attacking)
                {
                    var at = rig.Anim["Attack"];
                    at.time = Mathf.Min(rig.Attack.length - 0.001f, at.time + Dt);
                    rig.Anim.Sample();
                }
                else
                {
                    var st = rig.Anim["Walk"];
                    st.time = Mathf.Repeat(st.time + Dt * baseRate, rig.Walk.length);
                    rig.Anim.Sample();
                }

                Vector3 fL = rig.FootL.position;
                Vector3 fR = rig.FootR.position;
                Vector3 visL = fL + offset;
                Vector3 visR = fR + offset;
                Vector3 fwd = rig.Root.transform.forward;

                float hL = fL.y - rig.Root.transform.position.y;
                float hR = fR.y - rig.Root.transform.position.y;
                bool nearL = hL <= rig.Floor + ContactBand;
                bool nearR = hR <= rig.Floor + ContactBand;
                bool left = nearL && (!nearR || hL <= hR);

                // per-frame raw foot motion (must be measured BEFORE the previous sample is
                // overwritten, or every frame reads as zero movement)
                float dLf = Vector3.Dot(fL - prevRawL, fwd);
                float dRf = Vector3.Dot(fR - prevRawR, fwd);

                if (havePrev && !attacking)
                {
                    if (nearL || nearR)
                    {
                        Vector3 cur = left ? visL : visR;
                        Vector3 prev = left ? prevVisL : prevVisR;
                        float v = Vector2.Distance(new Vector2(cur.x, cur.z),
                                                   new Vector2(prev.x, prev.z)) / Dt;
                        skate.Add(v * 100f);
                        contactFrames++;
                    }
                }
                prevVisL = visL;
                prevVisR = visR;
                prevRawL = fL;
                prevRawR = fR;
                havePrev = true;

                // stance window (for drift + correction), requires contact + backward motion
                bool backL = dLf < -0.0005f;
                bool backR = dRf < -0.0005f;
                bool leftStance = !attacking && nearL && backL && hL <= hR;
                bool rightStance = !attacking && nearR && backR && hR < hL;
                bool any = leftStance || rightStance;
                bool stanceFootLeft = leftStance;

                if (any && (!stance || stanceFootLeft != stanceLeft))
                {
                    stance = true;
                    stanceLeft = stanceFootLeft;
                    touchdownVis = stanceFootLeft ? visL : visR;
                    touchdownRaw = stanceFootLeft ? fL : fR;
                    stances++;
                }
                else if (!any)
                {
                    stance = false;
                }

                if (stance)
                {
                    stanceFrames++;
                    Vector3 v = left ? visL : visR;
                    float drift = Vector2.Distance(new Vector2(v.x, v.z),
                                                   new Vector2(touchdownVis.x, touchdownVis.z));
                    sumDrift += drift;
                    maxDrift = Mathf.Max(maxDrift, drift);
                }

                Vector3 wanted = Vector3.zero;
                if (mode == Mode.Offset && stance)
                {
                    Vector3 raw = left ? fL : fR;
                    Vector3 err = new Vector3(raw.x - touchdownRaw.x, 0f, raw.z - touchdownRaw.z);
                    wanted = Vector3.ClampMagnitude(-err, clamp);
                }

                if (mode == Mode.Offset)
                {
                    Vector3 next = Vector3.MoveTowards(offset, wanted, 4.0f * Dt);
                    maxJump = Mathf.Max(maxJump, (next - offset).magnitude);
                    offset = next;
                }
                else if (mode == Mode.Playback && stance)
                {
                    float d = Mathf.Abs(Vector3.Dot((left ? fL : fR) - (left ? prevRawL : prevRawR), fwd));
                    float vFoot = d / Dt;
                    if (vFoot > 0.01f)
                    {
                        float ratio = Mathf.Clamp(SimSpeed / vFoot, 1f - clamp, 1f + clamp);
                        baseRate = Mathf.Clamp(baseRate * Mathf.Lerp(1f, ratio, 0.08f), 0.4f, 6f);
                    }
                }

                sumOffset += offset.magnitude;
                maxOffset = Mathf.Max(maxOffset, offset.magnitude);
            }

            skate.Sort();
            float meanSkate = 0f;
            for (int i = 0; i < skate.Count; i++) meanSkate += skate[i];
            if (skate.Count > 0) meanSkate /= skate.Count;
            float p95 = skate.Count > 0 ? skate[Mathf.Clamp((int)(skate.Count * 0.95f), 0, skate.Count - 1)] : 0f;
            float maxSkate = skate.Count > 0 ? skate[skate.Count - 1] : 0f;

            Debug.Log(string.Format(
                "[gaitval] {0,-16} contactF {1,4} skate mean {2,6:0.0} p95 {3,6:0.0} max {4,6:0.0}cm/s | " +
                "stances {5,3} driftMean {6,5:0.0} driftMax {7,5:0.0}cm | offsetAvg {8,4:0.0} max {9,5:0.0} jump {10:0.00}cm rate {11:0.00}",
                label, contactFrames, meanSkate, p95, maxSkate,
                stances, stanceFrames > 0 ? sumDrift / stanceFrames * 100f : 0f, maxDrift * 100f,
                stanceFrames > 0 ? sumOffset / Mathf.Max(1, stanceFrames) * 100f : 0f,
                maxOffset * 100f, maxJump * 100f, baseRate));
        }
    }
}

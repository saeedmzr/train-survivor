using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Validates the procedural Walker stride on the real prefab/clip, with the same solver the
    /// game uses. The support foot is defined by the solver's contact phase; ground contact is
    /// then checked independently from the foot's height, so "contact" cannot be faked by the
    /// state being measured.
    ///
    /// Reports per run: stance count, support-foot world displacement per stance (mean/max),
    /// share of contact frames with the foot actually near the floor, per-frame foot jumps,
    /// knee-direction violations, reach releases. Baseline (clip only) is measured the same way.
    ///
    ///   Unity.exe -batchmode -quit -nographics -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.WalkerStrideProbe.Run -logFile Logs/stride.log
    /// </summary>
    public static class WalkerStrideProbe
    {
        const string PrefabPath = "Assets/Resources/Models/walker3d.fbx";
        const float TargetHeight = 4.0f;
        const float Speed = 2.4f;
        const float ClipSpeed = 0.93f;
        const float Dt = 1f / 60f;
        const float GroundBand = 0.03f;

        [MenuItem("Train Survivor/Diagnostics/Walker Stride Probe")]
        public static void Run()
        {
            try { Validate(); }
            catch (Exception e) { Debug.LogError("[stride] failed: " + e); }
            EditorApplication.Exit(0);
        }

        sealed class Rig
        {
            public GameObject Root;
            public Animation Anim;
            public AnimationClip Walk, Attack, Death;
            public Transform FootL, FootR, LegL, LegR, UpLegL, UpLegR, Hips, Spine, Chest;
            public WalkerStride Stride;
            public float Floor;
            public float Fit;
        }

        static Rig Build()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var clips = new List<AnimationClip>();
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(PrefabPath))
                if (a is AnimationClip c) clips.Add(c);

            var rig = new Rig
            {
                Root = new GameObject("StrideProbeRoot"),
                Walk = clips.Find(c => c.name == "Walk"),
                Attack = clips.Find(c => c.name == "Attack"),
                Death = clips.Find(c => c.name == "Death")
            };
            var model = UnityEngine.Object.Instantiate(prefab, rig.Root.transform);
            var renderers = model.GetComponentsInChildren<Renderer>();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            rig.Fit = b.size.y > 0.01f ? TargetHeight / b.size.y : 1f;
            model.transform.localScale = Vector3.one * rig.Fit;

            rig.Anim = model.GetComponent<Animation>();
            if (rig.Anim == null) rig.Anim = model.AddComponent<Animation>();
            rig.Anim.playAutomatically = false;
            foreach (var c in clips) rig.Anim.AddClip(c, c.name);

            rig.Hips = FindBone(model.transform, "Hips");
            rig.Spine = FindBone(model.transform, "Spine");
            rig.Chest = FindBone(model.transform, "Spine1");
            rig.UpLegL = FindBone(model.transform, "LeftUpLeg");
            rig.LegL = FindBone(model.transform, "LeftLeg");
            rig.FootL = FindBone(model.transform, "LeftFoot");
            rig.UpLegR = FindBone(model.transform, "RightUpLeg");
            rig.LegR = FindBone(model.transform, "RightLeg");
            rig.FootR = FindBone(model.transform, "RightFoot");

            rig.Anim.Play("Walk");
            rig.Anim.wrapMode = WrapMode.ClampForever;
            rig.Anim["Walk"].time = 0f;
            rig.Anim.Sample();
            rig.Floor = Mathf.Min(rig.FootL.position.y, rig.FootR.position.y);

            rig.Stride = new WalkerStride();
            bool ok = rig.Stride.Bind(model.transform, rig.Hips, rig.UpLegL, rig.LegL, rig.FootL,
                                      rig.UpLegR, rig.LegR, rig.FootR);
            Debug.Log(string.Format(
                "[stride] clip {0:0.00}s fit {1:0.000} floor {2:0.000}m thigh {3:0.000} shin {4:0.000} leg {5:0.000} " +
                "ankle {6:0.000} footHalf {7:0.000} bind {8} hipY {9:0.000} hipFootDz {10:0.000}",
                rig.Walk.length, rig.Fit, rig.Floor, rig.Stride.Thigh, rig.Stride.Shin,
                rig.Stride.LegLength, rig.Stride.AnkleHeight, rig.Stride.FootHalfWidth, ok,
                rig.Hips.position.y, rig.Hips.position.z - rig.FootL.position.z));
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
            if (!rig.Stride.Ready) { Debug.LogError("[stride] not ready"); return; }

            Run(rig, "base fwd", new Vector3(0, 0, 1), 8f, false, 0f);
            Run(rig, "base south", new Vector3(0, 0, 1), 6f, false, 0f, 0f, false, 180f);
            Run(rig, "stride fwd", new Vector3(0, 0, 1), 8f, true, 0f);
            Run(rig, "stride east", new Vector3(0, 0, 1), 6f, true, 0f, 0f, false, 90f);
            Run(rig, "stride south", new Vector3(0, 0, 1), 6f, true, 0f, 0f, false, 180f);
            Run(rig, "stride turn", new Vector3(0, 0, 1), 6f, true, 60f);
            Run(rig, "stride stop", new Vector3(0, 0, 1), 6f, true, 0f, 4f);
            Run(rig, "stride attack", new Vector3(0, 0, 1), 6f, true, 0f, 0f, true);

            UnityEngine.Object.DestroyImmediate(rig.Root);
        }

        static void Run(Rig rig, string label, Vector3 dir, float seconds, bool useStride,
                        float turnRate, float stopAt = 0f, bool attackRun = false, float heading = 0f)
        {
            var stride = rig.Stride;
            stride.Reset();
            stride.Stride = 0.72f;

            rig.Root.transform.position = Vector3.zero;
            rig.Root.transform.rotation = Quaternion.Euler(0f, heading, 0f);
            rig.Anim.Play("Walk");
            rig.Anim["Walk"].time = 0f;
            rig.Anim["Walk"].speed = 0f;
            rig.Anim.Sample();

            dir.Normalize();
            float playback = Speed / ClipSpeed;

            bool contact = false;
            int foot = 0;
            Vector3 start = Vector3.zero;
            float stanceDrift = 0f, stanceMax = 0f;
            int stances = 0, stanceFrames = 0, onGroundFrames = 0;
            int framesThisStance = 0;
            float stanceFramesSum = 0f;
            int snaps = 0, kneeBad = 0, reachBad = 0;
            float kneeMin = 99f;
            Vector3 prevFoot = Vector3.zero;
            bool havePrev = false;
            int chestMoved = 0;
            float releaseDeg = -1f;

            bool baseContact = false;
            Vector3 baseStart = Vector3.zero;
            float baseDrift = 0f, baseMax = 0f;
            int baseStances = 0, baseFrames = 0;

            float t = 0f;
            bool stopLatched = false;

            while (t < seconds)
            {
                t += Dt;

                bool attacking = attackRun && t >= 3f && t < 4.6f;
                bool walking = !attacking && !(stopAt > 0f && t >= stopAt);
                if (stopAt > 0f && !walking && !stopLatched) stopLatched = true;

                if (turnRate > 0f) rig.Root.transform.rotation *= Quaternion.Euler(0f, turnRate * Dt, 0f);
                rig.Root.transform.position += (rig.Root.transform.rotation * dir) * (Speed * Dt);

                if (attacking)
                {
                    var at = rig.Anim["Attack"];
                    at.time = Mathf.Min(rig.Attack.length - 0.001f, at.time + Dt);
                    rig.Anim.Sample();
                }
                else
                {
                    var st = rig.Anim["Walk"];
                    st.time = Mathf.Repeat(st.time + Dt * playback, rig.Walk.length);
                    rig.Anim.Sample();
                }

                Quaternion chestBefore = rig.Chest != null ? rig.Chest.rotation : Quaternion.identity;

                if (useStride)
                    stride.Step(Dt, Speed, rig.Root.transform.position, rig.Root.transform.rotation, walking);

                if (useStride && !walking && stride.Weight <= 0.001f)
                {
                    Quaternion a0 = rig.UpLegL.localRotation, b0 = rig.LegL.localRotation, c0 = rig.FootL.localRotation;
                    stride.Step(Dt, 0f, rig.Root.transform.position, rig.Root.transform.rotation, false);
                    releaseDeg = Mathf.Max(releaseDeg,
                        Mathf.Max(Quaternion.Angle(a0, rig.UpLegL.localRotation),
                        Mathf.Max(Quaternion.Angle(b0, rig.LegL.localRotation),
                                  Quaternion.Angle(c0, rig.FootL.localRotation))));
                }

                if (rig.Chest != null && Quaternion.Angle(chestBefore, rig.Chest.rotation) > 0.01f) chestMoved++;

                Vector3 rootPos = rig.Root.transform.position;
                float hL = rig.FootL.position.y - rootPos.y;
                float hR = rig.FootR.position.y - rootPos.y;

                if (useStride && walking)
                {
                    bool nowContact = stride.LeftContact || stride.RightContact;
                    int nowFoot = stride.LeftContact ? 0 : 1;
                    Vector3 footPos = nowFoot == 0 ? rig.FootL.position : rig.FootR.position;

                    if (nowContact && (!contact || nowFoot != foot))
                    {
                        contact = true;
                        foot = nowFoot;
                        start = footPos;
                        stanceDrift = 0f;
                        stanceMax = 0f;
                        stances++;
                        framesThisStance = 0;
                        havePrev = false;
                    }
                    else if (!nowContact)
                    {
                        contact = false;
                    }

                    if (contact)
                    {
                        stanceFrames++;
                        framesThisStance++;
                        float d = Vector2.Distance(new Vector2(footPos.x, footPos.z),
                                                   new Vector2(start.x, start.z));
                        stanceDrift += d;
                        stanceMax = Mathf.Max(stanceMax, d);
                        float h = footPos.y - rootPos.y;
                        if (h <= stride.AnkleHeight + GroundBand) onGroundFrames++;
                        Transform up = nowFoot == 0 ? rig.UpLegL : rig.UpLegR;
                        Transform leg = nowFoot == 0 ? rig.LegL : rig.LegR;
                        Vector3 mid = (up.position + footPos) * 0.5f;
                        float side = Vector3.Dot(leg.position - mid, rig.Root.transform.forward);
                        if (side < -0.005f) kneeBad++;
                        kneeMin = Mathf.Min(kneeMin, side);
                    }

                    if (havePrev && nowContact && contact && foot == nowFoot &&
                        stride.Weight > 0.99f && framesThisStance > 2)
                    {
                        float step = Vector2.Distance(new Vector2(footPos.x, footPos.z),
                                                      new Vector2(prevFoot.x, prevFoot.z));
                        if (step > 0.08f)
                        {
                            snaps++;
                            if (snaps <= 5)
                                Debug.Log(string.Format(
                                    "[stride]   snap t={0:0.000} foot={1} step={2:0.00}cm toAnchor={3:0.00}cm w={4:0.00} sf={5} phaseAnch={6}",
                                    t, nowFoot, step * 100f,
                                    Vector3.Distance(footPos, nowFoot == 0 ? stride.AnchorL : stride.AnchorR) * 100f,
                                    stride.Weight, stanceFrames,
                                    Vector3.Distance(stride.AnchorL, stride.AnchorR) * 100f));
                        }
                    }
                    prevFoot = footPos;
                    havePrev = true;
                    reachBad = Mathf.Max(reachBad, stride.ReleasedByReach);
                    stanceFramesSum = stanceFramesSum;
                }
                else if (!useStride && !attacking)
                {
                    float floorRel = Mathf.Min(hL, hR);
                    int lower = hL <= hR ? 0 : 1;
                    bool nowContact = floorRel <= rig.Floor + GroundBand;
                    Vector3 footPos = lower == 0 ? rig.FootL.position : rig.FootR.position;
                    if (nowContact && (!baseContact || lower != foot))
                    {
                        baseContact = true;
                        foot = lower;
                        baseStart = footPos;
                        baseStances++;
                    }
                    else if (!nowContact)
                    {
                        baseContact = false;
                    }
                    if (baseContact)
                    {
                        baseFrames++;
                        float d = Vector2.Distance(new Vector2(footPos.x, footPos.z),
                                                   new Vector2(baseStart.x, baseStart.z));
                        baseDrift += d;
                        baseMax = Mathf.Max(baseMax, d);
                    }
                }
            }


            if (useStride)
            {
                float mean = stanceFrames > 0 ? stanceDrift / stanceFrames : 0f;
                float onGround = stanceFrames > 0 ? 100f * onGroundFrames / stanceFrames : 0f;
                Debug.Log(string.Format(
                    "[stride] {0,-13} stances {1,3} contactF {2,4} driftMean {3,5:0.0}cm driftMax {4,5:0.0}cm " +
                    "onGround {5,5:0.0}% snaps {6,3} kneeFlip {7,3} (min {11:0.00}) reachRel {8,3} chestMove {9,3} release {10} stanceAvg {12:0.00}s",
                    label, stances, stanceFrames, mean * 100f, stanceMax * 100f, onGround, snaps, kneeBad,
                    reachBad, chestMoved, releaseDeg < 0f ? "n/a" : releaseDeg.ToString("0.00") + "deg",
                    kneeMin, stances > 0 ? (stanceFrames / 60f) / stances : 0f));
            }
            else
            {
                float mean = baseFrames > 0 ? baseDrift / baseFrames : 0f;
                Debug.Log(string.Format(
                    "[stride] {0,-13} stances {1,3} contactF {2,4} driftMean {3,5:0.0}cm driftMax {4,5:0.0}cm " +
                    "onGround   n/a snaps n/a kneeFlip n/a reachRel n/a chestMove {5,3}",
                    label, baseStances, baseFrames, mean * 100f, baseMax * 100f, chestMoved));
            }
        }
    }
}

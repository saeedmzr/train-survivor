using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Runs the real game scene in Play Mode inside a windowless batchmode editor, injects a
    /// normal one-thumb control stream through the Input System (boost hold / release), captures
    /// the gameplay camera to PNG at a fixed rate and samples the live WalkerModelView pool
    /// (stride weight, contacts, playing clip) so the walker solve is observed in the actual game
    /// loop — no visible window, no OS input, no offline rig.
    ///
    ///   Unity.exe -batchmode -projectPath unity -executeMethod
    ///             TrainSurvivor.EditorTools.PlayModeProbe.Run -logFile Logs/pmprobe.log
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeProbe
    {
        const string ScenePath = "Assets/_Project/Scenes/Run.unity";
        const string Key = "TrainSurvivor.PlayModeProbe";
        static readonly string OutDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "evidence", "2026-11-10-playmode"));
        const int CapW = 480, CapH = 854;
        const float Seconds = 22f;
        const float CaptureHz = 6f;

        static Camera _cam;
        static Camera _wcam;
        static RenderTexture _rt;
        static Texture2D _tex;
        static Mouse _mouse;
        static float _start, _nextCapture, _nextSample;
        static int _shots, _frames;
        static float _frameSum; static readonly List<float> _frameTimes = new List<float>();
        static int _maxWeightBodies, _contactTotal, _deathSeen, _attackSeen, _weightZeroWhileWalking;
        static string _dir;
        static WalkerRefs _refs;

        sealed class WalkerRefs
        {
            public object View;
            public FieldInfo PoolField;
            public IList Pool;
            public Type BodyType;
            public FieldInfo StrideF, PlayingF, EnemyIdF, GaitF;
            public Type StrideType;
            public PropertyInfo WeightF;
            public FieldInfo ContactsF, ReleasedF;
        }

        static PlayModeProbe()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (Time.realtimeSinceStartup > 90f) { Debug.LogError("[pmprobe] play mode never started"); EditorApplication.Exit(2); }
                return;
            }
            if (_start <= 0f) Begin();

            float t = Time.realtimeSinceStartup - _start;
            _frames++;
            _frameTimes.Add(Time.deltaTime);

            DriveInput(t);
            Sample(t);

            if (t >= _nextCapture)
            {
                _nextCapture = t + 1f / CaptureHz;
                Capture();
            }

            if (t > Seconds) Finish();
        }

        static void Begin()
        {
            _start = Time.realtimeSinceStartup;
            _nextCapture = _nextSample = 0f;
            _frameTimes.Clear();

            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (c.name.Contains("Walker")) { _wcam = c; continue; }   // diagnostic close-up cam
                if (_cam == null || c.depth > _cam.depth) _cam = c;
            }
            if (_cam == null) { Debug.LogError("[pmprobe] no camera"); Finish(); return; }
            _cam.aspect = (float)CapW / CapH;

            _rt = new RenderTexture(CapW, CapH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            _tex = new Texture2D(CapW, CapH, TextureFormat.RGB24, false);

            _mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
            _refs = BindWalkerRefs();
            Debug.Log(string.Format("[pmprobe] begin cam={0} fov={1:0.0} screen={2}x{3} walkers={4}",
                _cam.name, _cam.fieldOfView, Screen.width, Screen.height, _refs?.Pool?.Count ?? -1));
        }

        static WalkerRefs BindWalkerRefs()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.WalkerModelView>();
            if (view == null)
            {
                var inst = typeof(TrainSurvivor.Game.View.WalkerModelView)
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                view = inst?.GetValue(null) as TrainSurvivor.Game.View.WalkerModelView;
            }
            if (view == null)
            {
                foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (mb is TrainSurvivor.Game.View.WalkerModelView w) { view = w; break; }
            }
            if (view == null) return null;
            var refs = new WalkerRefs { View = view };
            refs.PoolField = view.GetType().GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance);
            refs.Pool = refs.PoolField?.GetValue(view) as IList;
            Debug.Log(string.Format("[pmprobe] bound view {0} field {1} pool {2}",
                view.GetType().FullName, refs.PoolField != null, refs.Pool?.Count ?? -1));
            if (refs.Pool == null || refs.Pool.Count == 0) return refs;
            refs.BodyType = refs.Pool[0].GetType();
            Type bt = refs.BodyType;
            refs.StrideF = bt.GetField("Stride", BindingFlags.Public | BindingFlags.Instance);
            refs.PlayingF = bt.GetField("Playing", BindingFlags.Public | BindingFlags.Instance);
            refs.EnemyIdF = bt.GetField("EnemyId", BindingFlags.Public | BindingFlags.Instance);
            refs.GaitF = bt.GetField("Gait", BindingFlags.Public | BindingFlags.Instance);
            refs.StrideType = refs.StrideF?.FieldType;
            if (refs.StrideType != null)
            {
                refs.WeightF = refs.StrideType.GetProperty("Weight");
                refs.ContactsF = refs.StrideType.GetField("Contacts", BindingFlags.Public | BindingFlags.Instance);
                refs.ReleasedF = refs.StrideType.GetField("ReleasedByReach", BindingFlags.Public | BindingFlags.Instance);
            }
            return refs;
        }

        // one thumb: hold = boost, release = brake + fire. Real game, normal control pattern.
        static void DriveInput(float t)
        {
            if (_mouse == null) return;
            bool held = (t % 9f) < 3.2f;               // 3.2 s boost, 5.8 s release/fire
            float x = CapW * 0.5f;
            var state = new MouseState { position = new Vector2(x, CapH * 0.75f) };
            if (held) state = state.WithButton(MouseButton.Left);
            InputState.Change(_mouse, state);
        }

        static void Sample(float t)
        {
            if (_refs?.Pool == null || _refs.Pool.Count == 0) { _refs = BindWalkerRefs(); if (_refs?.Pool == null) return; }
            int wb = 0, contacts = 0, deaths = 0, attacks = 0, live = 0;
            int maxW = 0, released = 0;
            for (int i = 0; i < _refs.Pool.Count; i++)
            {
                object b = _refs.Pool[i];
                if (b == null) continue;
                int id = (int)(_refs.EnemyIdF?.GetValue(b) ?? -1);
                string playing = _refs.PlayingF?.GetValue(b) as string;
                if (playing == "Death") deaths++;
                if (playing == "Attack") attacks++;
                if (playing == "Walk") live++;
                var st = _refs.StrideF?.GetValue(b);
                if (st == null) continue;
                int weight = Mathf.RoundToInt((float)(_refs.WeightF?.GetValue(st) ?? 0f) * 100f);
                if (weight > 90) wb++;
                if (weight > maxW) maxW = weight;
                contacts = Mathf.Max(contacts, (int)(_refs.ContactsF?.GetValue(st) ?? 0));
                released = Mathf.Max(released, (int)(_refs.ReleasedF?.GetValue(st) ?? 0));
                if (weight <= 2 && playing == "Walk" && (fieldGate(_refs.GaitF, b) > 0.4f)) _weightZeroWhileWalking++;
            }
            _maxWeightBodies = Mathf.Max(_maxWeightBodies, wb);
            _contactTotal = Mathf.Max(_contactTotal, contacts);
            _attackSeen = Mathf.Max(_attackSeen, attacks);
            _deathSeen = Mathf.Max(_deathSeen, deaths);

            if (t >= _nextSample)
            {
                _nextSample = t + 1f;
                Debug.Log(string.Format(
                    "[pmprobe] t={0,4:0.0}s walk={1} strideW>90%={2} maxW={3} contacts={4} reachRel={5} attack={6} death={7}",
                    t, live, wb, maxW, contacts, released, attacks, deaths));
            }
        }

        static float fieldGate(FieldInfo f, object b) => f == null ? 0f : (float)f.GetValue(b);

        static void Capture()
        {
            if (_cam == null || _rt == null) return;
            _cam.targetTexture = _rt;
            _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            _dir = _dir ?? Path.Combine(OutDir, "frames");
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(Path.Combine(_dir, string.Format("pm-{0:0000}.png", _shots)), _tex.EncodeToPNG());
            _shots++;

            if (_wcam != null && _wcam.gameObject.activeInHierarchy)
            {
                _wcam.targetTexture = _rt;
                _wcam.aspect = (float)CapW / CapH;
                _wcam.Render();
                RenderTexture.active = _rt;
                _tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0);
                _tex.Apply();
                RenderTexture.active = null;
                _wcam.targetTexture = null;
                var wd = Path.Combine(OutDir, "frames-walker");
                Directory.CreateDirectory(wd);
                File.WriteAllBytes(Path.Combine(wd, string.Format("wk-{0:0000}.png", _shots)), _tex.EncodeToPNG());
            }
        }

        static void Finish()
        {
            var sorted = new List<float>(_frameTimes);
            sorted.Sort();
            float mean = 0f;
            for (int i = 0; i < sorted.Count; i++) mean += sorted[i];
            mean = sorted.Count > 0 ? mean / sorted.Count : 0f;
            float P(float q) => sorted.Count > 0 ? sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)] * 1000f : 0f;

            Debug.Log(string.Format(
                "[pmprobe] summary seconds~{0:0.0} frames={1} shots={2} fpsMean={3:0.0} frameMean={4:0.0}ms p95={5:0.0}ms p99={6:0.0}ms worst={7:0.0}ms " +
                "strideBodiesMax={8} contactsMax={9} attacksSeen={10} deathsSeen={11} weightZeroWhileWalking={12}",
                Seconds, _frames, _shots, _frames / Mathf.Max(0.001f, Seconds), mean * 1000f, P(0.95f), P(0.99f),
                P(1f), _maxWeightBodies, _contactTotal, _attackSeen, _deathSeen, _weightZeroWhileWalking));

            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}

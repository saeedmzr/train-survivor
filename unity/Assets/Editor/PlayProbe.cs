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
using TrainSurvivor.Game;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Full-run driver for the real game scene in windowless Play Mode: injects ordinary
    /// one-thumb hold/release input (plus a centre tap to take upgrade cards), logs the live
    /// World state once a second, and optionally captures the gameplay camera to PNG.
    /// Two entry points so a metrics run is never polluted by capture overhead.
    ///
    ///   -executeMethod TrainSurvivor.EditorTools.PlayProbe.RunLong    (metrics, no capture)
    ///   -executeMethod TrainSurvivor.EditorTools.PlayProbe.RunClip    (capture + audio tap)
    /// </summary>
    [InitializeOnLoad]
    public static class PlayProbe
    {
        const string ScenePath = "Assets/_Project/Scenes/Run.unity";
        const string Key = "TrainSurvivor.PlayProbe";
        static readonly string Root = ResolveRoot();
        static string ResolveRoot()
        {
            string env = Environment.GetEnvironmentVariable("TS_EVIDENCE_ROOT");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "evidence",
                "2026-11-11-deepseek-convoy-fit", "play"));
        }
        static int CapW = 540, CapH = 960;

        // mode
        static bool _capture;
        static float _seconds;
        static float _captureHz;
        static int _stopStage;

        static Camera _cam;
        static RenderTexture _rt;
        static Texture2D _tex;
        static Mouse _mouse;
        static GameLoop _loop;
        static float _start, _nextCapture, _nextSample, _nextTap;
        static int _shots, _frames, _lastFrame = -1;
        static readonly List<float> _ft = new List<float>();
        static string _dir;
        static int _maxLiving, _maxStrideBodies, _lastContacts, _maxReachRel, _maxStage, _maxLevel;
        static bool _stage2Logged, _offerSeen, _deathSeen, _attackSeen, _attackReleaseLogged;
        static int _attackWeighted, _attackReleased;
        static float _stage2At = -1f;
        static WalkerRefs _refs;
        static float _lastHp = -1f;

        sealed class WalkerRefs
        {
            public IList Pool; public Type BodyType;
            public FieldInfo StrideF, PlayingF;
            public Type StrideType; public PropertyInfo WeightF; public FieldInfo ContactsF, ReleasedF;
        }

        static PlayProbe() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void RunLong() { Start("long"); }
        public static void RunOfferMeasure() { Start("offermeasure"); }
        public static void RunCoverage() { Start("coverage"); }
        public static void RunConvoyFit() { Start("convoyfit"); }
        public static void RunClip() { Start("clip"); }
        public static void RunClipShort() { Start("clipshort"); }
        public static void RunBench() { Start("bench"); }

        static void Start(string mode)
        {
            // statics do not survive the play-mode domain reload: keep the mode in SessionState
            SessionState.SetString(Key + ".mode", mode);
            ApplyMode(mode);
            Directory.CreateDirectory(Root);
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void ApplyMode(string mode)
        {
            if (mode == "clip") { _capture = true; _seconds = 145f; _captureHz = 4f; _stopStage = 3; }
            else if (mode == "clipshort") { _capture = true; _seconds = 18f; _captureHz = 6f; _stopStage = 99; }
            else if (mode == "convoyfit") { _capture = true; _seconds = 30f; _stopStage = 99; }
            else if (mode == "offermeasure") { _capture = false; _seconds = 18f; _stopStage = 99; }
            else if (mode == "bench") { _capture = false; _seconds = 30f; _stopStage = 99; }
            else { _capture = false; _seconds = 210f; _stopStage = 3; }
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (Time.realtimeSinceStartup > 120f) { Debug.LogError("[play] play mode never started"); EditorApplication.Exit(2); }
                return;
            }
            if (_start <= 0f) Begin();

            float t = Time.realtimeSinceStartup - _start;
            // count real rendered frames, not editor ticks: batchmode calls update many times
            // per frame, which inflated earlier "frame" totals by orders of magnitude
            int fc = Time.frameCount;
            if (fc != _lastFrame) { _frames++; _ft.Add(Time.deltaTime); _lastFrame = fc; }

            if (SessionState.GetString(Key + ".mode", "long") == "coverage")
            {
                if (t > 1.5f) { Coverage(); Finish("coverage"); }
                return;
            }
            if (SessionState.GetString(Key + ".mode", "long") == "convoyfit")
            {
                if (t > 1.5f) { ConvoyFit(); Finish("convoyfit"); }
                return;
            }
            Drive(t);
            Sample(t);
            if (_capture && t >= _nextCapture) { _nextCapture = t + 1f / _captureHz; Capture(); }

            if (t > _seconds) Finish("time");
        }

        static void Begin()
        {
            ApplyMode(SessionState.GetString(Key + ".mode", "long"));
            _start = Time.realtimeSinceStartup;
            _nextSample = _nextCapture = 0f;
            _nextTap = 1.5f;
            _ft.Clear();

            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (!c.name.Contains("Walker") && (_cam == null || c.depth > _cam.depth)) _cam = c;
            if (_cam != null) _cam.aspect = (float)CapW / CapH;

            if (_capture)
            {
                _rt = new RenderTexture(CapW, CapH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                _tex = new Texture2D(CapW, CapH, TextureFormat.RGB24, false);
            }

            // a batchmode editor has no focused Game view; without this the Input System drops
            // every device event and the game never sees the thumb at all
            try
            {
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            }
            catch (Exception e) { Debug.LogWarning("[play] input settings: " + e.Message); }

            // the HUD is a ScreenSpaceOverlay canvas, which a target-texture camera cannot see;
            // switch it to camera space on the capture camera so stills include the real HUD.
            if (_capture && _cam != null)
            {
                foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (cv.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    cv.renderMode = RenderMode.ScreenSpaceCamera;
                    cv.worldCamera = _cam;
                    cv.planeDistance = 1f;
                }
            }

            _mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
            _loop = UnityEngine.Object.FindFirstObjectByType<GameLoop>();
            _refs = BindWalkers();
            _loop.SimEventRaised += ev =>
            {
                if (ev.Kind == TrainSurvivor.Core.SimEventKind.EnemyHit) _lastBulletEnemy = ev.EnemyId;
                if (ev.Kind == TrainSurvivor.Core.SimEventKind.Shot) _bulletShots++;
                if (ev.Kind == TrainSurvivor.Core.SimEventKind.Kill && ev.EnemyId == _lastBulletEnemy)
                {
                    _bulletKills++;
                    if (_bulletKills <= 5) Debug.Log($"[play] turret kill enemy={ev.EnemyId} shots={_bulletShots} viewDriven={_loop.World.ViewDrivenProjectiles}");
                }
            };
            Debug.Log($"[play] controls demoKills={_loop.World.DemoKills} paused={_loop.Paused} timeScale={_loop.TimeScale} wagons={_loop.World.Train.Wagons}");
            Debug.Log(string.Format("[play] begin capture={0} cam={1} fov={2:0.0} screen={3}x{4} loop={5} pool={6}",
                _capture, _cam?.name, _cam?.fieldOfView ?? 0f, Screen.width, Screen.height, _loop != null, _refs?.Pool?.Count ?? -1));
        }

        static WalkerRefs BindWalkers()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.WalkerModelView>();
            if (view == null) return null;
            var pf = view.GetType().GetField("_pool", BindingFlags.NonPublic | BindingFlags.Instance);
            var pool = pf?.GetValue(view) as IList;
            if (pool == null || pool.Count == 0) return null;
            var r = new WalkerRefs { Pool = pool, BodyType = pool[0].GetType() };
            r.StrideF = r.BodyType.GetField("Stride", BindingFlags.Public | BindingFlags.Instance);
            r.PlayingF = r.BodyType.GetField("Playing", BindingFlags.Public | BindingFlags.Instance);
            r.StrideType = r.StrideF?.FieldType;
            if (r.StrideType != null)
            {
                r.WeightF = r.StrideType.GetProperty("Weight");
                r.ContactsF = r.StrideType.GetField("Contacts", BindingFlags.Public | BindingFlags.Instance);
                r.ReleasedF = r.StrideType.GetField("ReleasedByReach", BindingFlags.Public | BindingFlags.Instance);
            }
            return r;
        }

        // ---- ordinary one-thumb controls -------------------------------------
        static float _tapUntil = -1f;
        static void Drive(float t)
        {
            if (_mouse == null) return;
            float phase = t % 5.2f;
            bool held = phase < 2.4f;                 // 2.4 s boost, 2.8 s brake/fire

            if (t >= _nextTap) { _nextTap = t + 0.9f; _tapUntil = t + 0.14f; }   // a real 140 ms tap
            bool tapNow = t < _tapUntil;
            // Let the real offer delay and camera ease finish before any ordinary click.
            if (_loop?.World != null && _loop.World.OfferPending)
            {
                held = false;
                tapNow = _offerAt > 0f && t > _offerAt + 4.5f && tapNow;
            }

            Vector2 pos = tapNow ? CardScreenPos() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var st = new MouseState { position = pos };
            if (held || tapNow) st = st.WithButton(MouseButton.Left);
            // queue, never force an update: wasPressedThisFrame/wasReleasedThisFrame are what
            // the HUD taps on, and only the frame's own input update sets them
            InputSystem.QueueStateEvent(_mouse, st);
        }

        // ---- live state ------------------------------------------------------
        static bool _shotStart, _shotOffer, _shotLate, _shotStage2;
        static float _offerAt;
        static int _lastBulletEnemy = -1, _bulletKills, _bulletShots;
        static bool _wasOffer;
        static int[] _beforeStacks;
        static string _beforeStats;
        static float _resumeS;
        static bool _awaitResume, _clickedShot;
        static string Stats(TrainSurvivor.Core.TrainState tr) =>
            $"damage={tr.GunDamageMultiplier:F3} fire={tr.FireRateMultiplier:F3} range={tr.GunRangeMultiplier:F3} aim={tr.AimSpeedMultiplier:F3} hp={tr.Hp:F3}/{tr.MaxHp:F3} taken={tr.DamageTakenMultiplier:F3} move={tr.MoveSpeedMultiplier:F3} xp={tr.XpGainMultiplier:F3}";
        static float _stageAt;

        static void Sample(float t)
        {
            if (_loop == null) _loop = UnityEngine.Object.FindFirstObjectByType<GameLoop>();
            var w = _loop?.World;
            if (w == null) return;
            var tr = w.Train;
            if (w.OfferPending && !_wasOffer)
            {
                _beforeStacks = (int[])tr.CardStacks.Clone();
                _beforeStats = Stats(tr);
                Debug.Log("[play] offer BEFORE " + _beforeStats);
            }
            if (!w.OfferPending && _wasOffer)
            {
                for (int i = 0; i < tr.CardStacks.Length; i++)
                    if (tr.CardStacks[i] != _beforeStacks[i])
                        Debug.Log($"[play] genuine click APPLIED {(TrainSurvivor.Core.CardId)i} stacks={_beforeStacks[i]}->{tr.CardStacks[i]} BEFORE {_beforeStats} AFTER {Stats(tr)}");
                _resumeS = tr.S;
                _awaitResume = true;
                if (!_clickedShot)
                {
                    _clickedShot = true;
                    Snap("05-genuine-click-pressed");
                    var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
                    var rects = (RectTransform[])hud.GetType().GetField("_cardRects", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);
                    var face = rects[1].Find("Face").GetComponent<UnityEngine.UI.Image>();
                    Debug.Log($"[play] pressed faceTint={face.color} scale={rects[1].localScale} spriteBorder={face.sprite.border}");
                }
            }
            _wasOffer = w.OfferPending;
            if (_awaitResume && tr.S > _resumeS + 1f)
            {
                Debug.Log($"[play] RESUMED s={_resumeS:F3}->{tr.S:F3} modal={TrainSurvivor.Game.UI.UiState.Modal}");
                _awaitResume = false;
            }

            int living = 0, attacks = 0, deaths = 0, strideOn = 0, contacts = 0, reach = 0;
            if (_refs?.Pool != null)
            {
                for (int i = 0; i < _refs.Pool.Count; i++)
                {
                    object b = _refs.Pool[i]; if (b == null) continue;
                    string playing = _refs.PlayingF?.GetValue(b) as string;
                    if (playing == "Walk") living++;
                    if (playing == "Attack")
                    {
                        attacks++;
                        var sa = _refs.StrideF?.GetValue(b);
                        float aw = sa != null ? (float)(_refs.WeightF?.GetValue(sa) ?? 0f) : 1f;
                        if (aw > 0.05f) _attackWeighted++; else _attackReleased++;
                    }
                    if (playing == "Death") deaths++;
                    var s = _refs.StrideF?.GetValue(b); if (s == null) continue;
                    if ((float)(_refs.WeightF?.GetValue(s) ?? 0f) > 0.9f) strideOn++;
                    contacts = Mathf.Max(contacts, (int)(_refs.ContactsF?.GetValue(s) ?? 0));
                    reach = Mathf.Max(reach, (int)(_refs.ReleasedF?.GetValue(s) ?? 0));
                }
                if (attacks > 0) _attackSeen = true;
                if (deaths > 0) _deathSeen = true;
                if (_attackReleased > 0 && _attackWeighted == 0) _attackReleaseLogged = true;
            }
            _maxLiving = Mathf.Max(_maxLiving, living);
            _maxStrideBodies = Mathf.Max(_maxStrideBodies, strideOn);
            _lastContacts = Mathf.Max(_lastContacts, contacts);
            _maxReachRel = Mathf.Max(_maxReachRel, reach);
            _maxStage = Mathf.Max(_maxStage, tr.Stage);
            _maxLevel = Mathf.Max(_maxLevel, tr.Level);
            if (w.OfferPending) _offerSeen = true;
            if (_lastHp >= 0f && tr.Hp < _lastHp - 0.01f) { }
            _lastHp = tr.Hp;

            if (!_shotStart && t > 1.2f) { _shotStart = true; Snap("01-fresh-start"); MeasureSilhouette("01-fresh-start"); }

            // cards only exist once the HUD has settled past its 1.15 s intro: wait it out
            if (w.OfferPending && _offerAt <= 0f) _offerAt = t;
            if (!w.OfferPending) _offerAt = 0f;
            if (!_shotOffer && w.OfferPending && _offerAt > 0f && t > _offerAt + 3.8f)
            { _shotOffer = true; Snap("02-natural-offer-settled"); MeasureOffer(); }

            float remaining = w.StageLength - tr.S;
            if (!_shotLate && tr.Stage == 1 && remaining <= 200f && remaining >= 183f)
            { _shotLate = true; Snap("03-factory-combat"); }

            if (!_shotStage2 && tr.Stage >= 2 && _stageAt <= 0f) _stageAt = t;
            if (!_shotStage2 && _stageAt > 0f && t > _stageAt + 1.6f) { _shotStage2 = true; Snap("04-stage2"); }

            if (t >= _nextSample)
            {
                _nextSample = t + 1f;
                Debug.Log(string.Format(
                    "[play] t={0,5:0.0}s stage={1} level={2} hp={3}/{4} wagons={5} guns={6} s={7:0}m prog={8:0.0}% " +
                    "offer={9} walkers={10} attacks={11} deaths={12} strideW={13} contacts={14} reachRel={15} mouse={16} intent={17}",
                    t, tr.Stage, tr.Level, Mathf.RoundToInt(tr.Hp), Mathf.RoundToInt(tr.MaxHp), tr.Wagons, tr.Guns,
                    tr.S, w.Progress01 * 100f, w.OfferPending, living, attacks, deaths, strideOn, contacts, reach,
                    Mouse.current != null && Mouse.current.leftButton.isPressed,
                    IntentHeld()));
                if (tr.Stage >= 2 && !_stage2Logged)
                {
                    _stage2Logged = true;
                    _stage2At = t;
                    Debug.Log(string.Format("[play] STAGE2 REACHED at t={0:0.0}s level={1} hp={2}/{3} wagons={4} guns={5}",
                        t, tr.Level, Mathf.RoundToInt(tr.Hp), Mathf.RoundToInt(tr.MaxHp), tr.Wagons, tr.Guns));
                }
            }
            if (_stage2Logged && _stage2At > 0f && t > _stage2At + 25f) { Finish("stage2+25s"); }
            if (tr.Stage >= _stopStage && _maxStage > 2) Finish("stage3");
        }


        static bool _diagDone;

        /// <summary>Progressive isolation of the horizon band at the end of a run: each capture
        /// removes one more contributor, so the frame where the band disappears names the draw.</summary>
        static void Diag()
        {
            if (_diagDone) return;
            _diagDone = true;
            Snap("diag-A-all");
            var haze = GameObject.Find("HazeShell");
            if (haze != null) haze.SetActive(false);
            Snap("diag-B-no-haze");
            foreach (var o in UnityEngine.Object.FindObjectsByType<TrainSurvivor.Game.View.DistrictScenery>(FindObjectsSortMode.None))
                o.gameObject.SetActive(false);
            Snap("diag-C-no-district");
            foreach (var nm in new[] { "Track", "Ground", "TrackMesh" })
            {
                var go = GameObject.Find(nm);
                if (go != null) go.SetActive(false);
            }
            Snap("diag-D-no-track");
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            if (hud != null) hud.gameObject.SetActive(false);
            Snap("diag-E-no-ui");
            foreach (var o in UnityEngine.Object.FindObjectsByType<TrainSurvivor.Game.View.TrainRig>(FindObjectsSortMode.None))
                o.gameObject.SetActive(false);
            Snap("diag-F-no-train");
        }

        static Vector2 CardScreenPos()
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            var f = hud != null ? hud.GetType().GetField("_cardRects", BindingFlags.NonPublic | BindingFlags.Instance) : null;
            var arr = f?.GetValue(hud) as RectTransform[];
            if (arr == null || arr.Length < 2 || arr[1] == null || !arr[1].gameObject.activeInHierarchy)
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var rt = arr[1];
            var canvas = rt.GetComponentInParent<Canvas>();
            Vector3 world = rt.TransformPoint(rt.rect.center);
            return RectTransformUtility.WorldToScreenPoint(
                canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, world);
        }

        static string IntentHeld()
        {
            var router = UnityEngine.Object.FindFirstObjectByType<InputRouter>();
            if (router == null) return "no-router";
            var cur = typeof(InputRouter).GetProperty("Current");
            object intent = cur?.GetValue(router);
            var held = intent?.GetType().GetField("Held");
            return held != null ? held.GetValue(intent).ToString() : "?";
        }


        /// <summary>One-off still, HUD included. The overlay canvas is moved onto the capture
        /// camera only for this frame and restored immediately, so a long no-capture run keeps
        /// resolving taps in real screen space.</summary>
        static float _hudTopRow, _trayBottomRow;
        static void MeasureHudRects(string name)
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            var root = hud.GetComponentInChildren<Canvas>().transform.Find("SafeArea");
            float Row(RectTransform rect, int corner)
            {
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var canvas = rect.GetComponentInParent<Canvas>();
                return CapH - RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners[corner]).y;
            }
            _hudTopRow = Row((RectTransform)root.Find("XpBar"), 2);
            _trayBottomRow = Row((RectTransform)root.Find("CardPanel/Tray"), 0);
            Debug.Log($"[play] HUD {name}: xpTopRow={_hudTopRow:F2} trayBottomRow={_trayBottomRow:F2} resolution={CapW}x{CapH}");
        }

        static void Snap(string name)
        {
            if (_cam == null) return;
            if (_rt == null || _rt.width != CapW || _rt.height != CapH)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); }
                if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
                _rt = new RenderTexture(CapW, CapH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                _tex = new Texture2D(CapW, CapH, TextureFormat.RGB24, false);
            }
            var canvasList = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = new RenderMode[canvasList.Length];
            for (int i = 0; i < canvasList.Length; i++)
            {
                modes[i] = canvasList[i].renderMode;
                if (canvasList[i].renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvasList[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvasList[i].worldCamera = _cam;
                canvasList[i].planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            _cam.aspect = (float)CapW / CapH;
            _cam.targetTexture = _rt;
            _cam.Render();
            MeasureHudRects(name);
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            for (int i = 0; i < canvasList.Length; i++) canvasList[i].renderMode = modes[i];
            Canvas.ForceUpdateCanvases();

            // measured convoy box in viewport percent (real projection, not an estimate)
            var rig = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.TrainRig>();
            if (rig != null)
            {
                var rs = rig.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds wb = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) wb.Encapsulate(rs[i].bounds);
                    float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? wb.min.x : wb.max.x,
                            (c & 2) == 0 ? wb.min.y : wb.max.y,
                            (c & 4) == 0 ? wb.min.z : wb.max.z);
                        var vp = _cam.WorldToViewportPoint(corner);
                        x0 = Mathf.Min(x0, vp.x); x1 = Mathf.Max(x1, vp.x);
                        y0 = Mathf.Min(y0, vp.y); y1 = Mathf.Max(y1, vp.y);
                    }
                    Debug.Log(string.Format(
                        "[play] bbox {0}: left {1:0.0}% right {2:0.0}% top(from top) {3:0.0}% bottom {4:0.0}% width {5:0.0}% height {6:0.0}% dist {7:0.0}m to convoy centre",
                        name, x0 * 100f, x1 * 100f, (1f - y1) * 100f, (1f - y0) * 100f,
                        (x1 - x0) * 100f, (y1 - y0) * 100f, Vector3.Distance(_cam.transform.position, wb.center)));
                }
            }

            var dir = Path.Combine(Root, "shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), _tex.EncodeToPNG());
        }

        // Synthetic coverage stress only; kept separate from the ordinary-input RunLong.
        static void Coverage()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.TrainRig>();
            var follow = _cam.GetComponent<TrainSurvivor.Game.View.CameraFollow>();
            var tr = _loop.World.Train;
            tr.Wagons = TrainSurvivor.Core.SimConfig.MaxWagons;
            foreach (float s in new[] { 0f, 180f, 420f, 860f })
            {
                tr.S = s;
                rig.LateUpdate();
                for (int i = 0; i < 300; i++) follow.SendMessage("LateUpdate");
                Debug.Log($"[play] coverage s={s} wagons={tr.Wagons} camera={_cam.transform.position} far={_cam.farClipPlane}");
                Snap("coverage-six-wagons-s" + s.ToString("000"));
                MeasureSilhouette("coverage-six-wagons-s" + s.ToString("000"));
                var ground = GameObject.Find("Ground").GetComponent<Renderer>().bounds;
                // All visible ground intersections must lie inside the actual mesh, even
                // beyond the distance blend. Full viewport grid, not a few sampled pixels.
                int tested = 0, outside = 0;
                var plane = new Plane(Vector3.up, new Vector3(0f, ground.center.y, 0f));
                for (int y = 0; y <= 96; y++) for (int x = 0; x <= 54; x++)
                {
                    var ray = _cam.ViewportPointToRay(new Vector3(x / 54f, y / 96f, 0f));
                    if (!plane.Raycast(ray, out float d) || d > _cam.farClipPlane || d < 0f) continue;
                    Vector3 p = ray.GetPoint(d); tested++;
                    if (p.x < ground.min.x || p.x > ground.max.x || p.z < ground.min.z || p.z > ground.max.z) outside++;
                }
                Debug.Log($"[play] coverage-grid s={s}: tested={tested} outside={outside} ground={ground}");
                if (outside != 0) throw new InvalidOperationException("Ground coverage grid failed");
            }
        }

        // Controlled long-convoy framing check: the train is forced to MaxWagons (six) and the
        // camera is settled, then the rear clearance above the real HUD edge is measured from
        // rasterized train pixels at both supported portrait sizes. Controlled, not played.
        static void ConvoyFit()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.TrainRig>();
            var follow = _cam.GetComponent<TrainSurvivor.Game.View.CameraFollow>();
            var tr = _loop.World.Train;
            // Starter first, untouched: original one-wagon chase still reads as before.
            Debug.Log($"[play] convoyfit starter wagons={tr.Wagons} s={tr.S}");
            foreach (var r in rig.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer || r is SkinnedMeshRenderer)
                    Debug.Log($"[play] trainpart {r.name} center={r.bounds.center} size={r.bounds.size}");
            rig.LateUpdate();
            ShootLong(540, 960, "starter-540x960", follow);
            tr.Wagons = TrainSurvivor.Core.SimConfig.MaxWagons;
            Debug.Log($"[play] convoyfit controlled wagons={tr.Wagons} (not a played run)");
            foreach (float s in new[] { 0f, 180f, 420f, 860f })
            {
                tr.S = s;
                rig.LateUpdate();
                ShootLong(540, 960, "convoy-540x960-s" + s.ToString("000"), follow);
                ShootLong(360, 780, "convoy-360x780-s" + s.ToString("000"), follow);
                Debug.Log($"[play] convoyfit tailClear s={s} (see safe-margins lines above)");
            }
        }

        static void ShootLong(int w, int h, string name, TrainSurvivor.Game.View.CameraFollow follow)
        {
            CapW = w; CapH = h;
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); }
                if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
                _tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            }
            if (_cam == null) return;
            _cam.aspect = (float)w / h;
            foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (cv.renderMode != RenderMode.ScreenSpaceCamera) continue;
                cv.worldCamera = _cam;
                cv.planeDistance = 1f;
            }
            Canvas.ForceUpdateCanvases();
            for (int i = 0; i < 240; i++) follow.SendMessage("LateUpdate");
            Snap(name);
            MeasureSilhouette(name);
        }


        static void MeasureOffer()
        {
            var follow = _cam.GetComponent<TrainSurvivor.Game.View.CameraFollow>();
            var pos = _cam.transform.position;
            var rot = _cam.transform.rotation;
            var df = typeof(TrainSurvivor.Game.View.CameraFollow).GetField("_distance", BindingFlags.Instance | BindingFlags.NonPublic);
            object distance = df.GetValue(follow);
            MeasureSilhouette("02-offer-540x960");
            CapW = 360; CapH = 780;
            _cam.aspect = (float)CapW / CapH;
            // Settle only the camera at the exact same paused natural offer/world state.
            for (int i = 0; i < 240; i++) follow.SendMessage("LateUpdate");
            Snap("02-offer-360x780");
            MeasureSilhouette("02-offer-360x780");
            CapW = 540; CapH = 960;
            _cam.aspect = (float)CapW / CapH;
            _cam.transform.SetPositionAndRotation(pos, rot);
            df.SetValue(follow, distance);
        }

        static void MeasureSilhouette(string name)
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.View.TrainRig>();
            var renderers = rig.GetComponentsInChildren<Renderer>();
            var saved = new Material[renderers.Length][];
            var mark = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mark.SetColor("_BaseColor", Color.magenta);
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var enabled = new bool[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { enabled[i] = canvases[i].enabled; canvases[i].enabled = false; }
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) continue;
                saved[i] = renderers[i].sharedMaterials;
                var mats = new Material[saved[i].Length];
                for (int j = 0; j < mats.Length; j++) mats[j] = mark;
                renderers[i].sharedMaterials = mats;
            }
            _cam.targetTexture = _rt;
            _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0); _tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(Root, "shots", name + "-silhouette.png"), _tex.EncodeToPNG());
            int top = CapH, bottom = -1, left = CapW, right = -1, pixels = 0;
            var px = _tex.GetPixels32();
            for (int y = 0; y < CapH; y++) for (int x = 0; x < CapW; x++)
            {
                var c = px[y * CapW + x];
                if (c.r < 64 || c.b < 64 || c.r < c.g * 3 || c.b < c.g * 3) continue;
                int row = CapH - 1 - y;
                top = Mathf.Min(top, row); bottom = Mathf.Max(bottom, row);
                left = Mathf.Min(left, x); right = Mathf.Max(right, x); pixels++;
            }
            Debug.Log($"[play] silhouette {name}: x={left}..{right} y={top}..{bottom} pixels={pixels} (rasterized train surfaces, no smoke/shadows)");
            Debug.Log($"[play] safe-margins {name}: tailToXp={_hudTopRow - bottom:F2}px noseBelowTray={top - _trayBottomRow:F2}px");
            for (int i = 0; i < renderers.Length; i++) if (saved[i] != null) renderers[i].sharedMaterials = saved[i];
            for (int i = 0; i < canvases.Length; i++) canvases[i].enabled = enabled[i];
            UnityEngine.Object.DestroyImmediate(mark);
        }

        static void Capture()
        {
            if (_cam == null) return;
            _cam.targetTexture = _rt; _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, CapW, CapH), 0, 0); _tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null;
            _dir = _dir ?? Path.Combine(Root, "frames");
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(Path.Combine(_dir, string.Format("ff-{0:0000}.png", _shots)), _tex.EncodeToPNG());
            _shots++;
        }

        static void Finish(string why)
        {
            var sorted = new List<float>(_ft); sorted.Sort();
            float mean = 0f; for (int i = 0; i < sorted.Count; i++) mean += sorted[i];
            mean = sorted.Count > 0 ? mean / sorted.Count : 0f;
            float P(float q) => sorted.Count > 0 ? sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)] * 1000f : 0f;
            var tr = _loop?.World?.Train;
            Debug.Log(string.Format(
                "[play] DONE why={0} frames={1} shots={2} samples={3} mean={4:0.0}ms p95={5:0.0}ms p99={6:0.0}ms worst={7:0.0}ms " +
                "stageMax={8} levelMax={9} walkersMax={10} strideBodiesMax={11} contacts={12} reachRel={13} " +
                "offerSeen={14} attackSeen={15} deathSeen={16} attackRelease={17} endStage={18} endLevel={19} endHp={20} atkReleasedFrames={21} atkWeightedFrames={22}",
                why, _frames, _shots, sorted.Count, mean * 1000f, P(0.95f), P(0.99f), P(1f),
                _maxStage, _maxLevel, _maxLiving, _maxStrideBodies, _lastContacts, _maxReachRel,
                _offerSeen, _attackSeen, _deathSeen, _attackReleaseLogged,
                tr?.Stage ?? -1, tr?.Level ?? -1, tr != null ? Mathf.RoundToInt(tr.Hp) : -1, _attackReleased, _attackWeighted,
                Time.time, Time.realtimeSinceStartup - _start));

            Debug.Log($"[play] combat shots={_bulletShots} turretKills={_bulletKills} kills={tr?.Kills}");
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}

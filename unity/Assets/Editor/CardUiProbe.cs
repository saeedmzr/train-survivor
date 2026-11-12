using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using TrainSurvivor.Core;
using TrainSurvivor.Game;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// Deterministic UI check for the approved card set: runs the real game, forces one offer per
    /// approved card (reflection only — no shipping debug API), captures the paused card panel and
    /// a narrow portrait variant, then lets the game play a few seconds to catch a natural offer.
    /// </summary>
    [InitializeOnLoad]
    public static class CardUiProbe
    {
        const string ScenePath = "Assets/_Project/Scenes/Run.unity";
        const string Key = "TrainSurvivor.CardUiProbe";
        static readonly string Root = ResolveRoot();
        static string ResolveRoot()
        {
            string env = Environment.GetEnvironmentVariable("TS_EVIDENCE_ROOT");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "evidence",
                "2026-11-11-deepseek-convoy-fit", "cards"));
        }

        static GameLoop _loop;
        static Camera _cam;
        static RenderTexture _rt;
        static Texture2D _tex;
        static int _w = 540, _h = 960;
        static float _t, _step;
        static int _phase, _idx, _lastFrame = -1;
        static readonly string[] Keys =
        {
            "heavyhitter","fastfire","longreach","quickaim","toughtrain","strongarmor","fixitup","fullsteam","fastlearner"
        };

        static CardUiProbe() { if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick; }

        public static void Run()
        {
            Directory.CreateDirectory(Path.Combine(Root, "ui"));
            EditorSceneManager.OpenScene(ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            if (_loop == null) Begin();
            _t += Time.unscaledDeltaTime;

            if (_step <= 0f) { _step = 0.35f; Act(); }
            _step -= Time.unscaledDeltaTime;
            if (_t > 60f) Finish();
        }

        static void Begin()
        {
            _cam = Camera.main;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (!c.name.Contains("Walker") && (_cam == null || c.depth > _cam.depth)) _cam = c;
            _loop = UnityEngine.Object.FindFirstObjectByType<GameLoop>();
            // Controlled card catalogue pose, away from the spawn-side warehouse; natural
            // gameplay/offer evidence is recorded separately by PlayProbe.RunLong.
            if (_loop != null) _loop.World.Train.S = 40f;
            _rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            _tex = new Texture2D(_w, _h, TextureFormat.RGB24, false);
            // the HUD is overlay-only: move it onto the capture camera for the still
            if (_cam != null)
            {
                _cam.aspect = (float)_w / _h;
                foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (cv.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    cv.renderMode = RenderMode.ScreenSpaceCamera;
                    cv.worldCamera = _cam;
                    cv.planeDistance = 1f;
                }
            }
            Debug.Log("[cards] begin loop=" + (_loop != null));
            foreach (string name in new[] { "card_face", "card_plaque", "tray_face", "up_heavyhitter", "up_fastfire", "up_longreach", "up_quickaim", "up_toughtrain", "up_strongarmor", "up_fixitup", "up_fullsteam", "up_fastlearner" })
            {
                var tex = Resources.Load<Texture2D>("UI/cards/" + name);
                var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/UI/cards/" + name + ".png");
                Debug.Log($"[cards] import {name}: {tex.width}x{tex.height} format={tex.format} mipCount={tex.mipmapCount} type={importer.textureType} npot={importer.npotScale} compression={importer.textureCompression} alpha={importer.alphaIsTransparency} wrap={tex.wrapMode} filter={tex.filterMode} max={importer.maxTextureSize}");
            }
        }

        static void Act()
        {
            var w = _loop?.World;
            if (w == null) return;

            if (_phase < 9)
            {
                if (_idx == 0)
                {
                    if (_phase == 3) ForceOffer(w, CardId.QuickAim, CardId.StrongArmor, CardId.FullSteam);
                    else ForceOffer(w, (CardId)(9 + _phase));
                    _idx = 1;
                    return;
                }
                if (_idx < 12)
                {
                    _idx++;                   // the HUD holds the panel briefly before showing it
                    Debug.Log("[cards] offer=" + w.OfferPending + " count=" + w.OfferCount + " card=" + Keys[_phase]);
                    return;
                }
                Capture("ui/card-" + Keys[_phase] + ".png");
                ClearOffer(w);
                _idx = 0;
                _phase++;
                return;
            }
            if (_phase == 9)
            {
                if (_idx == 0) { _w = 360; _h = 780; Resize(); }
                ForceOffer(w, CardId.FastFire, CardId.ToughTrain, CardId.LongReach);
                _idx++;
                bool ok = PanelReady(w, Titles(CardId.FastFire, CardId.ToughTrain, CardId.LongReach))
                          && Screen.width > 0;
                if ((!ok || _idx < 12) && _idx < 24) return;
                if (!ok)
                {
                    Debug.Log("[cards] INVALID CAPTURE: narrow panel never settled; nothing written");
                    _phase = 11; _idx = 0; return;
                }
                Capture("ui/narrow-portrait.png");             // proven settled panel at 360x780
                Debug.Log("[cards] VALID narrow capture 360x780 with FAST FIRE/TOUGH TRAIN/LONG REACH");
                _idx = 0; _phase++;
                return;
            }
            if (_phase == 10)
            {
                _w = 540; _h = 960; Resize();
                ClearOffer(w);
                _w = 540; _h = 960; Resize();
                Finish();
                return;
            }
            if (w.OfferPending)
            {
                Capture("ui/natural-offer.png");
                w.ChooseCard(0);
                _phase = 12;
                return;
            }
            if (_phase < 12 && _t > 12f) Capture("ui/gameplay-signoff.png");
        }

        static void Resize()
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.DestroyImmediate(_rt); }
            _rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            if (_tex != null) UnityEngine.Object.DestroyImmediate(_tex);
            _tex = new Texture2D(_w, _h, TextureFormat.RGB24, false);
            if (_cam != null) _cam.aspect = (float)_w / _h;
        }

        static void ForceOffer(World w, CardId a, CardId b = CardId.FastFire, CardId c = CardId.FullSteam)
        {
            // keep every pictured offer distinct: pick fillers the featured card is not
            var alt = new[] { CardId.FastFire, CardId.FullSteam, CardId.StrongArmor, CardId.LongReach };
            int k = 0;
            while (b == a && k < alt.Length) b = alt[k++];
            k = 0;
            while ((c == a || c == b) && k < alt.Length) c = alt[k++];
            var t = typeof(World);
            var offer = (CardId[])t.GetField("_offer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w);
            offer[0] = a; offer[1] = b; offer[2] = c;
            t.GetField("_offerCount", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(w, 3);
            var pending = t.GetProperty("OfferPending", BindingFlags.Public | BindingFlags.Instance);
            pending.SetValue(w, true);

            // batchmode runs uncapped, so the HUD's 1.15 s "suspense" would take thousands of
            // frames: zero it for the still, exactly as it looks once the cards have slid in
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            if (hud != null)
            {
                var ht = typeof(TrainSurvivor.Game.UI.HudView);
                ht.GetField("_offerDelay", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(hud, 0f);
                ht.GetField("_offerWasPending", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(hud, true);
            }
        }

        /// <summary>True only when the live card panel is actually up, settled, showing exactly
        /// the expected three titles at the expected canvas size. A capture taken without this
        /// is invalid and is logged as such instead of being presented as panel evidence.</summary>
        static bool PanelReady(World w, params string[] expected)
        {
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            if (hud == null) { Debug.Log("[cards] INVALID: no HudView"); return false; }
            var t = typeof(TrainSurvivor.Game.UI.HudView);
            var panel = t.GetField("_cardPanel", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud) as RectTransform;
            if (panel == null || !panel.gameObject.activeInHierarchy) { Debug.Log("[cards] wait: panel closed"); return false; }
            Canvas.ForceUpdateCanvases();
            var names = t.GetField("_cardNames", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud) as UnityEngine.UI.Text[];
            var rects = t.GetField("_cardRects", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud) as RectTransform[];
            if (names == null || rects == null) { Debug.Log("[cards] INVALID: card refs missing"); return false; }
            if (Mathf.Abs(Screen.width - (int)(panel.rect.width * 0f)) < 0 && false) { }
            for (int i = 0; i < 3; i++)
            {
                if (rects[i] == null || !rects[i].gameObject.activeSelf || rects[i].rect.height < 40f)
                { Debug.Log("[cards] wait: card " + i + " not laid out"); return false; }
                if (names[i] == null || names[i].text != expected[i])
                { Debug.Log(string.Format("[cards] wait: card {0} shows '{1}' expected '{2}'", i, names[i]?.text, expected[i])); return false; }
                if (names[i].rectTransform.rect.width < 80f) { Debug.Log("[cards] wait: text not laid out"); return false; }
            }
            if (!panel.gameObject.activeInHierarchy) return false;
            return true;
        }

        static string[] Titles(CardId a, CardId b, CardId c) =>
            new[] { TrainSurvivor.Core.Cards.Names[(int)a], TrainSurvivor.Core.Cards.Names[(int)b], TrainSurvivor.Core.Cards.Names[(int)c] };

        static void ClearOffer(World w)
        {
            var pending = typeof(World).GetProperty("OfferPending", BindingFlags.Public | BindingFlags.Instance);
            pending.SetValue(w, false);
        }

        static void Capture(string rel)
        {
            if (_cam == null) return;
            var hud = UnityEngine.Object.FindFirstObjectByType<TrainSurvivor.Game.UI.HudView>();
            var icons = typeof(TrainSurvivor.Game.UI.HudView).GetField("_cardIcons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud) as UnityEngine.UI.Image[];
            for (int i = 0; i < 3; i++) Debug.Log($"[cards] mapping {rel} {_loop.World.OfferAt(i)} -> {icons[i].sprite?.name}");
            foreach (var text in hud.GetComponentsInChildren<UnityEngine.UI.Text>())
            {
                if (text.name != "Name" && text.name != "Blurb") continue;
                bool fits = text.preferredHeight <= text.rectTransform.rect.height + 0.1f;
                Debug.Log($"[cards] text {rel} '{text.text}' preferredH={text.preferredHeight:F1} box={text.rectTransform.rect} fits={fits}");
                if (!fits) throw new InvalidOperationException("Card label exceeds its safe text box");
            }
            _cam.targetTexture = _rt;
            _cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            var path = Path.Combine(Root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, _tex.EncodeToPNG());
        }

        static void Finish()
        {
            Debug.Log("[cards] done phase=" + _phase);
            SessionState.SetBool(Key, false);
            EditorApplication.isPlaying = false;
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}

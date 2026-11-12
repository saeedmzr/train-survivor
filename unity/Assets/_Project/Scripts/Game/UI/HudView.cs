using UnityEngine;
using UnityEngine.UI;
using TrainSurvivor.Core;
using TrainSurvivor.Game.Audio;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.Game.UI
{
    /// <summary>
    /// The real HUD, built from the art sheet: stage progress, HP with heart, XP bar,
    /// blockade warning, upgrade cards and the derailed/retry screen.
    /// Taps are hit-tested by hand so the game needs no EventSystem.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        static readonly Color Ink = new Color(0.98f, 0.98f, 1f);
        static readonly Color Muted = new Color(0.72f, 0.76f, 0.84f);
        static readonly Color PanelBg = new Color(0.07f, 0.10f, 0.17f, 0.98f);
        // card surfaces: aged graphite with restrained brass, not the blue HUD chrome
        static readonly Color CardBg = new Color(0.105f, 0.112f, 0.128f, 0.985f);
        static readonly Color CardEdge = new Color(0.72f, 0.56f, 0.28f, 1f);
        static readonly Color CardEdgeHot = new Color(1f, 0.84f, 0.48f, 1f);
        static readonly Color PanelEdge = new Color(0.32f, 0.40f, 0.55f, 1f);
        static readonly Color Gold = new Color(1f, 0.80f, 0.30f);

        RectTransform _root;
        Image _hpFill, _xpFill, _progFill, _hpFrame;
        RectTransform _xpBar;

        /// <summary>Bottom HUD height as a fraction of screen height (XP bar top edge, measured
        /// from the real layout). The chase camera keeps a long convoy above this line.</summary>
        public static float BottomHudFraction = 0.1f;
        static readonly Vector3[] HudCorners = new Vector3[4];

        Text _stageLabel, _hpLabel, _lvLabel, _hint, _bannerText, _deadStats, _xpLabel, _routeLabel;
        Image _banner, _warn;

        RectTransform _cardPanel, _deadPanel, _retryRect;
        readonly RectTransform[] _cardRects = new RectTransform[3];
        readonly Image[] _cardIcons = new Image[3];
        readonly Text[] _cardNames = new Text[3];
        readonly Text[] _cardBlurbs = new Text[3];

        int _shownLevel, _shownStage;
        float _bannerT, _bannerHold;
        bool _bannerActive;
        bool _cardsBuilt;
        CardId _lastA, _lastB, _lastC;
        bool _offerWasPending;
        float _offerDelay;

        public void Build()
        {
            ArtSprites.Preload();

            var canvas = UiFactory.CreateCanvas("HudCanvas", transform);
            _root = UiFactory.Node("SafeArea", canvas.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            gameObject.AddComponent<SafeAreaFitter>().Target = _root;

            BuildTopBar();
            BuildHealth();
            BuildXpBar();
            BuildOverlays();
        }

        // ------------------------------------------------------------------ top

        void BuildTopBar()
        {
            var top = Vector2.one;
            var topCenter = new Vector2(0.5f, 1f);

            // Top stack, laid out from measured heights rather than guessed offsets:
            //   plaque 0..132 (title centred inside), route bar 148..174, HP/LV row starts 196.
            const float plaqueH = 132f, routeY = plaqueH + 16f, routeH = 26f;
            var plaque = UiFactory.ImageNode("StagePlaque", _root, Sliced("card_plaque", 34f),
                Color.white, topCenter, topCenter, new Vector2(0.5f, 1f), new Vector2(0f, -18f),
                new Vector2(640f, plaqueH));
            plaque.type = Image.Type.Sliced;

            // title is a child of the plate, stretched with padding: it can never drift onto the bar
            _stageLabel = UiFactory.TextNode("StageLabel", plaque.transform, "STAGE 1  ·  380 m", 40,
                new Color(0.97f, 0.93f, 0.83f), TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Stretch(_stageLabel.rectTransform, 14f);
            _stageLabel.fontStyle = FontStyle.Bold;
            _stageLabel.verticalOverflow = VerticalWrapMode.Truncate;

            var frame = ArtSprites.Get("bar_frame", new Vector4(74f, 18f, 74f, 18f));
            var bar = UiFactory.ImageNode("ProgressBar", _root, frame, Color.white,
                topCenter, topCenter, new Vector2(0.5f, 1f), new Vector2(0f, -(18f + routeY)),
                new Vector2(556f, routeH));
            bar.type = Image.Type.Sliced;

            var fill = ArtSprites.Get("bar_fill_blue");   // tinted teal below
            _progFill = UiFactory.ImageNode("ProgressFill", bar.transform, fill, Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Inset(_progFill.rectTransform, 10f, 9f);
            _progFill.color = new Color(0.36f, 0.86f, 0.80f);   // route = teal, distinct from HP
            _progFill.type = Image.Type.Filled;
            _progFill.fillMethod = Image.FillMethod.Horizontal;
            _progFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            // Milestone flags riding on the bar.
            for (int i = 0; i < 3; i++)
            {
                float t = 0.25f + 0.25f * i;
                var flag = UiFactory.ImageNode("Flag" + i, bar.transform, ArtSprites.Get(i == 1 ? "flag_red" : "flag_green"),
                    Color.white, new Vector2(t, 0.5f), new Vector2(t, 0.5f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 22f), new Vector2(34f, 52f));
                flag.preserveAspect = true;
            }

            _routeLabel = UiFactory.TextNode("RouteLabel", _root, "", 1, Color.clear, TextAnchor.MiddleCenter,
                topCenter, topCenter, topCenter, new Vector2(0f, -170f), new Vector2(1f, 1f));
            _routeLabel.gameObject.SetActive(false);   // one distance readout only, inside the plaque

            _warn = UiFactory.ImageNode("Warning", _root, ArtSprites.Get("warn_0"), Color.white,
                top, top, top, new Vector2(-52f, -318f), new Vector2(112f, 104f));
            _warn.preserveAspect = true;
            _warn.gameObject.SetActive(false);
        }

        void BuildHealth()
        {
            var topLeft = new Vector2(0f, 1f);
            var heart = UiFactory.ImageNode("Heart", _root, ArtSprites.Get("icon_heart"), Color.white,
                topLeft, topLeft, topLeft, new Vector2(36f, -226f), new Vector2(96f, 96f));
            heart.preserveAspect = true;

            var frame = ArtSprites.Get("bar_frame", new Vector4(74f, 18f, 74f, 18f));
            _hpFrame = UiFactory.ImageNode("HpBar", _root, frame, Color.white,
                topLeft, topLeft, topLeft, new Vector2(136f, -232f), new Vector2(648f, 72f));
            _hpFrame.type = Image.Type.Sliced;

            _hpFill = UiFactory.ImageNode("HpFill", _hpFrame.transform, ArtSprites.Get("bar_fill_green"), Color.white,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Inset(_hpFill.rectTransform, 18f, 17f);
            _hpFill.type = Image.Type.Filled;
            _hpFill.fillMethod = Image.FillMethod.Horizontal;
            _hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            _hpLabel = UiFactory.TextNode("HpLabel", _hpFrame.transform, "100 / 100", 30, Ink, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _hpLabel.fontStyle = FontStyle.Bold;
            Stretch(_hpLabel.rectTransform);

            _lvLabel = UiFactory.TextNode("Level", _root, "LV 1", 34,
                new Color(0.97f, 0.93f, 0.83f), TextAnchor.MiddleCenter,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -226f),
                new Vector2(150f, 62f));
            _lvLabel.fontStyle = FontStyle.Bold;
        }

        void BuildXpBar()
        {
            var bottom = new Vector2(0.5f, 0f);

            var frame = ArtSprites.Get("bar_frame", new Vector4(74f, 18f, 74f, 18f));
            var bar = UiFactory.ImageNode("XpBar", _root, frame, new Color(1f, 1f, 1f, 0.98f),
                bottom, bottom, bottom, new Vector2(0f, 128f), new Vector2(940f, 68f));
            bar.type = Image.Type.Sliced;
            _xpBar = bar.rectTransform;

            _xpFill = UiFactory.ImageNode("XpFill", bar.transform, ArtSprites.Get("bar_fill_blue"),
                new Color(1f, 0.72f, 0.30f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Inset(_xpFill.rectTransform, 18f, 16f);
            _xpFill.type = Image.Type.Filled;
            _xpFill.fillMethod = Image.FillMethod.Horizontal;
            _xpFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            _xpLabel = UiFactory.TextNode("XpLabel", bar.transform, "XP 0 / 0", 30, Ink, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _xpLabel.fontStyle = FontStyle.Bold;
            Stretch(_xpLabel.rectTransform);

            _hint = UiFactory.TextNode("Hint", _root, "HOLD TO BOOST    ·    RELEASE TO FIRE", 28, Muted,
                TextAnchor.MiddleCenter, bottom, bottom, bottom, new Vector2(0f, 62f), new Vector2(1000f, 46f));
        }

        // ------------------------------------------------------------------ overlays

        void BuildOverlays()
        {
            var center = new Vector2(0.5f, 0.5f);

            // ---- banner -----------------------------------------------------
            _banner = UiFactory.ImageNode("Banner", _root, ArtSprites.Get("banner_victory"), Color.white,
                center, center, center, new Vector2(0f, 430f), new Vector2(760f, 220f));
            _banner.preserveAspect = true;
            _banner.gameObject.SetActive(false);

            _bannerText = UiFactory.TextNode("BannerText", _root, "", 40, Gold, TextAnchor.MiddleCenter,
                center, center, center, new Vector2(0f, 300f), new Vector2(900f, 60f));
            _bannerText.fontStyle = FontStyle.Bold;

            // ---- card offer ------------------------------------------------
            _cardPanel = UiFactory.Node("CardPanel", _root, Vector2.zero, Vector2.one,
                center, Vector2.zero, Vector2.zero);
            Stretch(_cardPanel);

            var dim = UiFactory.ImageNode("Dim", _cardPanel, UiFactory.White(), new Color(0.02f, 0.03f, 0.05f, 0.62f),
                Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            Stretch(dim.rectTransform);

            // One authored brass/graphite tray; white preserves its painted colour and alpha.
            var tray = UiFactory.ImageNode("Tray", _cardPanel, Sliced("tray_face", 40f),
                Color.white,
                center, center, center, new Vector2(0f, 211f), new Vector2(776f, 742f));
            tray.type = Image.Type.Sliced;

            // heading plaque, overlapping the tray top edge
            var plaque = UiFactory.ImageNode("CardPlaque", _cardPanel, Sliced("card_plaque", 34f),
                Color.white, center, center, center, new Vector2(0f, 596f), new Vector2(640f, 112f));
            plaque.type = Image.Type.Sliced;

            var title = UiFactory.TextNode("CardTitle", _cardPanel, "Choose an upgrade", 54,
                new Color(0.97f, 0.93f, 0.83f), TextAnchor.MiddleCenter, center, center, center,
                new Vector2(0f, 598f), new Vector2(600f, 96f));
            title.fontStyle = FontStyle.Bold;

            var cue = UiFactory.TextNode("CardCue", _cardPanel, "Pick one to power up your train.", 35,
                new Color(0.90f, 0.87f, 0.79f), TextAnchor.MiddleCenter, center, center, center,
                new Vector2(0f, 500f), new Vector2(680f, 52f));

            for (int i = 0; i < 3; i++)
            {
                float y = 352f - i * 208f;
                var card = UiFactory.Node("Card" + i, _cardPanel, center, center, center,
                    new Vector2(0f, y), new Vector2(707f, 186f));
                _cardRects[i] = card;

                var face = UiFactory.ImageNode("Face", card, Sliced("card_face", 40f), Color.white,
                    Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
                Stretch(face.rectTransform);
                face.type = Image.Type.Sliced;

                var icon = UiFactory.ImageNode("Icon", card, null, Color.white,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(22f, 0f), new Vector2(196f, 196f));
                icon.preserveAspect = true;
                _cardIcons[i] = icon;

                // thin cream separator between the illustration column and the text column
                var sep = UiFactory.ImageNode("Sep", card, UiFactory.White(),
                    new Color(0.93f, 0.89f, 0.78f, 0.30f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(238f, 0f), new Vector2(2f, 132f));

                _cardNames[i] = UiFactory.TextNode("Name", card, "", 44,
                    new Color(0.98f, 0.94f, 0.85f), TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(264f, -30f), new Vector2(400f, 56f));
                _cardNames[i].fontStyle = FontStyle.Bold;

                _cardBlurbs[i] = UiFactory.TextNode("Blurb", card, "", 30,
                    new Color(0.86f, 0.85f, 0.80f), TextAnchor.UpperLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(264f, -90f), new Vector2(400f, 68f));
            }

            _cardPanel.gameObject.SetActive(false);

            // ---- death ------------------------------------------------------
            _deadPanel = UiFactory.Node("DeadPanel", _root, Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            Stretch(_deadPanel);

            var deadDim = UiFactory.ImageNode("Dim", _deadPanel, UiFactory.White(), new Color(0.03f, 0.02f, 0.02f, 0.86f),
                Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            Stretch(deadDim.rectTransform);

            var derailed = UiFactory.TextNode("Derailed", _deadPanel, "DERAILED", 110, new Color(1f, 0.32f, 0.26f),
                TextAnchor.MiddleCenter, center, center, center, new Vector2(0f, 260f), new Vector2(1000f, 140f));
            derailed.fontStyle = FontStyle.Bold;

            _deadStats = UiFactory.TextNode("Stats", _deadPanel, "", 34, Ink, TextAnchor.MiddleCenter,
                center, center, center, new Vector2(0f, 90f), new Vector2(900f, 200f));

            var retry = UiFactory.Node("Retry", _deadPanel, center, center, center, new Vector2(0f, -140f),
                new Vector2(460f, 132f));
            _retryRect = retry;

            var retryEdge = UiFactory.ImageNode("Edge", retry, UiFactory.Rounded(), Gold,
                Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            Stretch(retryEdge.rectTransform);
            retryEdge.type = Image.Type.Sliced;

            var retryBg = UiFactory.ImageNode("Bg", retry, UiFactory.Rounded(), new Color(0.32f, 0.10f, 0.06f, 1f),
                Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            Stretch(retryBg.rectTransform, 4f);
            retryBg.type = Image.Type.Sliced;

            var retryLabel = UiFactory.TextNode("Label", retry, "RETRY", 52, Gold, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, center, Vector2.zero, Vector2.zero);
            retryLabel.fontStyle = FontStyle.Bold;
            Stretch(retryLabel.rectTransform);

            _deadPanel.gameObject.SetActive(false);
        }

        static readonly System.Collections.Generic.Dictionary<CardId, Sprite> IconCache =
            new System.Collections.Generic.Dictionary<CardId, Sprite>();

        /// <summary>Approved cards ship their own small original illustration in Resources; the
        /// retired legacy ids keep their old atlas art so old stacks still read correctly.</summary>
        static readonly System.Collections.Generic.Dictionary<string, Sprite> SliceCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        // Authored source-pixel borders, in Unity's left/bottom/right/top order.
        static readonly System.Collections.Generic.Dictionary<string, Vector4> SliceBorders =
            new System.Collections.Generic.Dictionary<string, Vector4>
            {
                { "tray_face", new Vector4(45f, 42f, 45f, 42f) },
                { "card_face", new Vector4(32f, 26f, 32f, 26f) },
                { "card_plaque", new Vector4(44f, 14f, 44f, 14f) }
            };

        /// <summary>Loads a 9-sliced plate so chamfers and bolts keep their proportion at any
        /// card width. Unknown plates retain the caller's uniform-border fallback.</summary>
        static Sprite Sliced(string name, float border)
        {
            if (SliceCache.TryGetValue(name, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("UI/cards/" + name);
            if (tex == null) return null;
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                                   100f, 0, SpriteMeshType.FullRect,
                                   SliceBorders.TryGetValue(name, out var sides)
                                       ? sides : new Vector4(border, border, border, border));
            SliceCache[name] = sp;
            return sp;
        }

        static Sprite CardIcon(CardId id)
        {
            if ((int)id < (int)CardId.HeavyHitter) return ArtSprites.Get(Cards.Sprites[(int)id]);
            if (IconCache.TryGetValue(id, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("UI/cards/" + Cards.Sprites[(int)id]);
            if (tex == null) return null;
            var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sp.name = Cards.Sprites[(int)id];
            IconCache[id] = sp;
            return sp;
        }

        static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>Offsets a stretched child inward (used to sit the fill inside the bar frame).</summary>
        static void Inset(RectTransform rt, float x, float y)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(x, y);
            rt.offsetMax = new Vector2(-x, -y);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;

            World w = loop.World;
            UpdateBars(w);
            UpdateWarn(w);
            UpdateModals(w);
            UpdateBanner(Time.deltaTime);

            if (w.Train.Stage < _shownStage || w.Train.Level < _shownLevel)
            {
                // retry or a fresh run: never leave a stale banner or milestone on screen
                _shownLevel = w.Train.Level;
                _shownStage = w.Train.Stage;
                _bannerActive = false;
                if (_banner != null) _banner.gameObject.SetActive(false);
            }

            if (w.Train.Level != _shownLevel || w.Train.Stage != _shownStage)
            {
                bool levelUp = w.Train.Level > _shownLevel && _shownLevel > 0;
                bool newStage = w.Train.Stage > _shownStage && _shownStage > 0;
                _shownLevel = w.Train.Level;
                _shownStage = w.Train.Stage;

                if (newStage) ShowBanner("banner_victory", "STAGE " + w.Train.Stage);
                else if (levelUp) ShowBanner("banner_levelup", "LEVEL " + w.Train.Level);
            }
        }

        bool _rectLogged;

        void LogTopRects()
        {
            if (_rectLogged || _stageLabel == null) return;
            _rectLogged = true;
            Canvas.ForceUpdateCanvases();
            RectTransform title = _stageLabel.rectTransform;
            RectTransform bar = _root.Find("ProgressBar") as RectTransform;
            RectTransform hp = _hpFrame != null ? _hpFrame.rectTransform : null;
            var c = GetComponentInParent<Canvas>();
            if (c == null || bar == null) return;
            Vector3[] tc = new Vector3[4], bc = new Vector3[4];
            title.GetWorldCorners(tc); bar.GetWorldCorners(bc);
            Vector2 tMin = RectTransformUtility.WorldToScreenPoint(null, tc[0]);
            Vector2 tMax = RectTransformUtility.WorldToScreenPoint(null, tc[2]);
            Vector2 bMin = RectTransformUtility.WorldToScreenPoint(null, bc[0]);
            Vector2 bMax = RectTransformUtility.WorldToScreenPoint(null, bc[2]);
            Debug.Log(string.Format(
                "[hud] title rect y {0:0.0}..{1:0.0}  route rect y {2:0.0}..{3:0.0}  gap {4:0.0}px  canvas {5}x{6}",
                tMax.y, tMin.y, bMax.y, bMin.y, tMin.y - bMax.y, Screen.width, Screen.height));
        }

        /// <summary>Measure the live bottom-HUD edge in canvas space (camera/aspect independent).
        /// Skipped while a capture probe has moved the canvas into camera space.</summary>
        void MeasureBottomHud()
        {
            if (_xpBar == null) return;
            var canvas = _xpBar.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) return;
            var cvr = (RectTransform)canvas.transform;
            if (cvr.rect.height <= 1f) return;
            _xpBar.GetWorldCorners(HudCorners);
            float top = cvr.InverseTransformPoint(HudCorners[1]).y;
            float frac = (top - cvr.rect.yMin) / cvr.rect.height;   // 0 = screen bottom, 1 = top
            if (frac > 0.5f && frac < 0.99f) BottomHudFraction = 1f - frac;   // height of the HUD band
        }

        void UpdateBars(World w)
        {
            LogTopRects();
            MeasureBottomHud();
            float hp = Mathf.Clamp01(w.Train.Hp / Mathf.Max(1f, w.Train.MaxHp));
            _hpFill.fillAmount = hp;
            // one rounding rule for both values, and the readout can never show HP above max:
            // the old ceil(hp)/round(max) pair printed "168 / 167" at a fractional 167.4 max
            int maxHpShown = Mathf.Max(1, Mathf.RoundToInt(w.Train.MaxHp));
            int hpShown = Mathf.Clamp(Mathf.RoundToInt(w.Train.Hp), 0, maxHpShown);
            _hpLabel.text = hpShown + " / " + maxHpShown;

            float xp = Mathf.Clamp01(w.Train.Xp / Mathf.Max(0.01f, w.Train.XpToNext));
            _xpFill.fillAmount = xp;
            _xpLabel.text = "XP  " + Mathf.FloorToInt(w.Train.Xp) + " / " + Mathf.RoundToInt(w.Train.XpToNext)
                          + "   ->   LEVEL " + (w.Train.Level + 1);

            _progFill.fillAmount = w.Progress01;
            _lvLabel.text = "LV " + w.Train.Level;

            // route meter from the real stage length: metres of rail still ahead
            float left = Mathf.Max(0f, w.StageLength - w.Train.S);
            _stageLabel.text = "STAGE " + w.Train.Stage + "   ·   " + Mathf.RoundToInt(left) + " m";

            // Low HP: pulse the frame red — the art stays green, the warning rides the chrome.
            if (hp < 0.35f)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
                _hpFrame.color = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.30f), pulse);
            }
            else
            {
                _hpFrame.color = Color.white;
            }

            if (_hint != null) _hint.enabled = !w.OfferPending;
            _hint.text = w.Train.Mode == TrainMode.Blocked
                ? "RELEASE TO BLAST THE BLOCKADE"
                : w.Train.Mode == TrainMode.Moving
                    ? "HOLD = BOOST"
                    : "RELEASE = FIRE";
        }

        void UpdateWarn(World w)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < w.BlockadeCount; i++)
            {
                var b = w.Blockades[i];
                if (!b.Alive) continue;
                float gap = b.S - w.Train.S;
                if (gap < -1f) continue;
                if (gap < nearest) nearest = gap;
            }

            bool show = nearest < 80f;
            if (_warn.gameObject.activeSelf != show) _warn.gameObject.SetActive(show);
            if (!show) return;

            float blink = 0.55f + 0.45f * Mathf.Sin(Time.time * 10f);
            float urgency = Mathf.InverseLerp(80f, 10f, nearest);
            _warn.color = new Color(1f, Mathf.Lerp(1f, 0.55f, urgency), Mathf.Lerp(1f, 0.45f, urgency),
                                   Mathf.Lerp(0.55f, 1f, blink));
            float s = Mathf.Lerp(0.9f, 1.15f, blink);
            _warn.rectTransform.localScale = new Vector3(s, s, 1f);
        }

        void UpdateModals(World w)
        {
            bool offer = w.OfferPending;

            // Let the LEVEL UP / STAGE banner land before the cards cover the screen.
            if (offer && !_offerWasPending) _offerDelay = 1.15f;
            if (offer) _offerDelay -= Time.deltaTime;
            bool panelUp = offer && _offerDelay <= 0f;
            _offerWasPending = offer;

            if (offer)
            {
                CardId a = w.OfferAt(0), b = w.OfferAt(1), c = w.OfferAt(2);
                if (!_cardsBuilt || a != _lastA || b != _lastB || c != _lastC)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var id = w.OfferAt(i);
                        _cardIcons[i].sprite = CardIcon(id);
                        _cardIcons[i].enabled = _cardIcons[i].sprite != null;
                        _cardNames[i].text = Cards.Names[(int)id];
                        _cardBlurbs[i].text = Cards.Blurbs[(int)id];
                    }
                    _lastA = a; _lastB = b; _lastC = c; _cardsBuilt = true;
                }
            }

            if (_cardPanel.gameObject.activeSelf != panelUp) _cardPanel.gameObject.SetActive(panelUp);

            bool dead = w.Train.Mode == TrainMode.Dead;
            if (dead && !_deadPanel.gameObject.activeSelf)
            {
                _deadStats.text =
                    "STAGE " + w.Train.Stage + "     KILLS " + w.Train.Kills +
                    "\nBLOCKADES " + w.Train.BlockadesBroken + "     LEVEL " + w.Train.Level;
                _deadPanel.gameObject.SetActive(true);
            }
            else if (!dead && _deadPanel.gameObject.activeSelf)
            {
                _deadPanel.gameObject.SetActive(false);
            }

            UiState.Modal = offer || dead;

            if (!panelUp && !dead) return;
            if (!UiFactory.TryTap(out var tap)) return;

            if (panelUp)
            {
                // a short pool can show fewer than three cards; never leave an empty slot
                int shown = Mathf.Clamp(w.OfferCount, 0, 3);
                for (int i = 0; i < 3; i++)
                    if (_cardRects[i] != null) _cardRects[i].gameObject.SetActive(i < shown);

                // pressed feedback: the card under the thumb brightens and sinks slightly
                var ptr = UnityEngine.InputSystem.Pointer.current;
                bool down = ptr != null && ptr.press.isPressed;
                Vector2 at = down ? ptr.position.ReadValue() : Vector2.zero;
                for (int i = 0; i < shown; i++)
                {
                    bool over = down && UiFactory.Hit(_cardRects[i], at);
                    var edge = _cardRects[i].Find("Face")?.GetComponent<Image>();
                    if (edge != null) edge.color = over ? new Color(1f, 0.95f, 0.86f) : Color.white;
                    float sc = over ? 0.975f : 1f;
                    _cardRects[i].localScale = new Vector3(sc, sc, 1f);
                }

                for (int i = 0; i < shown; i++)
                {
                    if (!_cardRects[i].gameObject.activeSelf) continue;
                    if (!UiFactory.Hit(_cardRects[i], tap)) continue;
                    w.ChooseCard(i);
                    HideBanner();
                    _offerDelay = 0f;
                    break;
                }
            }
            else if (UiFactory.Hit(_retryRect, tap))
            {
                // Retry had no feedback of its own; the click is the confirmation.
                if (AudioDirector.Instance != null) AudioDirector.Instance.UiConfirm();
                HideBanner();
                w.ResetRun();
            }
        }

        // ------------------------------------------------------------------ banner

        void ShowBanner(string sprite, string sub)
        {
            _banner.sprite = ArtSprites.Get(sprite);
            _bannerText.text = sub;
            _banner.gameObject.SetActive(true);
            _bannerText.gameObject.SetActive(true);
            _bannerActive = true;
            _bannerT = 0f;
            _bannerHold = 1.15f;
        }

        void HideBanner()
        {
            _bannerActive = false;
            _banner.gameObject.SetActive(false);
            _bannerText.gameObject.SetActive(false);
        }

        void UpdateBanner(float dt)
        {
            if (!_bannerActive) return;

            _bannerT += dt;

            float pop = Mathf.Clamp01(_bannerT / 0.22f);
            float scale = Mathf.Lerp(0.55f, 1f, EaseOutBack(pop));
            _banner.rectTransform.localScale = new Vector3(scale, scale, 1f);
            _bannerText.rectTransform.localScale = new Vector3(scale, scale, 1f);

            float fadeStart = _bannerHold + 0.35f;
            if (_bannerT > fadeStart)
            {
                float a = Mathf.Clamp01(1f - (_bannerT - fadeStart) / 0.35f);
                var c = _banner.color;
                _banner.color = new Color(c.r, c.g, c.b, a);
                var tc = _bannerText.color;
                _bannerText.color = new Color(tc.r, tc.g, tc.b, a);
                if (a <= 0f)
                {
                    _banner.color = Color.white;
                    HideBanner();
                }
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float x = t - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }
    }

    /// <summary>Keeps the HUD out of notches and gesture bars. Updates only on rotation/resize.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        public RectTransform Target;
        Rect _last;
        int _lastW, _lastH;

        void Update()
        {
            if (Target == null) return;
            if (Screen.width == _lastW && Screen.height == _lastH && Screen.safeArea == _last) return;

            _lastW = Screen.width;
            _lastH = Screen.height;
            _last = Screen.safeArea;

            var sa = Screen.safeArea;
            Vector2 min = sa.position;
            Vector2 max = sa.position + sa.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;

            Target.anchorMin = min;
            Target.anchorMax = max;
            Target.offsetMin = Vector2.zero;
            Target.offsetMax = Vector2.zero;
        }
    }
}

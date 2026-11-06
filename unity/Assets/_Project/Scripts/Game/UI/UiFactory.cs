using UnityEngine;
using UnityEngine.UI;

namespace TrainSurvivor.Game.UI
{
    /// <summary>Small helpers for building UGUI from code — no prefabs, no scene YAML.</summary>
    public static class UiFactory
    {
        static Font _font;
        static Sprite _white;
        static Sprite _round;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _font;
            }
        }

        public static Sprite White()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return _white;
        }

        /// <summary>Rounded 9-sliced panel, tinted by Image.color.</summary>
        public static Sprite Rounded(float radius = 22f)
        {
            if (_round != null) return _round;

            const int size = 96;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            float r = radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(r - x, x - (size - 1 - r)));
                    float dy = Mathf.Max(0f, Mathf.Max(r - y, y - (size - 1 - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            var border = new Vector4(r + 2f, r + 2f, r + 2f, r + 2f);
            _round = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, border);
            return _round;
        }

        public static RectTransform Node(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image ImageNode(string name, Transform parent, Sprite sprite, Color color,
                                      Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                      Vector2 anchoredPos, Vector2 size)
        {
            var rt = Node(name, parent, anchorMin, anchorMax, pivot, anchoredPos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static Text TextNode(string name, Transform parent, string text, int size, Color color,
                                    TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax,
                                    Vector2 pivot, Vector2 anchoredPos, Vector2 box)
        {
            var rt = Node(name, parent, anchorMin, anchorMax, pivot, anchoredPos, box);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Sets up an overlay canvas sized for a 1080x1920 portrait reference.</summary>
        public static Canvas CreateCanvas(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
            return canvas;
        }

        /// <summary>Allows taps to be hit-tested later without an EventSystem.</summary>
        public static bool Hit(RectTransform rt, Vector2 screenPos)
        {
            return rt != null && rt.gameObject.activeInHierarchy &&
                   RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }

        public static bool TryTap(out Vector2 pos)
        {
            // Low frame rates (software GPU, thermal throttle) can swallow a whole
            // press+release between two frames, so a release counts as a tap too.
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null)
            {
                var press = touch.primaryTouch.press;
                if (press.wasPressedThisFrame || press.wasReleasedThisFrame)
                {
                    pos = touch.primaryTouch.position.ReadValue();
                    return true;
                }
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var button = mouse.leftButton;
                if (button.wasPressedThisFrame || button.wasReleasedThisFrame)
                {
                    pos = mouse.position.ReadValue();
                    return true;
                }
            }

            pos = default;
            return false;
        }
    }

    /// <summary>Shared with InputRouter: no train input while a card or death panel is up.</summary>
    public static class UiState
    {
        public static bool Modal;
    }
}

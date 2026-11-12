using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Stylised lime acid burst for Walker deaths: a short core pop, outward droplets and a thin
    /// fading vapour plume. Cosmetic only — no damage, no hazard, no gameplay state. Every quad is
    /// pooled up front and the system caps at Bursts simultaneous deaths, so a wave of kills can
    /// never allocate mid-frame or grow the draw cost without bound.
    /// </summary>
    public sealed class AcidBurst : MonoBehaviour
    {
        public const int Bursts = 3;
        const int Droplets = 14;
        const int Vapours = 4;
        const float Gravity = -9.8f;

        public static AcidBurst Instance { get; private set; }
        public int Plays { get; private set; }

        sealed class Part
        {
            public Transform T;
            public SpriteRenderer R;
            public Vector3 Vel;
            public float Life, MaxLife, Size0, Size1;
            public Color Tint;
            public bool Vapour;
            public bool Active => Life > 0f;
        }

        readonly List<Part> _parts = new List<Part>();
        int _cursor;
        static Sprite _softSprite;
        static Material _mat;
        Camera _cam;

        public static void Play(Vector3 at)
        {
            if (Instance == null)
            {
                var go = new GameObject("AcidBursts");
                Instance = go.AddComponent<AcidBurst>();
                Instance.Build();
            }
            Instance.Emit(at);
        }

        void Build()
        {
            if (_softSprite == null) _softSprite = MakeSoftSprite();
            if (_mat == null)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
                _mat = new Material(sh) { name = "AcidSprite" };
                _mat.mainTexture = _softSprite.texture;
            }
            int total = Bursts * (1 + Droplets + Vapours);
            for (int i = 0; i < total; i++)
            {
                var go = new GameObject("Acid" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _softSprite;
                sr.sharedMaterial = _mat;
                sr.sortingOrder = 60;
                go.SetActive(false);
                _parts.Add(new Part { T = go.transform, R = sr });
            }
        }

        void Emit(Vector3 at)
        {
            Plays++;
            _cam = _cam != null ? _cam : Camera.main;
            Spawn(at, Vector3.up * 0.6f, 0.14f, 0.95f, 2.3f, false,
                  new Color(0.66f, 1f, 0.20f, 0.95f), 0.9f);            // core pop
            for (int i = 0; i < Droplets; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = Random.Range(0.6f, 1f);
                Spawn(at + Random.insideUnitSphere * 0.18f,
                      new Vector3(Mathf.Cos(ang) * rad, Random.Range(0.55f, 1.25f), Mathf.Sin(ang) * rad)
                          * Random.Range(2.2f, 6.2f),
                      Random.Range(0.30f, 0.52f), Random.Range(0.18f, 0.34f), 0.06f, false,
                      new Color(0.58f, 0.98f, 0.18f, 0.95f));
            }
            for (int i = 0; i < Vapours; i++)
            {
                Spawn(at + Random.insideUnitSphere * 0.25f + Vector3.up * 0.3f,
                      new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.7f, 1.4f), Random.Range(-0.35f, 0.35f)),
                      Random.Range(0.55f, 0.85f), Random.Range(0.55f, 0.80f), 1.6f, true,
                      new Color(0.74f, 1f, 0.50f, 0.34f));
            }
        }

        void Spawn(Vector3 at, Vector3 vel, float life, float size0, float size1, bool vapour, Color tint, float flash = 0f)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                int idx = (_cursor + i) % _parts.Count;
                var p = _parts[idx];
                if (p.Active) continue;
                _cursor = (idx + 1) % _parts.Count;
                p.T.position = at;
                p.T.localScale = Vector3.one * size0;
                p.Vel = vel;
                p.Life = p.MaxLife = life;
                p.Size0 = size0; p.Size1 = size1;
                p.Tint = tint;
                p.Vapour = vapour;
                p.R.color = new Color(tint.r + flash, tint.g + flash, tint.b + flash, tint.a);
                p.T.gameObject.SetActive(true);
                return;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_cam == null) _cam = Camera.main;
            Quaternion face = _cam != null
                ? Quaternion.LookRotation(_cam.transform.forward, _cam.transform.up)
                : Quaternion.identity;

            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (!p.Active) continue;
                p.Life -= dt;
                if (p.Life <= 0f) { p.T.gameObject.SetActive(false); continue; }

                float k = 1f - p.Life / p.MaxLife;
                if (!p.Vapour) { p.Vel.y += Gravity * dt; p.Vel *= 1f - 1.6f * dt; }
                else p.Vel *= 1f - 0.9f * dt;

                p.T.position += p.Vel * dt;
                p.T.rotation = face;
                float size = Mathf.Lerp(p.Size0, p.Size1, k);
                p.T.localScale = new Vector3(size, size, 1f);
                var c = p.Tint;
                c.a = p.Tint.a * (1f - k * k);
                p.R.color = c;
            }
        }

        static Sprite MakeSoftSprite()
        {
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        }
    }
}

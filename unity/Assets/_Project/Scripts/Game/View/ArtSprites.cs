using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Runtime view of the generated art atlases. The table in ArtAtlas.Generated.cs is
    /// produced by tools/slice_art.py; textures live in Resources so they always build.
    /// </summary>
    public static class ArtSprites
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(64);
        static readonly Dictionary<string, Vector4> UvCache = new Dictionary<string, Vector4>(64);
        static readonly Dictionary<string, ArtAtlas.Entry> Index = new Dictionary<string, ArtAtlas.Entry>(64);

        static Texture2D _world, _ui, _enemies, _vfx, _missingTex;
        static Sprite _missing;
        static bool _built;
        static readonly Dictionary<string, Vector2> SizeCache = new Dictionary<string, Vector2>(256);

        public static Texture2D WorldAtlas => _world;
        public static Texture2D UiAtlas => _ui;
        public static Texture2D EnemyAtlas => _enemies;
        public static Texture2D VfxAtlas => _vfx;

        public static void Preload()
        {
            if (_built) return;
            _built = true;

            _world = Resources.Load<Texture2D>("Art/atlas_world");
            _ui = Resources.Load<Texture2D>("Art/atlas_ui");
            _enemies = Resources.Load<Texture2D>("Art/atlas_enemies");
            _vfx = Resources.Load<Texture2D>("Art/atlas_vfx");

            for (int i = 0; i < ArtAtlas.Entries.Length; i++)
            {
                var e = ArtAtlas.Entries[i];
                Index[e.Name] = e;

                float w = e.Atlas == "ui" ? e.AtlasW : e.AtlasW;
                float h = e.AtlasH;
                // Atlas rows are stored top-down (image space); Unity textures are bottom-up.
                float y = h - e.Y - e.H;
                UvCache[e.Name] = new Vector4(e.X / w, y / h, e.W / w, e.H / h);
            }

            // card art cut from art-work/enemies (tools/pack_enemy_atlas.py)
            for (int i = 0; i < EnemyArt.Entries.Length; i++)
            {
                var e = EnemyArt.Entries[i];
                Index[e.Name] = e;
                float y = e.AtlasH - e.Y - e.H;
                UvCache[e.Name] = new Vector4(e.X / (float)e.AtlasW, y / (float)e.AtlasH,
                                              e.W / (float)e.AtlasW, e.H / (float)e.AtlasH);
            }

            // projectiles, impact FX and the ground shadow (tools/pack_vfx_atlas.py)
            for (int i = 0; i < ArtVfx.Entries.Length; i++)
            {
                var e = ArtVfx.Entries[i];
                Index[e.Name] = e;
                float y = e.AtlasH - e.Y - e.H;
                UvCache[e.Name] = new Vector4(e.X / (float)e.AtlasW, y / (float)e.AtlasH,
                                              e.W / (float)e.AtlasW, e.H / (float)e.AtlasH);
            }

            if (_world == null) Debug.LogError("[Art] missing Resources/Art/atlas_world.png");
            if (_ui == null) Debug.LogError("[Art] missing Resources/Art/atlas_ui.png");
            if (_enemies == null && EnemyArt.Entries.Length > 0)
                Debug.LogError("[Art] missing Resources/Art/atlas_enemies.png");
            if (_vfx == null && ArtVfx.Entries.Length > 0)
                Debug.LogError("[Art] missing Resources/Art/atlas_vfx.png");
        }

        public static bool Has(string name)
        {
            Preload();
            return Index.ContainsKey(name);
        }

        public static Texture2D AtlasOf(string name)
        {
            Preload();
            if (!Index.TryGetValue(name, out var e)) return _world;
            switch (e.Atlas)
            {
                case "ui": return _ui;
                case "enemies": return _enemies != null ? _enemies : _world;
                case "vfx": return _vfx != null ? _vfx : _world;
                default: return _world;
            }
        }

        /// <summary>Which atlas a sprite lives in ("ui" / "world" / "enemies").</summary>
        public static string AtlasKey(string name)
        {
            Preload();
            return Index.TryGetValue(name, out var e) ? e.Atlas : "world";
        }

        /// <summary>Pixel size of a sprite, for billboard aspect ratio.</summary>
        public static bool TrySize(string name, out Vector2 size)
        {
            Preload();
            if (SizeCache.TryGetValue(name, out size)) return true;
            if (!Index.TryGetValue(name, out var e)) { size = Vector2.one; return false; }
            size = new Vector2(e.W, e.H);
            SizeCache[name] = size;
            return true;
        }

        public static bool TryUv(string name, out Vector4 uv)
        {
            Preload();
            return UvCache.TryGetValue(name, out uv);
        }

        /// <summary>Sprite for UGUI, with optional 9-slice border (left, bottom, right, top).</summary>
        public static Sprite Get(string name, Vector4 border = default)
        {
            Preload();
            if (!Index.TryGetValue(name, out var e))
            {
                Debug.LogWarning("[Art] no sprite named " + name);
                return GetMissing();
            }

            string key = border == default ? name : name + "#b" + border.x + "," + border.y;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = e.Atlas == "ui" ? _ui : _world;
            if (tex == null) return GetMissing();

            float y = e.AtlasH - e.Y - e.H;
            var sprite = Sprite.Create(
                tex,
                new Rect(e.X, y, e.W, e.H),
                new Vector2(e.Pivot.x, e.Pivot.y),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name;
            Cache[key] = sprite;
            return sprite;
        }

        static Sprite GetMissing()
        {
            if (_missing == null)
            {
                var t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 255, 255);
                t.SetPixels32(px);
                t.Apply();
                _missingTex = t;
                _missing = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            }
            return _missing;
        }
    }
}

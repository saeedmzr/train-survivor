using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// One camera-facing quad showing a sprite from the atlas. World sprites are drawn
    /// bottom-pivot so they stand on the track like cardboard cut-outs: depth-tested,
    /// unlit, tinted and flashable per instance without touching the shared material.
    /// </summary>
    public sealed class SpriteBillboard : MonoBehaviour
    {
        static Mesh _quad;
        static Material _material;
        static Material _addMaterial;
        static readonly Dictionary<string, Vector2> AspectCache = new Dictionary<string, Vector2>(64);

        /// <summary>Set once by the bootstrap so 60 billboards don't call Camera.main.</summary>
        public static Transform CameraTransform;

        MeshRenderer _mr;
        MaterialPropertyBlock _block;
        Texture2D _texture;
        Vector4 _uv = new Vector4(0f, 0f, 1f, 1f);
        string _spriteName;
        float _height = 2f;
        Color _tint = Color.white;
        float _flash;

        public bool Visible => gameObject.activeSelf;
        public string SpriteName => _spriteName;

        /// <summary>Roll around the view axis, degrees. Applied on top of the camera facing.</summary>
        public float Tilt;

        /// <summary>Non-uniform scale multiplier, applied on top of the sprite's base scale.</summary>
        public Vector2 Squash = Vector2.one;

        /// <summary>Scale set by the last SetSprite call, before any per-frame squash.</summary>
        public Vector3 BaseScale { get; private set; } = Vector3.one;

        public static Material SharedMaterial()
        {
            if (_material != null) return _material;

            var shader = Resources.Load<Shader>("Art/Sprite3D");
            if (shader == null) shader = Shader.Find("TrainSurvivor/Sprite3D");
            _material = new Material(shader) { name = "Sprite3D (shared)" };
            _material.SetTexture("_BaseMap", ArtSprites.WorldAtlas);
            return _material;
        }

        /// <summary>Additive variant (TrainSurvivor/Sprite3DAdd) for bullets and glows.</summary>
        public static Material AdditiveMaterial()
        {
            if (_addMaterial != null) return _addMaterial;

            var shader = Resources.Load<Shader>("Art/Sprite3DAdd");
            if (shader == null) shader = Shader.Find("TrainSurvivor/Sprite3DAdd");
            if (shader == null) return SharedMaterial();

            _addMaterial = new Material(shader) { name = "Sprite3DAdd (shared)" };
            _addMaterial.SetTexture("_BaseMap", ArtSprites.VfxAtlas != null ? ArtSprites.VfxAtlas
                                                                           : ArtSprites.WorldAtlas);
            return _addMaterial;
        }

        static Mesh Quad()
        {
            if (_quad != null) return _quad;

            _quad = new Mesh { name = "BillboardQuad" };
            // Bottom-centre pivot: (-0.5,0) .. (0.5,1), facing +Z.
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 1f, 0f),
                new Vector3(-0.5f, 1f, 0f)
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
            _quad.RecalculateBounds();
            return _quad;
        }

        public static SpriteBillboard Create(Transform parent, string name, bool additive = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var bb = go.AddComponent<SpriteBillboard>();
            bb._mr = go.AddComponent<MeshRenderer>();
            bb._mr.sharedMaterial = additive ? AdditiveMaterial() : SharedMaterial();
            bb._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bb._mr.receiveShadows = false;
            bb._mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            bb._block = new MaterialPropertyBlock();
            go.SetActive(false);
            return bb;
        }

        public void Show(string spriteName, float heightMeters, Color tint, float flash = 0f)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            SetSprite(spriteName, heightMeters);
            SetLook(tint, flash);
        }

        public void SetSprite(string spriteName, float heightMeters)
        {
            if (spriteName == _spriteName && Mathf.Approximately(heightMeters, _height)) return;

            bool changedTexture = _spriteName == null || SpriteAtlas(spriteName) != SpriteAtlas(_spriteName);
            _spriteName = spriteName;
            _height = heightMeters;

            if (!ArtSprites.TryUv(spriteName, out var uv))
            {
                _uv = new Vector4(0f, 0f, 1f, 1f);
                BaseScale = new Vector3(heightMeters, heightMeters, 1f);
            }
            else
            {
                _uv = uv;
                var aspect = Aspect(spriteName);
                BaseScale = new Vector3(heightMeters * aspect.x, heightMeters * aspect.y, 1f);
            }
            transform.localScale = BaseScale;

            if (changedTexture) _texture = ArtSprites.AtlasOf(spriteName);
            Apply();
        }

        static string SpriteAtlas(string name) => ArtSprites.AtlasKey(name);

        static Vector2 Aspect(string name)
        {
            if (AspectCache.TryGetValue(name, out var a)) return a;
            a = ArtSprites.TrySize(name, out var px) && px.y > 0f
                ? new Vector2(px.x / px.y, 1f)
                : Vector2.one;
            AspectCache[name] = a;
            return a;
        }

        public void SetLook(Color tint, float flash)
        {
            if (_tint == tint && Mathf.Approximately(_flash, flash)) return;
            _tint = tint;
            _flash = Mathf.Clamp01(flash);
            Apply();
        }

        void Apply()
        {
            if (_block == null) return;
            _block.Clear();
            if (_texture != null) _block.SetTexture("_BaseMap", _texture);
            _block.SetVector("_UVRect", _uv);
            _block.SetColor("_Color", _tint);
            _block.SetFloat("_Flash", _flash);
            _mr.SetPropertyBlock(_block);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (CameraTransform != null)
                transform.rotation = CameraTransform.rotation * Quaternion.Euler(0f, 0f, Tilt);

            if (!Mathf.Approximately(Squash.x, 1f) || !Mathf.Approximately(Squash.y, 1f))
                transform.localScale = new Vector3(BaseScale.x * Squash.x, BaseScale.y * Squash.y, 1f);
        }
    }
}

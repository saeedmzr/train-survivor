using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// One pooled tracer quad, drawn from the VFX atlas and oriented along its own flight
    /// direction instead of facing the camera flat-on. The sprite art (proj_bullet_tracer)
    /// is a streak with the bright slug on +X and a taper behind it, so aligning local X to
    /// the round's direction gives a short tapered tracer that still reads side-on from the
    /// chase camera. No allocation after Create: the driver reuses the pool.
    /// </summary>
    public sealed class TracerView : MonoBehaviour
    {
        static Mesh _quad;
        static Material _material;

        MeshRenderer _mr;
        MaterialPropertyBlock _block;
        Color _color = Color.white;
        float _life, _maxLife;
        public float Length = 2.6f;
        public float Thickness = 0.20f;

        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "TracerQuad" };
            // Centre pivot, 1x1 plane in XY, +X = head.
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _quad.RecalculateBounds();
            return _quad;
        }

        static Material Mat()
        {
            if (_material != null) return _material;
            var shader = Resources.Load<Shader>("Art/Sprite3DAdd");
            if (shader == null) shader = Shader.Find("TrainSurvivor/Sprite3DAdd");
            if (shader == null) shader = Shader.Find("TrainSurvivor/Sprite3D");
            _material = new Material(shader) { name = "TracerAdd (shared)" };
            _material.SetTexture("_BaseMap", ArtSprites.VfxAtlas);
            return _material;
        }

        public static TracerView Create(Transform parent, string name, string sprite)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var t = go.AddComponent<TracerView>();
            t._mr = go.AddComponent<MeshRenderer>();
            t._mr.sharedMaterial = Mat();
            t._mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            t._mr.receiveShadows = false;
            t._mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            t._block = new MaterialPropertyBlock();

            if (ArtSprites.TryUv(sprite, out var uv))
            {
                t._block.SetVector("_UVRect", uv);
                var tex = ArtSprites.AtlasOf(sprite);
                if (tex != null) t._block.SetTexture("_BaseMap", tex);
            }
            go.SetActive(false);
            return t;
        }

        public void Show(Vector3 head, Vector3 dir, Camera cam, float length, float thickness,
                         Color color, float life, float alpha)
        {
            Length = length;
            Thickness = thickness;
            _maxLife = Mathf.Max(0.01f, life);
            _life = _maxLife;
            _color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Place(head, dir, cam);
            Apply();
        }

        /// <summary>
        /// Camera-facing streak along the round's screen-space flight direction. A true
        /// 3D quad degenerates when the shot flies away from the chase camera (the common
        /// case): its plane turns edge-on and the tracer collapses to a dot. Billboarding
        /// the quad and rolling it to the projected direction keeps a readable streak at
        /// every angle; the length is foreshortened but never below half, so a shot going
        /// straight down-range still reads as a tracer.
        /// </summary>
        public void Place(Vector3 head, Vector3 dir, Camera cam)
        {
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();

            Vector3 view, right, up;
            if (cam != null)
            {
                view = cam.transform.forward;
                right = cam.transform.right;
                up = cam.transform.up;
            }
            else
            {
                view = Vector3.back;
                right = Vector3.right;
                up = Vector3.up;
            }

            float vx = Vector3.Dot(dir, right);
            float vy = Vector3.Dot(dir, up);
            float mag = Mathf.Sqrt(vx * vx + vy * vy);
            Vector3 projDir = mag > 0.001f ? (right * vx + up * vy) / mag : up;
            if (projDir.sqrMagnitude < 1e-4f) projDir = up;

            float len = Length * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(mag));
            Vector3 center = head - projDir * (len * 0.5f);

            transform.position = center;
            // local X = flight direction on screen, local Z = view axis
            transform.rotation = Quaternion.LookRotation(view, Vector3.Cross(view, projDir));
            transform.localScale = new Vector3(len, Thickness, 1f);
        }

        public void Tick(float dt)
        {
            if (_life <= 0f) return;
            _life -= dt;
            if (_life <= 0f) { Hide(); return; }
            float k = _life / _maxLife;
            _color.a = Mathf.Clamp01(k * 1.4f);
            Apply();
        }

        /// <summary>Held for the round's whole flight: the streak stays solid until it lands.</summary>
        public void KeepAlive()
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            _life = _maxLife;
            _color.a = 1f;
            Apply();
        }

        /// <summary>After the round lands, let the last streak cool off instead of popping.</summary>
        public void FadeOut(float seconds)
        {
            _maxLife = Mathf.Max(0.01f, seconds);
            _life = _maxLife;
        }

        void Apply()
        {
            _block.SetColor("_Color", _color);
            _mr.SetPropertyBlock(_block);
        }

        public void Hide()
        {
            _life = 0f;
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}

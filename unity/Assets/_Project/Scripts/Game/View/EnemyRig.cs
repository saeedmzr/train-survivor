using UnityEngine;
using UnityEngine.Rendering;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Runtime skeletal enemy: one sprite per species, a bone hierarchy built from
    /// <see cref="EnemyRigData"/>, linear-blend skinning and a procedural gait that rotates joints.
    ///
    /// Why CPU skinning and not SpriteSkin: Unity 2D Animation's SpriteSkin needs skin data that is
    /// baked into a *sprite asset at import time* (its vertex/weight setters only exist in the editor
    /// importer path, and a runtime Sprite.Create() result cannot receive them). The maths below is
    /// the same linear-blend skinning SpriteSkin runs — bone matrix x bind pose, four influences —
    /// so swapping in SpriteSkin later is a renderer change, not a rig change.
    ///
    /// The rig billboards (root copies the camera rotation), so every bone moves in the sprite's
    /// screen plane: Z rotation plus X/Y translation.
    /// </summary>
    public sealed class EnemyRig : MonoBehaviour
    {
        const float PPU = 100f;
        const int Cols = 14;
        const int Rows = 14;
        const int MaxInfluences = 4;

        public Transform Pose;              // gait offsets (bob, sway, tilt)
        public Transform[] Bones;
        public EnemyRigData.Gait Gait;
        public float AmpScale = 1f;         // dev.rigAmp — live exaggeration knob

        ArtAtlas.Entry _entry;
        Vector3[] _jointPos;                // rig-local joint positions
        Matrix4x4[] _bindPoses;
        Vector3[] _verts;
        Vector2[] _uvs;
        int[] _tris;
        BoneWeight[] _weights;
        Vector3[] _skinned;
        Mesh _mesh;
        MeshRenderer _mr;
        MaterialPropertyBlock _block;
        float _phase;

        static Material _material;

        public static Material SharedMaterial()
        {
            if (_material != null) return _material;

            var shader = Resources.Load<Shader>("Art/Sprite3D");
            if (shader == null) shader = Shader.Find("TrainSurvivor/Sprite3D");
            _material = new Material(shader) { name = "Sprite3D (rig)" };
            _material.SetTexture("_BaseMap", ArtSprites.EnemyAtlas);
            _material.SetVector("_UVRect", new Vector4(0f, 0f, 1f, 1f));
            return _material;
        }

        static bool TryFindEntry(string name, out ArtAtlas.Entry entry)
        {
            var all = EnemyArt.Entries;
            for (int i = 0; i < all.Length; i++)
                if (all[i].Name == name) { entry = all[i]; return true; }
            entry = default;
            return false;
        }

        public static EnemyRig Create(Transform parent, EnemyRigData.Rig data)
        {
            ArtSprites.Preload();

            if (!TryFindEntry(data.Sprite, out var entry))
            {
                Debug.LogError("[Rig] no atlas entry for " + data.Sprite);
                return null;
            }

            float w = entry.W / PPU, h = entry.H / PPU;

            var go = new GameObject("Rig:" + data.Id);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * (data.HeightM / h);

            var rig = go.AddComponent<EnemyRig>();
            rig.Gait = data.Gait;
            rig._entry = entry;

            var pose = new GameObject("Pose").transform;
            pose.SetParent(go.transform, false);
            rig.Pose = pose;

            rig.BuildBones(data, w, h);
            rig.BuildGeometry(w, h);
            rig.BuildMesh();
            return rig;
        }

        void BuildBones(EnemyRigData.Rig data, float w, float h)
        {
            var joints = data.Joints;
            Bones = new Transform[joints.Length];
            _jointPos = new Vector3[joints.Length];

            for (int i = 0; i < joints.Length; i++)
            {
                var j = joints[i];
                _jointPos[i] = new Vector3((j.X - 0.5f) * w, j.Y * h, 0f);

                var t = new GameObject(j.Name).transform;
                if (j.Parent < 0)
                {
                    t.SetParent(Pose, false);
                    t.localPosition = _jointPos[i];
                }
                else
                {
                    t.SetParent(Bones[j.Parent], false);
                    t.localPosition = _jointPos[i] - _jointPos[j.Parent];
                }
                Bones[i] = t;
            }
        }

        void BuildGeometry(float w, float h)
        {
            int n = (Cols + 1) * (Rows + 1);
            _verts = new Vector3[n];
            _uvs = new Vector2[n];
            _skinned = new Vector3[n];
            _weights = new BoneWeight[n];
            _tris = new int[Cols * Rows * 6];

            float u0 = _entry.X / (float)_entry.AtlasW;
            float v0 = (_entry.AtlasH - _entry.Y - _entry.H) / (float)_entry.AtlasH;
            float du = _entry.W / (float)_entry.AtlasW;
            float dv = _entry.H / (float)_entry.AtlasH;

            for (int y = 0; y <= Rows; y++)
            {
                float fy = y / (float)Rows;
                for (int x = 0; x <= Cols; x++)
                {
                    float fx = x / (float)Cols;
                    int i = y * (Cols + 1) + x;
                    _verts[i] = new Vector3((fx - 0.5f) * w, fy * h, 0f);
                    _uvs[i] = new Vector2(u0 + fx * du, v0 + fy * dv);
                }
            }

            int t = 0;
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Cols; x++)
                {
                    int i = y * (Cols + 1) + x;
                    _tris[t++] = i; _tris[t++] = i + Cols + 1; _tris[t++] = i + 1;
                    _tris[t++] = i + 1; _tris[t++] = i + Cols + 1; _tris[t++] = i + Cols + 2;
                }
            }

            _bindPoses = new Matrix4x4[Bones.Length];
            var rootWorld = transform.localToWorldMatrix;
            for (int i = 0; i < Bones.Length; i++)
                _bindPoses[i] = Bones[i].worldToLocalMatrix * rootWorld;

            BuildWeights();
        }

        /// <summary>Inverse-square distance to each bone segment, top four, normalised.</summary>
        void BuildWeights()
        {
            int nb = Bones.Length;
            var segA = new Vector3[nb];
            var segB = new Vector3[nb];
            for (int i = 0; i < nb; i++)
            {
                int parent = IndexOfParent(i);
                segB[i] = _jointPos[i];
                segA[i] = parent >= 0 ? _jointPos[parent] : _jointPos[i];
            }

            var score = new float[nb];

            for (int v = 0; v < _verts.Length; v++)
            {
                var p = _verts[v];
                for (int b = 0; b < nb; b++)
                {
                    Vector3 ab = segB[b] - segA[b];
                    float tt = Mathf.Clamp01(Vector3.Dot(p - segA[b], ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    float d = Vector3.Distance(p, segA[b] + ab * tt);
                    score[b] = 1f / (d * d + 4e-4f);
                }

                int i0 = 0, i1 = 0, i2 = 0, i3 = 0;
                float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f, sum = 0f;
                for (int k = 0; k < MaxInfluences; k++)
                {
                    int best = -1; float bv = -1f;
                    for (int b = 0; b < nb; b++)
                        if (score[b] > bv) { bv = score[b]; best = b; }
                    if (best < 0) break;
                    if (k == 0) { i0 = best; w0 = bv; }
                    else if (k == 1) { i1 = best; w1 = bv; }
                    else if (k == 2) { i2 = best; w2 = bv; }
                    else { i3 = best; w3 = bv; }
                    sum += bv;
                    score[best] = -1f;
                }
                if (sum <= 0f) { i0 = 0; w0 = 1f; sum = 1f; }

                _weights[v] = new BoneWeight
                {
                    boneIndex0 = i0, weight0 = w0 / sum,
                    boneIndex1 = i1, weight1 = w1 / sum,
                    boneIndex2 = i2, weight2 = w2 / sum,
                    boneIndex3 = i3, weight3 = w3 / sum
                };
            }
        }

        int IndexOfParent(int bone)
        {
            if (Bones[bone].parent == Pose) return -1;
            for (int i = 0; i < Bones.Length; i++)
                if (Bones[i] == Bones[bone].parent) return i;
            return -1;
        }

        void BuildMesh()
        {
            var go = new GameObject("Skinned");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            _mr = go.AddComponent<MeshRenderer>();
            _mr.sharedMaterial = SharedMaterial();
            _mr.shadowCastingMode = ShadowCastingMode.Off;
            _mr.receiveShadows = false;

            _mesh = new Mesh { name = "EnemyRig-" + _entry.Name };
            _mesh.MarkDynamic();
            _mesh.vertices = _verts;
            _mesh.uv = _uvs;
            _mesh.triangles = _tris;
            _mesh.RecalculateBounds();
            mf.sharedMesh = _mesh;

            _block = new MaterialPropertyBlock();
        }

        /// <summary>Advance and apply the gait.</summary>
        public void Tick(float dt, float speed, float urgency, Color tint, float flash)
        {
            _phase += dt * (2.2f + Mathf.Abs(speed) * 0.5f);
            if (_phase > 1f) _phase -= 1f;

            ApplyPose(_phase, urgency);
            ApplyLook(tint, flash);
        }

        void ApplyPose(float phase, float urgency)
        {
            float p = phase * Mathf.PI * 2f;
            var g = Gait;
            float amp = (0.85f + 0.5f * Mathf.Clamp01(urgency)) * AmpScale;

            Pose.localPosition = new Vector3(
                g.SwayM * amp * Mathf.Sin(p + 0.4f),
                g.BobM * amp * (0.5f - 0.5f * Mathf.Cos(2f * p)),
                0f);
            Pose.localRotation = Quaternion.Euler(0f, 0f, g.RootTiltDeg * amp * Mathf.Sin(p));

            Rot(0, 0f);
            Rot(1, g.SpineDeg * amp * Mathf.Sin(p + 0.5f));
            Rot(2, g.ChestDeg * amp * Mathf.Sin(p + 1.1f));
            Rot(3, -0.45f * g.HeadDeg * amp * Mathf.Sin(p + 1.3f));
            Rot(4, g.HeadDeg * amp * Mathf.Sin(p + 1.3f + g.HeadLag));

            Rot(5, g.ArmSwingDeg * amp * Mathf.Sin(p));
            Rot(6, g.ElbowDeg * (0.6f + 0.4f * Mathf.Sin(p + 0.8f)));
            Rot(7, 0f);
            Rot(8, -g.ArmSwingDeg * g.Asymmetry * amp * Mathf.Sin(p));
            Rot(9, -g.ElbowDeg * g.Asymmetry * (0.6f + 0.4f * Mathf.Sin(p + 0.8f)));
            Rot(10, 0f);

            Rot(11, g.HipDeg * amp * Mathf.Sin(p + Mathf.PI));
            Rot(12, g.KneeDeg * (0.5f + 0.5f * Mathf.Sin(p + Mathf.PI)));
            Rot(13, -g.HipDeg * amp * Mathf.Sin(p + Mathf.PI));
            Rot(14, g.KneeDeg * (0.5f + 0.5f * Mathf.Sin(p + Mathf.PI)));

            SkinCpu();
        }

        void Rot(int i, float zDeg)
        {
            if (Bones == null || i >= Bones.Length) return;
            Bones[i].localRotation = Quaternion.Euler(0f, 0f, zDeg);
        }

        void SkinCpu()
        {
            var rootInv = transform.localToWorldMatrix.inverse;
            for (int b = 0; b < Bones.Length; b++)
                _mat[b] = rootInv * Bones[b].localToWorldMatrix * _bindPoses[b];

            for (int v = 0; v < _verts.Length; v++)
            {
                var w = _weights[v];
                var src = _verts[v];
                Vector3 p = Vector3.zero;
                if (w.weight0 > 0f) p += _mat[w.boneIndex0].MultiplyPoint3x4(src) * w.weight0;
                if (w.weight1 > 0f) p += _mat[w.boneIndex1].MultiplyPoint3x4(src) * w.weight1;
                if (w.weight2 > 0f) p += _mat[w.boneIndex2].MultiplyPoint3x4(src) * w.weight2;
                if (w.weight3 > 0f) p += _mat[w.boneIndex3].MultiplyPoint3x4(src) * w.weight3;
                _skinned[v] = p;
            }

            _mesh.vertices = _skinned;
            _mesh.RecalculateBounds();
        }

        readonly Matrix4x4[] _mat = new Matrix4x4[24];

        void ApplyLook(Color tint, float flash)
        {
            if (_block == null) return;
            _block.Clear();
            _block.SetColor("_Color", tint);
            _block.SetFloat("_Flash", Mathf.Clamp01(flash));
            _mr.SetPropertyBlock(_block);
        }

        /// <summary>Billboard: copy the camera rotation, like SpriteBillboard does.</summary>
        public void FaceCamera(Transform cam)
        {
            if (cam != null) transform.rotation = cam.rotation;
        }

        public int BoneCount => Bones != null ? Bones.Length : 0;
    }
}

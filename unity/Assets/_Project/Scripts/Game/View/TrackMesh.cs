using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>Builds the rail, sleepers and ground as static meshes — 3 draw calls total.</summary>
    public static class TrackMesh
    {
        const float BallastHalfWidth = 3.1f;
        const float SampleStep = 2f;
        // Rails must exist under the rear freight cars at a fresh start (S=0). The longest
        // convoy is 6 wagons, so its tail is about -43 m; -70 leaves a visible safety margin.
        const float TrackStart = -70f;
        const float TrackEnd = TrackPath.Length + 40f;

        public static void Build(Transform parent)
        {
            BuildGround(parent);
            BuildRibbon(parent, "Ballast", BallastHalfWidth, 0f, 0f,
                        Textured("Ballast", Palette.Ballast, "ballast"));

            // Steel and sleepers now come from the supplied models: the rail is the measured
            // cross-section swept along the route, the sleepers are the timber prefab with its
            // baseplates straddling the rail feet. The old flat rail/web ribbons and the flat
            // sleeper quads are retired here.
            TrackRails.Build(parent, TrackStart, TrackEnd);
            TrackSleepers.Build(parent, TrackStart, TrackPath.Length);
        }

        static void BuildGround(Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, -0.03f, TrackPath.Length * 0.5f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = "GroundMesh" };

            // Cover the full route plus every side approach and rear chase position.
            // Distance blending belongs in the terrain shader, never at a mesh edge.
            float half = TrackPath.Length * 0.5f + 600f;
            var verts = new[]
            {
                new Vector3(-half, 0f, -half), new Vector3(-half, 0f, half),
                new Vector3(half, 0f, half),   new Vector3(half, 0f, -half)
            };
            mf.sharedMesh.vertices = verts;
            mf.sharedMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mf.sharedMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };

            // the quad had no UVs at all, so any ground texture sampled a single texel; wrap one
            // tile every 6 m so gravel reads at train scale
            const float tile = 6f;
            mf.sharedMesh.uv = new[]
            {
                new Vector2(-half / tile, -half / tile), new Vector2(-half / tile, half / tile),
                new Vector2(half / tile, half / tile),   new Vector2(half / tile, -half / tile)
            };
            mf.sharedMesh.RecalculateBounds();

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Textured("Ground", Palette.Ground, "yard_ground", 26f);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Material with a tiling ground texture at world scale; cached per slot so the
        /// whole district still batches into the same few draw calls.</summary>
        static readonly System.Collections.Generic.Dictionary<string, Material> _mats =
            new System.Collections.Generic.Dictionary<string, Material>();

        static Material Textured(string slot, Color fallback, string tex, float repeat = 0f)
        {
            if (_mats.TryGetValue(slot, out var cached)) return cached;
            // Never mutate the saved Resources material. Both terrain surfaces share the
            // same URP distance path; rails, webs and sleepers retain their own materials.
            var m = new Material(Palette.Get(slot, fallback));
            m.shader = Resources.Load<Shader>("Shaders/TerrainDistance");
            // DistrictScenery supplies the existing gravel texture and resets _BaseColor.
            // Keep the packed-yard tint separate so that dressing cannot erase it.
            m.SetColor("_TerrainTint", slot == "Ground" ? Palette.Ground / 0.34f : Color.white);
            var t = Resources.Load<Texture2D>("Textures/" + tex);
            if (t != null)
            {
                m.mainTexture = t;
                m.mainTextureScale = repeat > 0f ? new Vector2(repeat, repeat) : Vector2.one;
                m.color = Color.white;          // texture carries the colour
            }
            _mats[slot] = m;
            return m;
        }

        /// <summary>One quad strip, optionally mirrored to the left of the centerline.</summary>
        static void BuildRibbon(Transform parent, string name, float halfWidth, float inset, float y, Material mat)
        {
            int segments = Mathf.CeilToInt((TrackEnd - TrackStart) / SampleStep);
            var verts = new List<Vector3>(segments * 2 + 2);
            var tris = new List<int>(segments * 6);
            var normals = new List<Vector3>(segments * 2 + 2);
            var uvs = new List<Vector2>(segments * 2 + 2);

            for (int i = 0; i <= segments; i++)
            {
                float s = TrackStart + i * SampleStep;
                Vector3 c = TrackSpace.Point(s, inset, y);
                Vector3 r = TrackSpace.Right(s) * halfWidth;
                verts.Add(c - r);
                verts.Add(c + r);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, s / (halfWidth * 2.6f)));
                uvs.Add(new Vector2(1f, s / (halfWidth * 2.6f)));

                if (i == segments) continue;
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            AddMeshObject(parent, name + "_" + Mathf.RoundToInt(inset * 100f), verts, tris, normals, mat, uvs);
        }

        static void AddMeshObject(Transform parent, string name, List<Vector3> verts, List<int> tris,
                                  List<Vector3> normals, Material mat)
            => AddMeshObject(parent, name, verts, tris, normals, mat, null);

        static void AddMeshObject(Transform parent, string name, List<Vector3> verts, List<int> tris,
                                  List<Vector3> normals, Material mat, List<Vector2> uvs)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetNormals(normals);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>Builds the rail, sleepers and ground as static meshes — 3 draw calls total.</summary>
    public static class TrackMesh
    {
        const float BallastHalfWidth = 3.1f;
        const float RailHalfWidth = 0.075f;
        const float RailInset = 0.72f;
        const float SampleStep = 2f;

        public static void Build(Transform parent)
        {
            BuildGround(parent);
            BuildRibbon(parent, "Ballast", BallastHalfWidth, 0f, 0f,
                        Palette.Get("Ballast", Palette.Ballast));
            BuildRibbon(parent, "RailLeft", RailHalfWidth, RailInset, 0.12f,
                        Palette.Get("Rail", Palette.Rail));
            BuildRibbon(parent, "RailRight", RailHalfWidth, -RailInset, 0.12f,
                        Palette.Get("Rail", Palette.Rail));
            BuildSleepers(parent);
        }

        static void BuildGround(Transform parent)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, -0.03f, TrackPath.Length * 0.5f);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = "GroundMesh" };

            float half = 1500f;
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
            mr.sharedMaterial = Palette.Get("Ground", Palette.Ground);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>One quad strip, optionally mirrored to the left of the centerline.</summary>
        static void BuildRibbon(Transform parent, string name, float halfWidth, float inset, float y, Material mat)
        {
            int segments = Mathf.CeilToInt((TrackPath.Length + 60f) / SampleStep);
            var verts = new List<Vector3>(segments * 2 + 2);
            var tris = new List<int>(segments * 6);
            var normals = new List<Vector3>(segments * 2 + 2);

            for (int i = 0; i <= segments; i++)
            {
                float s = i * SampleStep - 20f;
                Vector3 c = TrackSpace.Point(s, inset, y);
                Vector3 r = TrackSpace.Right(s) * halfWidth;
                verts.Add(c - r);
                verts.Add(c + r);
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);

                if (i == segments) continue;
                int b = i * 2;
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }

            AddMeshObject(parent, name + "_" + Mathf.RoundToInt(inset * 100f), verts, tris, normals, mat);
        }

        static void BuildSleepers(Transform parent)
        {
            const float spacing = 2.6f;
            int count = Mathf.CeilToInt(TrackPath.Length / spacing);
            var verts = new List<Vector3>(count * 4);
            var tris = new List<int>(count * 6);
            var normals = new List<Vector3>(count * 4);
            const float halfLen = 1.35f;
            const float halfDepth = 0.19f;

            for (int i = 0; i < count; i++)
            {
                float s = i * spacing;
                Vector3 c = TrackSpace.Point(s, 0f, 0.055f);
                Vector3 r = TrackSpace.Right(s) * halfLen;
                Vector3 f = TrackSpace.Right(s) * 0f + (TrackSpace.Point(s + halfDepth, 0f) - c);

                int b = verts.Count;
                verts.Add(c - r - f); verts.Add(c + r - f);
                verts.Add(c + r + f); verts.Add(c - r + f);
                for (int n = 0; n < 4; n++) normals.Add(Vector3.up);

                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }

            AddMeshObject(parent, "Sleepers", verts, tris, normals, Palette.Get("Sleeper", Palette.Sleeper));
        }

        static void AddMeshObject(Transform parent, string name, List<Vector3> verts, List<int> tris,
                                  List<Vector3> normals, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}

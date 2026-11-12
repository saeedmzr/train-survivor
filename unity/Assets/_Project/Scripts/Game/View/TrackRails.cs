using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Sweeps the supplied steel-rail cross-section along the existing route, so both rails are
    /// continuous surfaces: no piece seams, no faceting through curves, one draw call.
    ///
    /// The route, gauge and railhead height come from TrackMesh/TrackSpace — this class only
    /// replaces how the steel is drawn, never where it is.
    /// </summary>
    public static class TrackRails
    {
        /// <summary>Rail centre offset from the centreline: 1.44 m gauge.</summary>
        public const float Inset = 0.72f;
        /// <summary>Top of the railhead, matching the old flat ribbons so wheels still sit on it.</summary>
        public const float HeadTop = 0.12f;
        /// <summary>Rail foot height — the sleeper baseplate tops meet the rail here.</summary>
        public const float FootY = HeadTop - RailProfile.Height;

        static Vector2 _centroid;         // section centre, used to orient the ring normals
        static float _headMinY;           // top of the running surface, for the head/flank split

        const float Step = 2.5f;          // metres between sections; 1.3 cm chord error at 120 m radius
        const float UVPerMetre = 1f;      // one texture tile per metre travelled
        const float UVPerPerimeter = 1.6f;// keeps the web/foot mapping consistent with the length

        public static void Build(Transform parent, float start, float end)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            Sweep(verts, norms, uvs, tris, -Inset, start, end);
            Sweep(verts, norms, uvs, tris, +Inset, start, end);

            var go = new GameObject("Rails");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "RailSweep" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);

            // Two passes over the raw triangles:
            //  - winding: a quad emitted with the wrong handedness is invisible from outside, so
            //    each face normal is compared with its vertex normals and swapped if they disagree;
            //  - split: upward faces become the polished head, the rest keep the rusty flank
            //    material, which is what the lamps are supposed to pick out.
            var sides = new List<int>(tris.Count);
            var head = new List<int>(tris.Count / 4);
            for (int t = 0; t < tris.Count; t += 3)
            {
                int a0 = tris[t], a1 = tris[t + 1], a2 = tris[t + 2];
                Vector3 face = Vector3.Cross(verts[a1] - verts[a0], verts[a2] - verts[a0]);
                Vector3 vn = norms[a0] + norms[a1] + norms[a2];
                bool flip = Vector3.Dot(face, vn) < 0f;
                int i1 = flip ? a2 : a1, i2 = flip ? a1 : a2;
                // classify by height, not by normal: on the rounded shoulder the normals
                // alternate between top and flank, which broke the head into a dashed line
                bool isHead = verts[a0].y > _headMinY && verts[a1].y > _headMinY && verts[a2].y > _headMinY;
                var dst = isHead ? head : sides;
                dst.Add(a0); dst.Add(i1); dst.Add(i2);
            }

            mesh.subMeshCount = 2;
            mesh.SetTriangles(sides, 0);
            mesh.SetTriangles(head, 1);
            mesh.RecalculateBounds();

            Debug.Log(string.Format("[track] rail sweep verts {0} tris {1} spans {2:0} m head {3}",
                verts.Count, tris.Count / 3, end - start, head.Count / 3));
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { TrackMaterials.Rail(), TrackMaterials.RailHead() };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        static void Sweep(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                          List<int> tris, float inset, float start, float end)
        {
            int n = RailProfile.Count;
            int ring = n * 2;
            int sections = Mathf.Max(2, Mathf.CeilToInt((end - start) / Step));

            _headMinY = FootY + RailProfile.Height * 0.62f;
            _centroid = Vector2.zero;
            for (int i = 0; i < ring; i++) _centroid += ProfilePoint(i, 0);
            _centroid /= ring;

            // perimeter distance along the profile, so the texture does not stretch on the web
            var perim = new float[ring];
            {
                float acc = 0f;
                Vector2 prev = ProfilePoint(0, 0);
                for (int i = 1; i <= ring; i++)
                {
                    Vector2 cur = ProfilePoint(i % ring, 0);   // i == ring must wrap back to the start
                    acc += (cur - prev).magnitude;
                    perim[i % ring] = acc;
                    prev = cur;
                }
                for (int i = 0; i < ring; i++) perim[i] /= acc;
            }

            int baseIndex = verts.Count;

            for (int s = 0; s <= sections; s++)
            {
                float dist = start + (end - start) * s / sections;
                Vector3 centre = TrackSpace.Point(dist, inset, FootY);
                Vector3 right = TrackSpace.Right(dist);
                Vector3 up = Vector3.up;

                for (int i = 0; i < ring; i++)
                {
                    Vector2 p = ProfilePoint(i, 0);
                    Vector2 nrm = ProfileNormal(i);
                    // guarantee outward: a ring built in the wrong handedness would otherwise
                    // shade the whole rail from the inside and read as black steel
                    if (Vector2.Dot(nrm, p - _centroid) < 0f) nrm = -nrm;

                    verts.Add(centre + right * p.x + up * p.y);
                    norms.Add((right * nrm.x + up * nrm.y).normalized);
                    uvs.Add(new Vector2(perim[i] * UVPerPerimeter, dist * UVPerMetre));
                }
            }

            for (int s = 0; s < sections; s++)
            {
                int a = baseIndex + s * ring;
                int b = a + ring;
                for (int i = 0; i < ring; i++)
                {
                    int i2 = (i + 1) % ring;
                    tris.Add(a + i); tris.Add(b + i); tris.Add(a + i2);
                    tris.Add(a + i2); tris.Add(b + i); tris.Add(b + i2);
                }
            }
        }

        /// <summary>Point on the closed cross-section ring; 0..n-1 up the left wall, n..2n-1 down the right.</summary>
        static Vector2 ProfilePoint(int i, int _)
        {
            int n = RailProfile.Count;
            bool leftSide = i < n;
            int row = leftSide ? i : (2 * n - 1 - i);
            float z = RailProfile.Section[row, 0];
            float hw = RailProfile.Section[row, 1];
            // the section is symmetric: the extractor forces the centre to 0 so the rail runs
            // dead straight along its gauge line instead of following the scan's jitter
            return new Vector2(leftSide ? -hw : hw, z);
        }

        /// <summary>Outward normal of the ring at i, from the neighbours (counter-clockwise ring).</summary>
        static Vector2 ProfileNormal(int i)
        {
            int ring = RailProfile.Count * 2;
            Vector2 prev = ProfilePoint((i - 1 + ring) % ring, 0);
            Vector2 next = ProfilePoint((i + 1) % ring, 0);
            Vector2 t = (next - prev).normalized;
            return new Vector2(-t.y, t.x);
        }
    }
}

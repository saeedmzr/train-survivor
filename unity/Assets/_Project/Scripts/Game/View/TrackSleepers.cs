using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Places the supplied wooden sleeper (timber with metal baseplates) along the route as one
    /// combined mesh per 40 m chunk, and culls chunks beyond the fog distance.
    ///
    /// The prefab is scaled so the two baseplates straddle the rail feet exactly: the model's
    /// plate span is 0.6587 m and the route gauge is 1.44 m, so the scale is 1.44/0.6587.
    /// Verticals are scaled separately — the timber is nearly buried in ballast, as on a real
    /// bed, and the baseplate tops meet the rail foot at TrackRails.FootY.
    /// </summary>
    public static class TrackSleepers
    {
        /// <summary>Measured centre-to-centre distance of the two baseplates on the supplied model.</summary>
        const float ModelPlateSpan = 0.6587f;
        const float Pitch = 2.15f;          // existing sleeper pitch, unchanged
        const float TargetHeight = 0.20f;   // timber height above the ballast
        const float ChunkLength = 40f;
        const float CullDistance = 240f;    // beyond fog, so nothing pops

        public static void Build(Transform parent, float start, float end)
        {
            var prefab = Resources.Load<GameObject>("Track/railway_sleeper");
            if (prefab == null)
            {
                Debug.LogWarning("[track] Track/railway_sleeper missing");
                return;
            }

            var src = Sample(prefab, out Bounds local);
            if (src == null) { Debug.LogWarning("[track] sleeper has no mesh"); return; }

            float kxz = TrackRails.Inset * 2f / ModelPlateSpan;
            float ky = TargetHeight / Mathf.Max(0.01f, local.size.y);
            var scale = new Vector3(kxz, ky, kxz);
            // baseplate tops must land on the rail foot; the model's origin is not guaranteed
            // to sit at its underside, so offset by the measured bottom.
            float bottomOffset = -local.min.y * ky;

            Debug.Log(string.Format(
                "[track] sleeper model {0:0.00} x {1:0.00} x {2:0.00} m, scale {3:0.000}/{4:0.000}, plates at +/-{5:0.000} m",
                local.size.x, local.size.y, local.size.z, kxz, ky, TrackRails.Inset));

            var culler = parent.gameObject.GetComponent<TrackChunkCuller>();
            if (culler == null) culler = parent.gameObject.AddComponent<TrackChunkCuller>();

            int chunks = Mathf.CeilToInt((end - start) / ChunkLength);
            int total = 0;
            for (int c = 0; c < chunks; c++)
            {
                float cs = start + c * ChunkLength;
                float ce = Mathf.Min(end, cs + ChunkLength);
                int first = Mathf.CeilToInt((cs - start) / Pitch);
                int last = Mathf.FloorToInt((ce - start) / Pitch);

                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();

                for (int i = first; i <= last; i++)
                {
                    float s = start + i * Pitch;
                    Vector3 pos = TrackSpace.Point(s, 0f, TrackRails.FootY - TargetHeight + bottomOffset);
                    Quaternion rot = TrackSpace.Facing(s);
                    Append(src, verts, norms, uvs, tris, pos, rot, scale);
                    total++;
                }

                if (tris.Count == 0) continue;
                var go = new GameObject("Sleepers_" + c);
                go.transform.SetParent(parent, false);
                var mesh = new Mesh { name = "SleeperChunk" + c };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = TrackMaterials.Sleeper();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = true;
                culler.Add(go.transform, cs);
            }

            Debug.Log("[track] sleepers " + total + " in " + chunks + " chunks");
        }

        sealed class Piece { public Mesh mesh; public Matrix4x4 m; }

        static List<Piece> Sample(GameObject prefab, out Bounds localBounds)
        {
            var inst = Object.Instantiate(prefab);
            var mfs = inst.GetComponentsInChildren<MeshFilter>(true);
            var pieces = new List<Piece>();
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;

            var toRoot = inst.transform.worldToLocalMatrix;
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh == null) continue;
                var m = toRoot * mf.transform.localToWorldMatrix;
                if (!mf.sharedMesh.isReadable) continue;
                pieces.Add(new Piece { mesh = mf.sharedMesh, m = m });

                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(vs[i]);
                    if (first) { localBounds = new Bounds(p, Vector3.zero); first = false; }
                    else localBounds.Encapsulate(p);
                }
            }
            Object.Destroy(inst);
            return pieces.Count > 0 ? pieces : null;
        }

        static void Append(List<Piece> src, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs,
                           List<int> tris, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var trs = Matrix4x4.TRS(pos, rot, scale);
            foreach (var p in src)
            {
                Matrix4x4 m = trs * p.m;
                int b = verts.Count;
                var vs = p.mesh.vertices;
                var ns = p.mesh.normals;
                var u = p.mesh.uv;
                var ts = p.mesh.triangles;

                for (int i = 0; i < vs.Length; i++)
                {
                    verts.Add(m.MultiplyPoint3x4(vs[i]));
                    norms.Add(i < ns.Length ? m.MultiplyVector(ns[i]).normalized : Vector3.up);
                    uvs.Add(i < u.Length ? u[i] : Vector2.zero);
                }
                for (int i = 0; i < ts.Length; i++) tris.Add(b + ts[i]);
            }
        }
    }

    /// <summary>Enables sleeper chunks near the camera and hides the rest (fog is 42-165 m).</summary>
    public sealed class TrackChunkCuller : MonoBehaviour
    {
        readonly List<Transform> _chunks = new List<Transform>();
        readonly List<float> _s = new List<float>();

        public void Add(Transform t, float s) { _chunks.Add(t); _s.Add(s); }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i] == null) continue;
                float d = (_chunks[i].position - p).magnitude;
                bool on = d < 240f;
                if (_chunks[i].gameObject.activeSelf != on) _chunks[i].gameObject.SetActive(on);
            }
        }
    }
}

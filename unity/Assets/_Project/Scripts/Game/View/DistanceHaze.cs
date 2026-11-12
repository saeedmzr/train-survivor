using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Graded, unlit haze shell around the camera. The scene's distant ground ignored the
    /// fog (its material does not sample it), so the horizon showed a hard pale-to-near-black
    /// seam. This shell sits far behind every gameplay element and blends sky into distance
    /// without touching the track, props or collision.
    /// </summary>
    public sealed class DistanceHaze : MonoBehaviour
    {
        const float Radius = 150f;
        const int Segments = 48;

        public static void Attach(Camera cam)
        {
            if (cam == null) return;
            if (cam.transform.Find("HazeShell") != null) return;

            var go = new GameObject("HazeShell");
            go.transform.SetParent(cam.transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = Unlit();

            var verts = new System.Collections.Generic.List<Vector3>();
            var cols = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();

            // three stacked rings: low band darker (ground contact), top melting into the sky
            float[] heights = { -6f, 12f, 34f, 70f };
            Color[] tone = {
                new Color(0.30f, 0.325f, 0.37f),
                new Color(0.40f, 0.43f, 0.485f),
                new Color(0.475f, 0.51f, 0.565f),
                new Color(0.50f, 0.545f, 0.60f)
            };

            for (int h = 0; h < heights.Length; h++)
            {
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i / (float)Segments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * Radius, heights[h], Mathf.Sin(a) * Radius));
                    cols.Add(tone[h]);
                }
            }
            int stride = Segments + 1;
            for (int h = 0; h < heights.Length - 1; h++)
                for (int i = 0; i < Segments; i++)
                {
                    int b = h * stride + i, t = b + stride;
                    tris.Add(b); tris.Add(t); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(t); tris.Add(t + 1);
                }

            var mesh = new Mesh { name = "HazeShell" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mf.sharedMesh = mesh;
        }

        static Material Unlit()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "HazeShell" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            m.SetInt("_Cull", 0);                       // visible from inside
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = 2990;                        // after opaque, before overlays
            return m;
        }
    }
}

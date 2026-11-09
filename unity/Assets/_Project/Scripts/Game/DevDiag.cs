using System.Text;
using UnityEngine;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Dev-only probe: gated behind <c>dev.diag=1</c> in tune.txt, dumps the render state of the
    /// bootstrapped scene to the log once (and every <c>dev.diagRepeat</c> seconds if set), so a
    /// device build can be compared against the editor without a debugger.
    /// </summary>
    public sealed class DevDiag : MonoBehaviour
    {
        float _next;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Mathf.Max(1f, DevTuning.Get("dev.diagRepeat", 0f));
            Dump();
            if (DevTuning.Get("dev.diagRepeat", 0f) <= 0f) enabled = false;
        }

        void Dump()
        {
            var sb = new StringBuilder();
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            sb.AppendLine("[DIAG] pipeline=" + (rp != null ? rp.GetType().Name + ":" + rp.name : "BUILT-IN") +
                          " quality='" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "'" +
                          " gfx=" + SystemInfo.graphicsDeviceType + " '" + SystemInfo.graphicsDeviceVersion + "'" +
                          " shaderLevel=" + SystemInfo.graphicsShaderLevel);

            var roots = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var rig = GameObject.Find("Train");
            sb.AppendLine("[DIAG] objects=" + roots.Length + " root(TrainSurvivor=" + (transform.parent != null ? transform.parent.name : "-") + ")");

            var cams = GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cams)
                sb.AppendLine($"[DIAG] cam '{c.name}' pos={F(c.transform.position)} rot={F(c.transform.eulerAngles)} " +
                              $"fov={c.fieldOfView} near={c.nearClipPlane} far={c.farClipPlane} mask={c.cullingMask} " +
                              $"clear={c.clearFlags}/{F(c.backgroundColor)} active={c.isActiveAndEnabled}");

            var mrs = GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            int visible = 0, shown = 0;
            foreach (var mr in mrs)
            {
                if (mr.isVisible) visible++;
                if (shown++ >= 18) continue;
                var mat = mr.sharedMaterial;
                string shader = mat == null ? "NO-MATERIAL" : (mat.shader == null ? "NO-SHADER" : mat.shader.name);
                var b = mr.bounds;
                float dist = cams.Length > 0 ? Vector3.Distance(cams[0].transform.position, b.center) : -1f;
                sb.AppendLine($"[DIAG] mr '{mr.name}' shader={shader} enabled={mr.enabled && mr.gameObject.activeInHierarchy} " +
                              $"visible={mr.isVisible} layer={mr.gameObject.layer} center={F(b.center)} size={F(b.size)} dist={dist:0.0} " +
                              $"verts={(mr.GetComponent<MeshFilter>() != null && mr.GetComponent<MeshFilter>().sharedMesh != null ? mr.GetComponent<MeshFilter>().sharedMesh.vertexCount : -1)}");
            }
            sb.AppendLine($"[DIAG] meshRenderers={mrs.Length} visible={visible}");

            var lights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
                sb.AppendLine($"[DIAG] light '{l.name}' type={l.type} intensity={l.intensity} color={F(l.color)} enabled={l.enabled} rot={F(l.transform.eulerAngles)}");

            sb.AppendLine($"[DIAG] ambient mode={RenderSettings.ambientMode} sky={F(RenderSettings.ambientSkyColor)} " +
                          $"fog={RenderSettings.fog} fogColor={F(RenderSettings.fogColor)} sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "none")}");

            if (rig != null)
                sb.AppendLine($"[DIAG] train pos={F(rig.transform.position)} scale={F(rig.transform.localScale)} children={rig.transform.childCount}");

            Debug.Log(sb.ToString());
        }

        static string F(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
        static string F(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},{c.a:0.00})";
    }
}

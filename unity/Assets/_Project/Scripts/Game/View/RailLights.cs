using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Track-side lamps built from the original industrial lamp prefab (tall post, bent arm,
    /// amber lens). Each lamp stands on the ground just beyond the gravel bed with a strip of
    /// clearance, its arm turned over the track, and a warm spot under the head aimed down and
    /// inward so both rails light up.
    ///
    /// A fixed pool of lamps is re-assigned to slots around the train, so object and light count
    /// never grow during a stage (mobile budget: no shadows, spot only).
    /// </summary>
    public sealed class RailLights : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Metres between lamps along the track")]
        public float spacing = 26f;
        [Tooltip("How far behind the locomotive the pool extends")]
        public float behind = 30f;
        [Tooltip("How far ahead the pool extends")]
        public float ahead = 100f;
        [Tooltip("Fixed number of live lamps — this is the budget")]
        public int poolSize = 6;
        [Tooltip("Base distance from the centreline (ballast half-width is 3.1 m)")]
        public float lateral = 3.9f;
        [Tooltip("Fitted height of the lamp prefab, metres")]
        public float lampHeight = 5.6f;
        [Tooltip("Arm direction along local Z (+1 / -1). The FBX is 5.5 m tall on Y with the " +
                 "arm reaching ~1.5 m along Z after Unity's Z-up import.")]
        public float armLocalSign = 1f;
        [Tooltip("Metres from the post to the head along the arm")]
        public float armReach = 1.35f;

        [Header("Light")]
        public Color colour = new Color(1f, 0.66f, 0.32f);
        public float intensity = 14f;
        public float range = 24f;
        [Range(30f, 120f)] public float spotAngle = 80f;

        bool _logged;
        bool _off;
        GameObject[] _roots;
        Light[] _lights;
        Vector3[] _headLocal;

        void Awake()
        {
            _off = DevTuning.Get("dev.raillights", 1f) < 0.5f;   // A/B perf switch
            _roots = new GameObject[Mathf.Max(1, poolSize)];
            _lights = new Light[_roots.Length];
            _headLocal = new Vector3[_roots.Length];
            for (int i = 0; i < _roots.Length; i++) Build(i);
        }

        void Build(int i)
        {
            var root = new GameObject("RailLamp" + i);
            root.transform.SetParent(transform, false);
            _roots[i] = root;

            var prefab = Resources.Load<GameObject>("Models/lamp");
            if (prefab == null)
            {
                Debug.LogWarning("[rail] Models/lamp missing");
                return;
            }

            var model = Instantiate(prefab, root.transform);
            model.name = "LampModel";
            // Keep the prefab's own rotation and scale: the FBX carries a Z-up -> Y-up
            // correction, and forcing identity lays the lamp on its side (which then makes the
            // height fit explode, because "height" becomes its thickness).
            model.transform.localPosition = Vector3.zero;

            var rs = model.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;

            // fit by measured height, then drop the base onto the ground
            Bounds b = rs[0].bounds;
            for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
            float fit = b.size.y > 0.01f ? lampHeight / b.size.y : 1f;
            model.transform.localScale *= fit;
            Bounds fitted = rs[0].bounds;
            for (int k = 1; k < rs.Length; k++) fitted.Encapsulate(rs[k].bounds);
            model.transform.localPosition -= Vector3.up * (fitted.min.y - root.transform.position.y);

            // original albedo on URP Lit; the lens texture stays hot on its own
            var albedo = Resources.Load<Texture2D>("Models/lamp_albedo");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var mat = new Material(shader) { name = "RailLampMat" };
                if (albedo != null) mat.SetTexture("_BaseMap", albedo);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetColor("_EmissionColor", new Color(0.5f, 0.2f, 0.05f));
                mat.EnableKeyword("_EMISSION");
                mat.SetFloat("_Smoothness", 0.25f);
                foreach (var r in rs) r.sharedMaterial = mat;
            }

            // head position in root space: the far end of the arm, just under the top
            Bounds lb = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in rs)
            {
                var rb = r.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? rb.min.x : rb.max.x,
                        (c & 2) == 0 ? rb.min.y : rb.max.y,
                        (c & 4) == 0 ? rb.min.z : rb.max.z);
                    Vector3 p = root.transform.InverseTransformPoint(corner);
                    if (first) { lb = new Bounds(p, Vector3.zero); first = false; }
                    else lb.Encapsulate(p);
                }
            }
            _headLocal[i] = new Vector3(
                lb.center.x, lb.max.y * 0.86f, Mathf.Sign(armLocalSign) * armReach);

            var lgo = new GameObject("Light");
            lgo.transform.SetParent(root.transform, false);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = spotAngle;
            l.innerSpotAngle = spotAngle * 0.55f;
            l.shadows = LightShadows.None;
            _lights[i] = l;
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            if (_off) return;
            Sync(loop.World);
        }

        public void Sync(World world)
        {
            if (world == null) return;

            float s0 = world.Train.S;
            int firstSlot = Mathf.FloorToInt((s0 - behind) / spacing);
            int lastSlot = Mathf.CeilToInt((s0 + ahead) / spacing);

            int used = 0;
            for (int k = firstSlot; k <= lastSlot && used < _roots.Length; k++)
            {
                if (_lights[used] == null) { used++; continue; }

                float s = k * spacing;
                int side = (k & 1) == 0 ? -1 : 1;

                Vector3 p = TrackSpace.Point(s, side * lateral, 0.02f);
                // local +Z must map onto the inward direction: +/-90 deg from the track heading,
                // flipped when the arm hangs the other way
                float yaw = (side < 0 ? 90f : -90f) * (armLocalSign >= 0f ? 1f : -1f);
                _roots[used].transform.SetPositionAndRotation(
                    p, TrackSpace.Facing(s) * Quaternion.Euler(0f, yaw, 0f));
                _roots[used].SetActive(true);

                Vector3 head = _roots[used].transform.TransformPoint(_headLocal[used]);
                Vector3 inward = -side * TrackSpace.Right(s);
                Vector3 centre = TrackSpace.Point(s, 0f, 0f);
                float run = Mathf.Max(0.6f, Vector3.ProjectOnPlane(head - centre, Vector3.up).magnitude);
                float ratio = Mathf.Clamp(head.y / run, 0.6f, 6f);

                _lights[used].transform.position = head + Vector3.down * 0.2f;
                _lights[used].transform.rotation =
                    Quaternion.LookRotation((inward + Vector3.down * ratio).normalized, Vector3.up);
                _lights[used].enabled = true;
                used++;
            }

            for (int i = used; i < _roots.Length; i++)
                if (_roots[i] != null) _roots[i].SetActive(false);

            if (!_logged && used > 0)
            {
                _logged = true;
                var l = _lights[0];
                Debug.Log(string.Format(
                    "[rail] lamps {0}/{1} head {2} light {3} int {4:0.0} angle {5:0}",
                    used, _roots.Length, _headLocal[0], l.transform.position, l.intensity, l.spotAngle));
            }
        }
    }
}

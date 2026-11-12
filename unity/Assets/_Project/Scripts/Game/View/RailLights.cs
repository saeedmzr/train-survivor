using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Track-mounted lamps: compact fixtures at the sleeper ends, beside the steel, spaced evenly
    /// along the rails and turned with the track so they follow curves. Each is a downward, inward
    /// spot that throws a warm amber pool across both rails, the sleepers and a little gravel.
    ///
    /// A fixed pool of fixtures is re-assigned to the slots around the train every frame, so the
    /// light count stays constant for the whole stage (mobile budget: no shadows, spot only).
    /// </summary>
    public sealed class RailLights : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Metres between fixtures along the track")]
        public float spacing = 20f;
        [Tooltip("How far behind the locomotive the pool extends")]
        public float behind = 25f;
        [Tooltip("How far ahead the pool extends")]
        public float ahead = 95f;
        [Tooltip("Fixed number of live fixtures — this is the lighting budget")]
        public int poolSize = 6;
        [Tooltip("Distance from the centreline to the sleeper end")]
        public float lateral = 0.95f;
        [Tooltip("Fixture head height above the sleeper")]
        public float height = 2.3f;      // mast height: the cone must reach both rails

        [Header("Light")]
        public Color colour = new Color(1f, 0.62f, 0.28f);
        public float intensity = 6f;
        public float range = 16f;
        [Range(30f, 120f)] public float spotAngle = 85f;

        GameObject[] _roots;
        Light[] _lights;
        static Material _bodyMat;

        void Awake()
        {
            _roots = new GameObject[Mathf.Max(1, poolSize)];
            _lights = new Light[_roots.Length];
            for (int i = 0; i < _roots.Length; i++) _roots[i] = Build(i);
        }

        GameObject Build(int i)
        {
            var root = new GameObject("RailLamp" + i);
            root.transform.SetParent(transform, false);

            // slim mast with a compact head at the top: low enough to stay out of the train's
            // silhouette, high enough that the pool spans both rails
            var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(mast.GetComponent<Collider>());
            mast.name = "Mast";
            mast.transform.SetParent(root.transform, false);
            mast.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            mast.transform.localScale = new Vector3(0.07f, height, 0.07f);
            mast.GetComponent<Renderer>().sharedMaterial = BodyMaterial();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "Head";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, height, 0f);
            body.transform.localScale = new Vector3(0.22f, 0.10f, 0.30f);
            body.GetComponent<Renderer>().sharedMaterial = BodyMaterial();

            var lgo = new GameObject("Light");
            lgo.transform.SetParent(root.transform, false);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = colour;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = spotAngle;
            l.innerSpotAngle = spotAngle * 0.45f;
            l.shadows = LightShadows.None;     // budget: pools only, no shadow maps
            _lights[i] = l;
            return root;
        }

        static Material BodyMaterial()
        {
            if (_bodyMat != null) return _bodyMat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            _bodyMat = new Material(shader) { name = "RailLampBody" };
            _bodyMat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.50f));
            _bodyMat.SetColor("_EmissionColor", new Color(2.4f, 1.15f, 0.38f));
            _bodyMat.EnableKeyword("_EMISSION");
            _bodyMat.SetFloat("_Smoothness", 0.25f);
            return _bodyMat;
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World);
        }

        public void Sync(World world)
        {
            if (world == null) return;

            float s0 = world.Train.S;
            int first = Mathf.FloorToInt((s0 - behind) / spacing);
            int last = Mathf.CeilToInt((s0 + ahead) / spacing);

            int used = 0;
            for (int k = first; k <= last && used < _roots.Length; k++)
            {
                float s = k * spacing;
                int side = (k & 1) == 0 ? -1 : 1;

                Vector3 p = TrackSpace.Point(s, side * lateral, 0.06f);
                _roots[used].transform.SetPositionAndRotation(p, TrackSpace.Facing(s));
                _roots[used].SetActive(true);

                // aim down and across the track so both rails sit inside the pool
                Vector3 inward = -side * TrackSpace.Right(s);
                // ~32 deg below the horizontal: the cone lands across both rails, not at the post
                Vector3 dir = (inward * 0.85f + Vector3.down * 0.53f).normalized;
                _lights[used].transform.position = p + Vector3.up * height;
                _lights[used].transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                _lights[used].enabled = true;
                used++;
            }

            for (int i = used; i < _roots.Length; i++) _roots[i].SetActive(false);
        }
    }
}

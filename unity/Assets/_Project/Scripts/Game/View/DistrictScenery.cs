using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// The abandoned industrial district: warehouses and a brick factory set back from the rails,
    /// broken walls, rubble, crates, barrels and lamp posts dressing the corridor. Placement is
    /// deterministic from a seed and slot index, and drawn from fixed pools that are re-assigned as
    /// the train advances — so the environment never grows in object count and never repeats after
    /// a restart with the same seed.
    ///
    /// Nothing is placed inside CombatClear of the rails, so zombies keep the open approaches on
    /// both sides and the locomotive's corridor stays clear.
    /// </summary>
    public sealed class DistrictScenery : MonoBehaviour
    {
        public static DistrictScenery Instance { get; private set; }

        [Header("Placement")]
        [Tooltip("Distance between scenery slots along the track, in metres")]
        public float slotStep = 7f;
        [Tooltip("Nothing is placed closer to the rails than this")]
        public float combatClear = 6.5f;
        [Tooltip("How far ahead of the train scenery is built")]
        public float windowAhead = 110f;
        [Tooltip("How far behind the train scenery is kept alive")]
        public float windowBehind = 35f;
        [Tooltip("Buildings sit at least this far out")]
        public float buildingLateral = 19f;
        public int seed = 1337;
        [Tooltip("Uniform scale jitter is ± this fraction")]
        [Range(0f, 0.3f)] public float scaleJitter = 0.15f;

        [Header("Lighting")]
        [Tooltip("How many lamp lights can be active at once")]
        [Range(0, 6)] public int maxLampLights = 3;
        public Color lampColour = new Color(1f, 0.72f, 0.36f);

        sealed class Prop
        {
            public GameObject Go;
            public Renderer Rd;
            public float BaseScale = 1f;   // measured fit, never assume the FBX import scale
            public float FootLocal;        // local bounds min Y at scale 1, for ground contact
        }

        readonly Dictionary<string, List<Prop>> _pools = new Dictionary<string, List<Prop>>();
        readonly Dictionary<string, int> _used = new Dictionary<string, int>();
        readonly List<Light> _lampLights = new List<Light>();
        readonly List<Transform> _activeLamps = new List<Transform>();
        readonly List<Renderer> _activeLampRenderers = new List<Renderer>();

        bool _logged;

        public bool Active => _pools.Count > 0;

        void Awake()
        {
            Instance = this;

            DressGround();

            if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Skybox ||
                RenderSettings.ambientLight.maxColorComponent < 0.22f)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.26f, 0.25f, 0.24f);
            }

            // counts are the ceiling on live objects of each type, regardless of stage length
            Add("warehouse", 6, 14f, true);
            Add("brickbuilding", 6, 11f, true);
            Add("rockwall", 12, 6f, false);
            Add("rubble", 14, 3.2f, false);
            Add("crate", 18, 1.2f, true);
            Add("barrel", 18, 0.95f, true);
            Add("lamp", 12, 5.5f, true);

            for (int i = 0; i < maxLampLights; i++)
            {
                var go = new GameObject("LampLight" + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = lampColour;
                l.range = 14f;
                l.intensity = 2.2f;
                l.shadows = LightShadows.None;   // mobile: no shadow maps for props
                l.enabled = false;
                _lampLights.Add(l);
            }

            Debug.Log(string.Format("[env] district ready: {0} types, {1} props, seed {2}, clear {3} m",
                _pools.Count, CountProps(), seed, combatClear));
        }

        void Add(string name, int count, float targetSize, bool byHeight)
        {
            var prefab = Resources.Load<GameObject>("Models/" + name);
            if (prefab == null)
            {
                Debug.LogWarning("[env] missing Resources/Models/" + name);
                return;
            }

            var tex = Resources.Load<Texture2D>("Models/" + name + "_albedo");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material mat = null;
            if (shader != null)
            {
                mat = new Material(shader) { name = name + "Mat" };
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", tex != null ? Color.white : FallbackTint(name));
                Debug.Log(string.Format("[env] tex {0}: {1}", name, tex != null ? tex.name : "MISSING"));
                mat.SetFloat("_Smoothness", 0.18f);
            }

            var list = new List<Prop>();
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = name + "_" + i;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                // Fit from measured bounds: Tripo FBX files carry their own unit scale, and
                // overwriting localScale with 1 renders them centimetre-tall and invisible.
                Bounds b = WorldBounds(go);
                float size = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                float k = size > 0.0001f ? targetSize / size : 1f;
                go.transform.localScale = Vector3.one * k;
                Bounds fitted = WorldBounds(go);

                // Re-root the mesh so the instance origin sits exactly on the ground. Measured
                // once here; the visual lives under a holder, so later scaling never re-floats it.
                var holder = new GameObject("Visual");
                holder.transform.SetParent(go.transform, false);
                var kids = new List<Transform>();
                foreach (Transform c in go.transform) if (c != holder.transform) kids.Add(c);
                foreach (var kid in kids) kid.SetParent(holder.transform, true);
                holder.transform.localPosition = new Vector3(0f, -fitted.min.y / Mathf.Max(k, 0.0001f), 0f);
                if (mat != null)
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;

                // lamp lenses glow amber; the prop itself stays unlit-added so mobile stays cheap
                if (name == "lamp" && mat != null)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", lampColour * 1.6f);
                }

                go.SetActive(false);
                list.Add(new Prop
                {
                    Go = go,
                    Rd = go.GetComponentInChildren<Renderer>(),
                    BaseScale = k,
                    FootLocal = 0f
                });
            }
            _pools[name] = list;
        }

        /// <summary>
        /// The ground is one flat-coloured ribbon, which reads as a void once real props stand on it.
        /// A deterministic grain texture at a coarse tiling gives gravel without a new draw call.
        /// </summary>
        void DressGround()
        {
            var ground = GameObject.Find("Ground");
            if (ground == null) { Debug.LogWarning("[env] no Ground object"); return; }

            var mr = ground.GetComponent<MeshRenderer>();
            if (mr == null || mr.sharedMaterial == null) return;

            int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = "GravelGround",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4
            };

            var rng = new System.Random(seed);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // fine grain + a slow blotch layer, both from a seeded hash so runs match
                    int h = x * 73856093 ^ y * 19349663 ^ seed * 83492791;
                    float grain = ((h >> 8) & 255) / 255f;
                    float coarse = Mathf.PerlinNoise(x * 0.055f + 3.3f, y * 0.055f + 7.1f);
                    float v = 0.26f + grain * 0.16f + (coarse - 0.5f) * 0.22f;
                    v = Mathf.Clamp(v, 0.06f, 0.55f);
                    byte g = (byte)(v * 255f);
                    px[y * n + x] = new Color32((byte)(g * 1.02f), g, (byte)(g * 0.94f), 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true);

            var mat = new Material(mr.sharedMaterial);
            mat.name = "GroundGravel";
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", Vector2.one);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            mr.sharedMaterial = mat;
        }

        static Color FallbackTint(string name)
        {
            switch (name)
            {
                case "brickbuilding": return new Color(0.42f, 0.27f, 0.22f);
                case "warehouse": return new Color(0.33f, 0.32f, 0.30f);
                case "crate": return new Color(0.45f, 0.35f, 0.24f);
                case "barrel": return new Color(0.38f, 0.30f, 0.24f);
                case "lamp": return new Color(0.25f, 0.25f, 0.25f);
                default: return new Color(0.34f, 0.33f, 0.31f);
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        int CountProps()
        {
            int n = 0;
            foreach (var kv in _pools) n += kv.Value.Count;
            return n;
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Pick the prop for a slot: type, side, lateral distance, yaw and scale.</summary>
        void Pick(int slot, int side, out string type, out float lateral, out float yaw, out float scale)
        {
            uint h = Hash((uint)(slot * 73856093) ^ (uint)(seed * 19349663));
            int roll = (int)(h % 100);
            float r1 = ((h >> 12) & 0xFF) / 255f;
            float r2 = ((h >> 20) & 0xFF) / 255f;

            yaw = r1 * 360f;
            scale = 1f + (r2 * 2f - 1f) * scaleJitter;

            if (roll < 22)
            {
                // buildings only every third slot, well back, so they never crowd the corridor
                type = (slot % 7 < 2) ? (((h >> 24) & 1) == 0 ? "warehouse" : "brickbuilding") : null;
                lateral = side * (buildingLateral + r1 * 8f);
            }
            else if (roll < 34)
            {
                type = "rockwall";
                lateral = side * (combatClear + 1.2f + r1 * 2.6f);
            }
            else if (roll < 52)
            {
                type = "rubble";
                lateral = side * (combatClear + 0.2f + r1 * 2.4f);
            }
            else if (roll < 72)
            {
                type = ((h >> 26) & 1) == 0 ? "crate" : "barrel";
                lateral = side * (combatClear + 0.3f + r1 * 2.0f);
            }
            else if (roll < 84)
            {
                type = "lamp";
                lateral = side * (combatClear + 0.2f + r1 * 1.2f);
            }
            else
            {
                type = null;               // open ground: keep combat lanes readable
                lateral = 0f;
            }
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World);
        }

        public void Sync(World world)
        {
            if (!Active || world == null) return;

            foreach (var kv in _pools)
                foreach (var p in kv.Value)
                    if (p.Go.activeSelf) p.Go.SetActive(false);
            _used.Clear();
            _activeLamps.Clear();
            _activeLampRenderers.Clear();

            float s0 = world.Train.S;
            int first = Mathf.FloorToInt((s0 - windowBehind) / slotStep);
            int last = Mathf.CeilToInt((s0 + windowAhead) / slotStep);

            for (int i = first; i <= last; i++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Pick(i * 2 + (side > 0 ? 1 : 0), side, out string type, out float lateral,
                         out float yaw, out float scale);
                    if (type == null) continue;

                    var list = _pools[type];
                    int idx = _used.TryGetValue(type, out int u) ? u : 0;
                    if (idx >= list.Count) continue;
                    _used[type] = idx + 1;

                    Prop p = list[idx];
                    float s = i * slotStep;
                    Vector3 pos = TrackSpace.Point(s, lateral, 0.02f);
                    float finalScale = p.BaseScale * scale;
                    pos.y = -p.FootLocal * finalScale + 0.02f;
                    p.Go.transform.SetPositionAndRotation(
                        pos,
                        TrackSpace.Facing(s) * Quaternion.Euler(0f, yaw, 0f));
                    p.Go.transform.localScale = Vector3.one * finalScale;
                    p.Go.SetActive(true);

                    if (type == "lamp")
                    {
                        _activeLamps.Add(p.Go.transform);
                        if (p.Rd != null) _activeLampRenderers.Add(p.Rd);
                    }
                }
            }

            if (!_logged && _activeLamps.Count > 0)
            {
                _logged = true;
                Debug.Log(string.Format("[env] placed {0} props, {1} lamps, first prop {2}",
                    _used.Count, _activeLamps.Count, _activeLamps[0].position));
            }

            // light only the lamps nearest the train, capped for mobile
            int lit = Mathf.Min(maxLampLights, _activeLamps.Count);
            for (int i = 0; i < _lampLights.Count; i++)
            {
                if (i >= lit)
                {
                    _lampLights[i].enabled = false;
                    continue;
                }
                Transform lamp = _activeLamps[i];
                _lampLights[i].transform.position = lamp.position + Vector3.up * 4.8f;
                _lampLights[i].enabled = true;
            }
        }
    }
}

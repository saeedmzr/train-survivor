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
        /// <summary>Layer the chase camera raycasts against (see ProjectSettings/TagManager).</summary>
        const int SceneryLayer = 6;
        public static DistrictScenery Instance { get; private set; }

        [Header("Placement")]
        [Tooltip("Distance between scenery slots along the track, in metres")]
        public float slotStep = 7f;
        [Tooltip("Nothing is placed closer to the rails than this")]
        public float combatClear = 6.5f;
        [Tooltip("How far ahead of the train scenery is built")]
        public float windowAhead = 140f;
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
            public float Radius;           // half diagonal of the XZ footprint at scale 1
        }

        readonly Dictionary<string, List<Prop>> _pools = new Dictionary<string, List<Prop>>();
        readonly Dictionary<string, int> _used = new Dictionary<string, int>();
        readonly List<Light> _lampLights = new List<Light>();
        readonly List<Transform> _activeLamps = new List<Transform>();
        readonly List<Renderer> _activeLampRenderers = new List<Renderer>();

        bool _logged;
        bool _verbose;

        public bool Active => _pools.Count > 0;

        void Awake()
        {
            Instance = this;
            _verbose = TrainSurvivor.Game.DevTuning.Get("dev.verbose", 0f) > 0.5f;

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

            if (_verbose)
                Debug.Log(string.Format("[env] district ready: {0} types, {1} props, seed {2}, clear {3} m",
                    _pools.Count, CountProps(), seed, combatClear));
        }

        /// <summary>
        /// Explicit per-asset import correction, applied to the imported visual child. The FBX
        /// were authored with mixed orientations, so each asset is pinned individually.
        /// </summary>
        static Quaternion AssetRotation(string name)
        {
            // Every source FBX is authored Z-up; -90X converts Z-up to Unity Y-up.
            // dev.rot.<name> overrides one asset at runtime for A/B inspection.
            float deg = TrainSurvivor.Game.DevTuning.Get("dev.rot." + name, -90f);
            return Quaternion.Euler(deg, 0f, 0f);
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
                if (_verbose)
                    Debug.Log(string.Format("[env] tex {0}: {1}", name, tex != null ? tex.name : "MISSING"));
                mat.SetFloat("_Smoothness", 0.18f);
            }

            var list = new List<Prop>();
            for (int i = 0; i < count; i++)
            {
                // Neutral pooled root: the placement pass owns its world position, yaw and
                // variant scale. The imported asset lives under it as "Visual" and keeps the
                // explicit import correction, which placement must never touch.
                var root = new GameObject(name + "_" + i);
                root.transform.SetParent(transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;

                var model = Instantiate(prefab, root.transform);
                model.name = "Visual";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = AssetRotation(name);
                model.transform.localScale = Vector3.one;

                // Orient before measuring, then fit the visual in the root's local units.
                Bounds b = WorldBounds(model);
                float size = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
                float k = size > 0.0001f ? targetSize / size : 1f;
                model.transform.localScale = Vector3.one * k;

                // Ground the visual on the root's plane in world space (root scale is 1 here).
                float minY = WorldBounds(model).min.y;
                model.transform.position += Vector3.up * (root.transform.position.y - minY);

                if (_verbose)
                    Debug.Log(string.Format("[env] orient {0}: {1} size {2:0.00}x{3:0.00}x{4:0.00}",
                        name, AssetRotation(name).eulerAngles, b.size.x, b.size.y, b.size.z));

                if (mat != null)
                    foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;

                // Collider measured after grounding, expressed in the root's local frame so the
                // placement yaw/scale carry it correctly.
                Bounds wb = WorldBounds(model);
                root.layer = SceneryLayer;
                var box = root.AddComponent<BoxCollider>();
                box.center = root.transform.InverseTransformPoint(wb.center);
                box.size = wb.size;
                float radius = 0.5f * Mathf.Sqrt(wb.size.x * wb.size.x + wb.size.z * wb.size.z);

                if (name == "lamp" && mat != null)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", lampColour * 1.6f);
                }

                root.SetActive(false);
                list.Add(new Prop
                {
                    Go = root,
                    Rd = root.GetComponentInChildren<Renderer>(),
                    BaseScale = 1f,
                    FootLocal = 0f,
                    Radius = radius
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

            if (roll < 26)
            {
                // Industrial clusters: roughly every third candidate slot on a side, so the
                // skyline reads as a district instead of one lone warehouse per kilometer.
                bool building = ((h >> 24) % 3) != 0;
                type = building ? (((h >> 26) & 1) == 0 ? "warehouse" : "brickbuilding") : null;
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

        float _lastSyncS = float.MaxValue;
        int _lastStage = -1;
        World _lastWorld;

        /// <summary>A district slot is fixed in world space, so rebuilding every frame is pure
        /// churn. Re-place only when the train has moved half a slot (or the stage changed).</summary>
        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World);
        }

        public void Sync(World world)
        {
            if (!Active || world == null) return;

            float s0 = world.Train.S;
            int stage = world.Train.Stage;
            if (world == _lastWorld && stage == _lastStage &&
                Mathf.Abs(s0 - _lastSyncS) < 0.5f) return;

            _lastWorld = world;
            _lastStage = stage;
            _lastSyncS = s0;

            foreach (var kv in _pools)
                foreach (var p in kv.Value)
                    if (p.Go.activeSelf) p.Go.SetActive(false);
            _used.Clear();
            _activeLamps.Clear();
            _activeLampRenderers.Clear();

            int first = Mathf.FloorToInt((s0 - windowBehind) / slotStep);
            int last = Mathf.CeilToInt((s0 + windowAhead) / slotStep);

            for (int i = first; i <= last; i++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    int slot = i * 2 + (side > 0 ? 1 : 0);
                    Pick(slot, side, out string type, out float lateral, out float yaw, out float scale);
                    if (type == null) continue;

                    float s = i * slotStep;
                    if (!Place(type, s, lateral, yaw, scale)) continue;

                    // Yard clutter beside a building closes the empty apron between the rails
                    // and the facade without narrowing the combat corridor.
                    if (type == "warehouse" || type == "brickbuilding")
                    {
                        uint h = Hash((uint)(slot * 92837111) ^ (uint)(seed * 689287499));
                        string yard = ((h & 1) == 0) ? "crate" : "barrel";
                        float yardLat = side * (combatClear + 4.6f + ((h >> 8) & 3) * 1.1f);
                        Place(yard, s + 2.4f, yardLat, ((h >> 12) & 63) * 5.7f,
                              0.9f + ((h >> 18) & 3) * 0.07f);
                    }
                }
            }

            if (_verbose && !_logged)
            {
                _logged = true;
                int active = 0;
                foreach (var kv in _used) active += kv.Value;
                Debug.Log(string.Format("[env] placed {0} props in view at S {1:0.0} ({2} lamps)",
                    active, s0, _activeLamps.Count));
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

        /// <summary>Place one pooled prop, enforcing a footprint-aware clearance from the rails.</summary>
        bool Place(string type, float s, float lateral, float yaw, float scale)
        {
            if (!_pools.TryGetValue(type, out var list)) return false;
            int idx = _used.TryGetValue(type, out int u) ? u : 0;
            if (idx >= list.Count) return false;
            _used[type] = idx + 1;

            Prop p = list[idx];
            float finalScale = p.BaseScale * scale;
            // A random yaw can swing a 26 m warehouse corner toward the track: clamp by the
            // measured footprint radius, not by a fixed facade offset.
            float minLateral = combatClear + p.Radius * finalScale + 0.5f;
            if (Mathf.Abs(lateral) < minLateral)
                lateral = (lateral < 0f ? -1f : 1f) * minLateral;

            Vector3 pos = TrackSpace.Point(s, lateral, 0.02f);
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
            return true;
        }
    }
}

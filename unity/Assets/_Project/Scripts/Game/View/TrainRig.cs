using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Builds the train: the locomotive comes from the modelled asset in Resources/Models
    /// (falling back to primitives if it is missing), the freight wagons stay primitive, and
    /// everything is placed on the rail every frame. Wagon 0 is the locomotive.
    /// </summary>
    public sealed class TrainRig : MonoBehaviour
    {
        const float WagonLength = 6.6f;
        const float LocoLength = 8.2f;     // metres; the model is fitted to this

        readonly List<Transform> _wagons = new List<Transform>();
        readonly List<Transform> _guns = new List<Transform>();
        Material _body, _accent, _metal, _glass;
        Material _locoMat;
        bool _warnedMissingModel;
        bool _verbose;

        // Gun mount plates, measured from the meshes (Blender axes: x right, y forward, z up;
        // Unity takes x, z, y). One on the loco roof, two on each wagon roof.
        // Plate centres as fractions of the model's world bounds (x, up, forward). Measured from
        // top-down renders with markers on the plates, so they are independent of the export's
        // pivot, scale and axis offsets.
        static readonly Vector3[] LocoMounts =
        {
            new Vector3(0.50f, 0.933f, 0.198f),   // plate nearer the cab
            new Vector3(0.50f, 0.807f, 0.493f),   // plate further forward
        };
        static readonly Vector3[] WagonMounts =
        {
            new Vector3(0.50f, 0.939f, 0.359f),
            new Vector3(0.50f, 0.923f, 0.703f),
        };
        readonly Dictionary<Transform, Transform> _wagonModel = new Dictionary<Transform, Transform>();
        readonly Dictionary<Transform, Vector3[]> _plateCache = new Dictionary<Transform, Vector3[]>();
        Transform _locoModel;

        public Vector3 LeadPosition => _wagons.Count > 0 ? _wagons[0].position : transform.position;
        public Quaternion LeadRotation => _wagons.Count > 0 ? _wagons[0].rotation : transform.rotation;

        public void Build(int wagons, int guns)
        {
            _body = Palette.Get("TrainBody", Palette.Body);
            _accent = Palette.Get("TrainAccent", Palette.Accent);
            _metal = Palette.Get("Metal", Palette.Metal);
            _glass = Palette.Get("CabGlass", new Color(0.05f, 0.09f, 0.14f));
            _verbose = DevTuning.Get("dev.verbose", 0f) > 0.5f;

            BuildLocomotive();
            // StartingWagons counts the freight cars behind the engine: 1 means loco + 1 wagon.
            for (int i = 1; i <= wagons; i++) BuildFreightWagon(i);

            BuildGuns(Mathf.Max(1, guns));
        }

        void BuildLocomotive()
        {
            var root = new GameObject("Loco").transform;
            root.SetParent(transform, false);
            _wagons.Add(root);

            var prefab = Resources.Load<GameObject>("Models/locomotive");
            if (prefab != null && BuildModelledLocomotive(root, prefab))
            {
                _locoModel = root.Find("LocoModel");
                return;
            }

            if (!_warnedMissingModel)
            {
                _warnedMissingModel = true;
                Debug.LogWarning("[loco] Resources/Models/locomotive missing — using the primitive loco");
            }

            MakeBox(root, "Hull", new Vector3(0f, 1.3f, 0f), new Vector3(2.5f, 1.4f, 6.4f), _body);
            MakeBox(root, "Skirt", new Vector3(0f, 0.62f, 0f), new Vector3(2.62f, 0.28f, 6.2f), _metal);
            MakeBox(root, "Cab", new Vector3(0f, 2.45f, -1.75f), new Vector3(2.2f, 1.05f, 1.9f), _body);
            MakeBox(root, "CabRoof", new Vector3(0f, 3.02f, -1.75f), new Vector3(2.34f, 0.16f, 2.04f), _accent);
            MakeBox(root, "CabWindow", new Vector3(0f, 2.52f, -2.72f), new Vector3(1.35f, 0.55f, 0.06f), _glass);
            MakeBox(root, "CabStripe", new Vector3(0f, 2.0f, -2.71f), new Vector3(2.1f, 0.18f, 0.05f), _accent);
            MakeCylinder(root, "Boiler", new Vector3(0f, 2.15f, 1.15f), 0.78f, 3.0f, _body,
                         Quaternion.Euler(90f, 0f, 0f));
            MakeCylinder(root, "Chimney", new Vector3(0f, 3.05f, 2.35f), 0.28f, 1.0f, _metal);
            MakeCylinder(root, "Dome", new Vector3(0f, 2.95f, 0.35f), 0.42f, 0.55f, _accent);
            var catcher = MakeBox(root, "Cowcatcher", new Vector3(0f, 0.72f, 3.25f), new Vector3(2.45f, 0.12f, 1.5f), _metal);
            catcher.localRotation = Quaternion.Euler(-32f, 0f, 0f);
            MakeBox(root, "Bumper", new Vector3(0f, 0.85f, 3.35f), new Vector3(2.6f, 0.35f, 0.25f), _accent);
            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? 1.9f : -1.9f;
                MakeWheel(root, new Vector3(1.15f, 0.55f, z));
                MakeWheel(root, new Vector3(-1.15f, 0.55f, z));
            }
        }

        /// <summary>
        /// Instantiate the modelled locomotive, fit it to LocoLength, sit it on the rail with its
        /// nose down the track. dev.locoYaw corrects the model's authored forward axis without a
        /// rebuild (the Tripo export's facing is not guaranteed).
        /// </summary>
        bool BuildModelledLocomotive(Transform root, GameObject prefab)
        {
            GameObject model;
            try
            {
                model = Object.Instantiate(prefab, root);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[loco] model instantiate failed: " + e.Message);
                return false;
            }

            model.name = "LocoModel";
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[loco] model has no renderers");
                return false;
            }

            // Stand it up, then lay its long axis down the track; measured in the car root's
            // local frame so a yawed (curved-track) parent cannot change fit or centring.
            Bounds MeasureLocal()
            {
                bool any = false;
                Bounds bb = new Bounds();
                foreach (var r in renderers)
                {
                    var lb = r.localBounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? lb.min.x : lb.max.x,
                            (c & 2) == 0 ? lb.min.y : lb.max.y,
                            (c & 4) == 0 ? lb.min.z : lb.max.z);
                        Vector3 p = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (!any) { bb = new Bounds(p, Vector3.zero); any = true; }
                        else bb.Encapsulate(p);
                    }
                }
                return bb;
            }

            Bounds b = MeasureLocal();
            if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.2f)
            {
                float pitch = DevTuning.Get("dev.locoPitch", -90f);
                model.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                b = MeasureLocal();
                if (_verbose) Debug.Log("[loco] model was lying along Y - pitched " + pitch);
            }
            if (b.size.x > b.size.z * 1.2f)
            {
                model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 90f, 0f);
                b = MeasureLocal();
            }

            float yaw = DevTuning.Get("dev.locoYaw", 0f);
            model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, yaw, 0f);

            b = MeasureLocal();
            float scale = LocoLength / Mathf.Max(b.size.z, 0.01f);
            model.transform.localScale = Vector3.one * scale;

            b = MeasureLocal();
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            _locoMat = MakeLocoMaterial();
            if (_locoMat != null)
                foreach (var r in renderers) r.sharedMaterial = _locoMat;

            if (_verbose)
                Debug.Log(string.Format("[loco] model ready: {0} renderers, scale {1:0.00}, size {2} yaw {3}",
                    renderers.Length, scale, b.size.ToString("0.0"), yaw));
            return true;
        }

        Material MakeLocoMaterial()
        {
            var tex = Resources.Load<Texture2D>("Models/locomotive_albedo");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[loco] URP/Lit shader not found");
                return null;
            }
            var mat = new Material(shader) { name = "LocomotiveMat" };
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.25f);
            if (tex == null) Debug.LogWarning("[loco] albedo missing");
            return mat;
        }

        /// <summary>Fit a prefab under a car root, measuring in that root's local frame.</summary>
        Transform PlaceModel(GameObject prefab, Transform parent, string albedoResource,
                        float targetLength, float targetHeight, float yaw)
        {
            GameObject model;
            try { model = Object.Instantiate(prefab, parent); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[asset] instantiate failed for " + prefab.name + ": " + e.Message);
                return null;
            }

            model.name = prefab.name + "_Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localScale = Vector3.one;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return null;

            Bounds MeasureLocal()
            {
                bool any = false;
                Bounds bb = new Bounds();
                foreach (var r in renderers)
                {
                    var lb = r.localBounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? lb.min.x : lb.max.x,
                            (c & 2) == 0 ? lb.min.y : lb.max.y,
                            (c & 4) == 0 ? lb.min.z : lb.max.z);
                        Vector3 p = parent.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (!any) { bb = new Bounds(p, Vector3.zero); any = true; }
                        else bb.Encapsulate(p);
                    }
                }
                return bb;
            }

            Bounds b = MeasureLocal();
            if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.15f)
            {
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                b = MeasureLocal();
            }
            if (b.size.x > b.size.z * 1.15f)
            {
                model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 90f, 0f);
                b = MeasureLocal();
            }
            model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, yaw, 0f);

            b = MeasureLocal();
            float lengthScale = targetLength / Mathf.Max(b.size.z, 0.01f);
            float heightScale = targetHeight / Mathf.Max(b.size.y, 0.01f);
            model.transform.localScale = Vector3.one * Mathf.Min(lengthScale, heightScale);

            b = MeasureLocal();
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            var tex = Resources.Load<Texture2D>(albedoResource);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var mat = new Material(shader) { name = prefab.name + "Mat" };
                if (tex != null) mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", 0.25f);
                foreach (var r in renderers) r.sharedMaterial = mat;
            }
            return model.transform;
        }


        /// <summary>
        /// Mount points for a model, measured ONCE from renderer local geometry into the wagon
        /// parent's local frame. Stable on curves because no per-frame world AABB is used.
        /// Gun child renderers are excluded so a mounted turret never moves its own slot.
        /// </summary>
        /// <summary>Renderer bounds of a model expressed in the car root's local frame.</summary>
        bool TryLocalBounds(Transform parent, Transform model, out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponentInParent<GunRig>() != null) continue;
                var lb = r.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? lb.min.x : lb.max.x,
                        (c & 2) == 0 ? lb.min.y : lb.max.y,
                        (c & 4) == 0 ? lb.min.z : lb.max.z);
                    Vector3 p = parent.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return any;
        }

        Vector3[] Anchors(Transform parent, Transform model, Vector3[] fracs)
        {
            if (!TryLocalBounds(parent, model, out var b))
            {
                Debug.LogError("[mount] no renderers for " + model.name + " — no guns mounted");
                return new Vector3[0];
            }
            var pts = new Vector3[fracs.Length];
            for (int i = 0; i < fracs.Length; i++)
                pts[i] = b.min + Vector3.Scale(fracs[i], b.size);
            if (_verbose)
                Debug.Log(string.Format("[mount] {0} anchors {1} | {2}", model.name, pts[0], pts.Length > 1 ? pts[1].ToString() : "-"));
            return pts;
        }

        struct Mount
        {
            public Transform Parent;
            public Transform Model;   // null when the primitive fallback is in use
            public Vector3 Local;
        }

        /// <summary>Ordered list of every gun mount on the train: loco first, then each wagon.</summary>
        List<Mount> BuildMounts()
        {
            var list = new List<Mount>();
            for (int w = 0; w < _wagons.Count; w++)
            {
                Transform parent = _wagons[w];
                Transform model = null;
                if (w == 0)
                {
                    model = _locoModel != null ? _locoModel : parent.Find("LocoModel");
                }
                else
                {
                    _wagonModel.TryGetValue(parent, out model);
                }

                if (model == null)
                {
                    // primitive fallback: one slot on the roof, same as before
                    list.Add(new Mount { Parent = parent, Model = null, Local = new Vector3(0f, 2.2f, 0.6f) });
                    continue;
                }

                Vector3[] fracs = w == 0 ? LocoMounts : WagonMounts;
                Vector3[] pts = Anchors(parent, model, fracs);
                for (int m = 0; m < pts.Length; m++)
                    list.Add(new Mount { Parent = parent, Model = model, Local = pts[m] });
            }
            return list;
        }

        /// <summary>Turrets track the closest living enemy; the barrel is authored facing +Z.</summary>
        void AimGuns(World world)
        {
            if (world == null) return;

            for (int i = 0; i < _guns.Count; i++)
            {
                Transform gun = _guns[i];
                if (!gun.gameObject.activeSelf) continue;

                Vector3 gp = gun.position;
                Vector3 best = Vector3.zero;
                float bestSqr = float.MaxValue;
                for (int e = 0; e < world.EnemyCount; e++)
                {
                    Enemy en = world.Enemies[e];
                    if (!en.Alive) continue;
                    Vector3 ep = TrackSpace.Point(en.S, en.X, 0f);
                    float d = (ep - gp).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = ep; }
                }
                if (bestSqr == float.MaxValue) continue;

                Vector3 dir = best - gp;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.04f) continue;

                Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                gun.rotation = Quaternion.Slerp(gun.rotation, want, Time.deltaTime * 7f);
            }
        }

        void BuildFreightWagon(int index)
        {
            var root = new GameObject("Wagon" + index).transform;
            root.SetParent(transform, false);
            _wagons.Add(root);

            var wagonPrefab = Resources.Load<GameObject>("Models/wagon");
            if (wagonPrefab != null)
            {
                var model = PlaceModel(wagonPrefab, root, "Models/wagon_albedo", 6.2f, 3.2f, 0f);
                if (model != null)
                {
                    _wagonModel[root] = model;
                    return;
                }
            }

            MakeBox(root, "Deck", new Vector3(0f, 1.05f, 0f), new Vector3(2.35f, 0.35f, 5.8f), _body);
            MakeBox(root, "WallL", new Vector3(-1.12f, 1.45f, 0f), new Vector3(0.12f, 0.5f, 5.8f), _accent);
            MakeBox(root, "WallR", new Vector3(1.12f, 1.45f, 0f), new Vector3(0.12f, 0.5f, 5.8f), _accent);

            for (int i = 0; i < 2; i++)
            {
                float z = i == 0 ? 1.7f : -1.7f;
                MakeWheel(root, new Vector3(1.1f, 0.5f, z));
                MakeWheel(root, new Vector3(-1.1f, 0.5f, z));
            }
        }

        void BuildGuns(int count)
        {
            for (int i = 0; i < count; i++) BuildGun(i);
        }

        /// <summary>Copy a single-mesh turret into one side of a horizontal cut, keeping the
        /// original vertices/UVs/materials. The lower part becomes the fixed pedestal.</summary>
        static readonly Dictionary<Mesh, Mesh[]> _splitCache = new Dictionary<Mesh, Mesh[]>();
        static readonly Dictionary<Mesh, float> _cutCache = new Dictionary<Mesh, float>();

        /// <summary>
        /// Split the turret in RECOIL-LOCAL space (the frame the pitch hinge lives in), so the
        /// imported rotation/scale cannot invalidate the cut. Vertices and normals are baked into
        /// that frame; UVs, submeshes and materials are carried over unchanged.
        /// </summary>
        Mesh[] SplitTurretPart(Transform src, Mesh mesh, Transform frame, float cutFrac, out float cutPlane)
        {
            if (_splitCache.TryGetValue(mesh, out var cached))
            {
                cutPlane = _cutCache.TryGetValue(mesh, out float cachedCut) ? cachedCut : 0f;
                return cached;
            }

            var verts = mesh.vertices;
            var norms = mesh.normals;
            var uvs = mesh.uv;
            var local = new Vector3[verts.Length];
            var localN = norms != null && norms.Length == verts.Length ? new Vector3[norms.Length] : null;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                local[i] = frame.InverseTransformPoint(src.TransformPoint(verts[i]));
                minY = Mathf.Min(minY, local[i].y);
                maxY = Mathf.Max(maxY, local[i].y);
                if (localN != null) localN[i] = frame.InverseTransformDirection(src.TransformDirection(norms[i]));
            }
            float cutY = Mathf.Lerp(minY, maxY, cutFrac);

            int subCount = Mathf.Max(1, mesh.subMeshCount);
            var meshes = new Mesh[2];
            for (int side = 0; side < 2; side++)
            {
                bool upper = side == 1;
                var nm = new Mesh { name = mesh.name + (upper ? "_upper" : "_base") };
                nm.vertices = local;
                if (localN != null) nm.normals = localN;
                if (uvs != null && uvs.Length == verts.Length) nm.uv = uvs;
                nm.subMeshCount = subCount;
                for (int s = 0; s < subCount; s++)
                {
                    int[] srcTris = mesh.GetTriangles(s);
                    var keep = new List<int>(srcTris.Length);
                    for (int t = 0; t + 2 < srcTris.Length; t += 3)
                    {
                        float cy = (local[srcTris[t]].y + local[srcTris[t + 1]].y + local[srcTris[t + 2]].y) / 3f;
                        if ((cy >= cutY) == upper)
                        {
                            keep.Add(srcTris[t]); keep.Add(srcTris[t + 1]); keep.Add(srcTris[t + 2]);
                        }
                    }
                    nm.SetTriangles(keep.ToArray(), s);
                }
                nm.RecalculateBounds();
                meshes[side] = nm;
            }
            _splitCache[mesh] = meshes;
            _cutCache[mesh] = cutY;
            cutPlane = cutY;
            if (_verbose)
                Debug.Log(string.Format("[gun] split frame cut {0:0.000} (y {1:0.000}..{2:0.000}, {3:0.00}% )",
                    cutY, minY, maxY, cutFrac * 100f));
            return meshes;
        }

        void BuildGun(int i)
        {
            // mount (fixed to the wagon) -> yaw pivot -> recoil sleeve -> turret model
            var mount = new GameObject("GunMount" + i).transform;
            mount.SetParent(_wagons[i % _wagons.Count], false);

            var mounts = BuildMounts();
            if (mounts.Count > 0)
            {
                Mount slot = mounts[Mathf.Min(i, mounts.Count - 1)];
                mount.SetParent(slot.Parent, false);
                // Local mount from cached geometry: stable through yaw, curves and wagon adds.
                mount.localPosition = slot.Local + Vector3.up * 0.03f;
            }
            else
            {
                mount.localPosition = new Vector3(0f, 4.15f, 0.6f);
            }

            var yaw = new GameObject("Yaw").transform;
            yaw.SetParent(mount, false);

            var pitch = new GameObject("Pitch").transform;
            pitch.SetParent(yaw, false);

            var recoil = new GameObject("Recoil").transform;
            recoil.SetParent(pitch, false);

            var turretPrefab = Resources.Load<GameObject>("Models/turret");
            Transform model = null;
            if (turretPrefab != null)
                model = PlaceModel(turretPrefab, recoil, "Models/turret_albedo", 2.2f, 1.35f, 0f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(recoil, false);
            if (model != null)
            {
                // Pedestal pivot: recentre the mesh so its base centre sits at the mount origin.
                // Centring the whole AABB would orbit the pedestal as the gun yaws.
                Bounds lb = new Bounds(model.position, Vector3.zero);
                var rs = model.GetComponentsInChildren<Renderer>();
                bool first = true;
                foreach (var r in rs)
                {
                    var lbb = r.localBounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? lbb.min.x : lbb.max.x,
                            (c & 2) == 0 ? lbb.min.y : lbb.max.y,
                            (c & 4) == 0 ? lbb.min.z : lbb.max.z);
                        Vector3 p = recoil.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (first) { lb = new Bounds(p, Vector3.zero); first = false; }
                        else lb.Encapsulate(p);
                    }
                }
                model.localPosition -= new Vector3(lb.center.x, lb.min.y, lb.center.z);

                Bounds lb2 = new Bounds(model.position, Vector3.zero);
                first = true;
                foreach (var r in rs)
                {
                    var lbb = r.localBounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = new Vector3(
                            (c & 1) == 0 ? lbb.min.x : lbb.max.x,
                            (c & 2) == 0 ? lbb.min.y : lbb.max.y,
                            (c & 4) == 0 ? lbb.min.z : lbb.max.z);
                        Vector3 p = recoil.InverseTransformPoint(r.transform.TransformPoint(corner));
                        if (first) { lb2 = new Bounds(p, Vector3.zero); first = false; }
                        else lb2.Encapsulate(p);
                    }
                }
                muzzle.localPosition = new Vector3(0f, lb2.center.y, lb2.max.z);
            }
            else
            {
                muzzle.localPosition = new Vector3(0f, 0.35f, 1.1f);
            }

            // Muzzle flash is drawn by Effects ("muzzle_core") at this same muzzle transform;
            // a second quad here re-used the blue tracer material and read as a blue slab.

            if (model != null)
            {
                var mf = model.GetComponent<MeshFilter>();
                var smr = model.GetComponent<MeshRenderer>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh != null && mesh.isReadable && smr != null)
                {
                    Mesh[] parts = SplitTurretPart(model, mesh, recoil, 0.34f, out float hingeY);
                    var baseGo = new GameObject(model.name + "_Base");
                    baseGo.transform.SetParent(mount, false);
                    baseGo.AddComponent<MeshFilter>().sharedMesh = parts[0];
                    baseGo.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;

                    var upperGo = new GameObject(model.name + "_Upper");
                    upperGo.transform.SetParent(recoil, false);
                    upperGo.AddComponent<MeshFilter>().sharedMesh = parts[1];
                    upperGo.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;

                    Object.Destroy(model.gameObject);
                    model = upperGo.transform;

                    // Barrel tip centre, not the mesh AABB centre: the muzzle sits on the bore
                    // axis so the alignment gate and the spawned round match the visible barrel.
                    var uverts = parts[1].vertices;
                    float maxZ = parts[1].bounds.max.z;
                    float tipX = 0f, tipY = 0.9f;
                    int tipN = 0;
                    for (int vi = 0; vi < uverts.Length; vi++)
                    {
                        if (uverts[vi].z < maxZ - 0.08f) continue;
                        tipX += uverts[vi].x;
                        tipY += uverts[vi].y;
                        tipN++;
                    }
                    if (tipN > 0) { tipX /= tipN; tipY /= tipN; }
                    muzzle.localPosition = new Vector3(tipX, tipY, maxZ);

                    // Real trunnion axis measured from the side discs: pitch about X at the
                    // circular bearing, not the planar cut. The recoil sleeve is offset back by
                    // the same vector so the resting pose is unchanged.
                    const float trunnionY = 0.845f;
                    const float trunnionZ = -0.26f;
                    pitch.localPosition = new Vector3(0f, trunnionY, trunnionZ);
                    recoil.localPosition = new Vector3(0f, -trunnionY, -trunnionZ);

                    // Closed overlapping sleeve/pin in the turret's own material: it stays with
                    // yaw and hides the cut and stem while the receiver pitches and recoils.
                    AddTrunnionSleeve(yaw, smr.sharedMaterials[0], trunnionY, trunnionZ);

                    // Cap the planar cut on both halves. Without this the receiver's hollow
                    // skirt is visible through the cut at high depression (the "hinge wedge"):
                    // each half now presents a solid bearing plate instead of open backfaces.
                    AddCutCaps(mount, recoil, parts[0], parts[1], hingeY);

                    if (_verbose)
                        Debug.Log(string.Format("[gun] split ok base {0} upper {1} muzzle {2} hinge {3:0.000} trunnion y{4:0.000} z{5:0.000}",
                            parts[0].vertexCount, parts[1].vertexCount, muzzle.localPosition,
                            hingeY, trunnionY, trunnionZ));
                }
                else
                {
                    Debug.LogWarning("[gun] turret mesh NOT readable — base still moves with recoil");
                }
            }

            var rig = mount.gameObject.AddComponent<GunRig>();
            rig.gunIndex = i;
            rig.Init(GetComponentInParent<GameLoop>(), mount, yaw, pitch, recoil, muzzle, model);

            _guns.Add(mount);
        }

        void AddTrunnionSleeve(Transform yawParent, Material mat, float trunnionY, float trunnionZ)
        {
            if (yawParent == null || mat == null) return;

            // Stem collar: closed overlap from the pedestal into the pitching receiver, in the
            // turret's own material. It stays with yaw, so pitch/recoil can never open the joint
            // to air. Sized to sit inside the existing bracket silhouette.
            var collar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            collar.name = "TrunnionCollar";
            Object.Destroy(collar.GetComponent<Collider>());
            collar.transform.SetParent(yawParent, false);
            collar.transform.localPosition = new Vector3(0f, 0.62f, -0.24f);
            collar.transform.localScale = new Vector3(0.60f, 0.34f, 0.60f);
            collar.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Trunnion pin through both side bearings: a fixed shaft the receiver rotates on.
            var pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pin.name = "TrunnionPin";
            Object.Destroy(pin.GetComponent<Collider>());
            pin.transform.SetParent(yawParent, false);
            pin.transform.localPosition = new Vector3(0f, trunnionY, trunnionZ);
            pin.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            pin.transform.localScale = new Vector3(0.18f, 0.64f, 0.18f);
            pin.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// Two thin bearing plates at the split plane, one per half. The turret mesh is a
        /// hollow shell cut by a plane; when the receiver pitches 70 degrees the open cut is
        /// visible as a wedge of interior backfaces. Capping each half closes it without
        /// moving the base, the hinge or the muzzle: the base cap stays on the mount, the
        /// upper cap travels with the recoil sleeve.
        /// </summary>
        void AddCutCaps(Transform mount, Transform recoil, Mesh baseMesh, Mesh upperMesh, float cutY)
        {
            if (mount == null || recoil == null) return;
            Material mat = FlatMetal();

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            CollectCut(baseMesh, cutY, ref minX, ref maxX, ref minZ, ref maxZ);
            CollectCut(upperMesh, cutY, ref minX, ref maxX, ref minZ, ref maxZ);
            if (minX > maxX) return;

            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float rx = ((maxX - minX) * 0.5f + 0.03f) * 0.94f;
            float rz = ((maxZ - minZ) * 0.5f + 0.03f) * 0.94f;

            // Base cap: the split assigns whole triangles by centroid, so the base's top edge
            // can sit above the cut plane and show a strip of interior skin.
            var capBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            capBase.name = "TurretCutCapBase";
            Object.Destroy(capBase.GetComponent<Collider>());
            capBase.transform.SetParent(mount, false);
            capBase.transform.localPosition = new Vector3(cx, cutY + 0.18f, cz);
            capBase.transform.localScale = new Vector3(rx * 2f, 0.035f, rz * 2f);
            capBase.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Upper cap: closes the receiver's open bottom, slightly inset so it stays inside
            // the silhouette at rest and only shows as the receiver's underside when the gun
            // depresses.
            var capUpper = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            capUpper.name = "TurretCutCapUpper";
            Object.Destroy(capUpper.GetComponent<Collider>());
            capUpper.transform.SetParent(recoil, false);
            capUpper.transform.localPosition = new Vector3(cx, cutY - 0.06f, cz);
            capUpper.transform.localScale = new Vector3(rx * 1.7f, 0.035f, rz * 1.7f);
            capUpper.GetComponent<MeshRenderer>().sharedMaterial = mat;

            if (_verbose)
                Debug.Log(string.Format("[gun] cut caps at y {0:0.000} centre ({1:0.00},{2:0.00}) radius ({3:0.00},{4:0.00})",
                    cutY, cx, cz, rx, rz));
        }

        static Material _flatMetal;

        /// <summary>Untextured steel for the turret ring/cap. The atlas materials tile the
        /// whole sheet onto a primitive cylinder, which reads as camouflage; a flat URP/Lit
        /// metal is what a machined bearing should look like.</summary>
        static Material FlatMetal()
        {
            if (_flatMetal != null) return _flatMetal;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _flatMetal = new Material(shader) { name = "TurretRingSteel" };
            _flatMetal.color = new Color(0.34f, 0.36f, 0.39f, 1f);
            if (_flatMetal.HasProperty("_Smoothness")) _flatMetal.SetFloat("_Smoothness", 0.5f);
            if (_flatMetal.HasProperty("_Metallic")) _flatMetal.SetFloat("_Metallic", 0.65f);
            return _flatMetal;
        }

        static void CollectCut(Mesh mesh, float cutY, ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            if (mesh == null) return;
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                if (Mathf.Abs(v[i].y - cutY) > 0.06f) continue;
                minX = Mathf.Min(minX, v[i].x);
                maxX = Mathf.Max(maxX, v[i].x);
                minZ = Mathf.Min(minZ, v[i].z);
                maxZ = Mathf.Max(maxZ, v[i].z);
            }
        }

        Transform MakeBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        Transform MakeCylinder(Transform parent, string name, Vector3 pos, float radius, float height, Material mat,
                               Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            if (localRot.HasValue) go.transform.localRotation = localRot.Value;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go.transform;
        }

        void MakeWheel(Transform parent, Vector3 pos)
        {
            var w = MakeCylinder(parent, "Wheel", pos, 0.4f, 0.22f, _metal);
            w.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        Camera _gunCam;
        bool _gunCamBuilt;
        readonly List<Camera> _hiddenCams = new List<Camera>();

        /// <summary>
        /// Fixed inspection view of one turret, framed from the gun's own renderer bounds.
        /// dev.gunscam=1 enables it; dev.gunscam.gun selects the mount (0 = loco plate 1).
        /// No timed side toggle: the pose is explicit and every frame is the same shot.
        /// </summary>
        void UpdateGunCam()
        {
            if (!_gunCamBuilt)
            {
                if (TrainSurvivor.Game.DevTuning.Get("dev.gunscam", 0f) < 0.5f) return;
                _gunCamBuilt = true;
                var go = new GameObject("GunInspectCam");
                _gunCam = go.AddComponent<Camera>();
                _gunCam.depth = 100f;
                _gunCam.fieldOfView = 42f;
                _gunCam.nearClipPlane = 0.05f;
                _gunCam.clearFlags = CameraClearFlags.Skybox;

                // Only this camera renders while inspecting; restore the rest on teardown.
                foreach (var c in Camera.allCameras)
                {
                    if (c == _gunCam) continue;
                    _hiddenCams.Add(c);
                    c.enabled = false;
                }
                if (_verbose) Debug.Log("[mount] inspection camera on (explicit bounds pose)");
            }

            if (_gunCam == null || _guns.Count == 0) return;

            int gi = Mathf.Clamp((int)TrainSurvivor.Game.DevTuning.Get("dev.gunscam.gun", 0f),
                                 0, _guns.Count - 1);
            Transform gun = _guns[gi];

            Bounds b = new Bounds(gun.position, Vector3.zero);
            bool any = false;
            foreach (var r in gun.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any) b = new Bounds(gun.position + Vector3.up * 1.2f, Vector3.one * 2f);

            Vector3 focus = b.center;
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float d = Mathf.Max(2.6f, size * 1.75f + 0.9f);

            // Behind and to the side of the car (back is -forward), at a positive world-up
            // height. The old '-back * 6' pointed AHEAD of the loco and cropped it.
            Vector3 right = LeadRotation * Vector3.right;
            Vector3 back = LeadRotation * Vector3.back;
            _gunCam.transform.position = focus + back * (d * 0.35f) + right * (d * 0.95f)
                                        + Vector3.up * (b.size.y * 0.45f);
            _gunCam.transform.LookAt(focus);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _hiddenCams.Count; i++)
                if (_hiddenCams[i] != null) _hiddenCams[i].enabled = true;
        }

        public void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;

            float s = loop.World.Train.S;

            // _wagons[0] is the locomotive; Train.Wagons counts the freight cars behind it.
            int wantWagons = Mathf.Min(SimConfig.MaxWagons + 1, loop.World.Train.Wagons + 1);
            while (_wagons.Count < wantWagons) BuildFreightWagon(_wagons.Count);

            int wantGuns = Mathf.Min(SimConfig.MaxGuns, loop.World.Train.Guns);
            // Capacity-aware: one mount per plate (2 on the loco, 2 per wagon). Guns past the
            // plate count stay in inventory but get no geometry, instead of stacking on the
            // last plate the way Mathf.Min(i, count-1) used to.
            int mountsAvailable = BuildMounts().Count;
            wantGuns = Mathf.Min(wantGuns, mountsAvailable);
            while (_guns.Count < wantGuns) BuildGun(_guns.Count);

            for (int i = 0; i < _wagons.Count; i++)
            {
                float ws = s - i * WagonLength;
                _wagons[i].SetPositionAndRotation(
                    TrackSpace.Point(ws, 0f, 0f),
                    TrackSpace.Facing(ws));
            }

            UpdateGunCam();

            int guns = loop.World.Train.Guns;
            for (int i = 0; i < _guns.Count; i++) _guns[i].gameObject.SetActive(i < guns);

            // turrets aim themselves in GunRig.Update (mount stays fixed, yaw pivot turns)
        }

        public Vector3 MuzzlePosition(int gunIndex)
        {
            if (_guns.Count == 0) return LeadPosition + Vector3.up * 2f;
            int i = Mathf.Clamp(gunIndex, 0, _guns.Count - 1);
            var rig = _guns[i].GetComponent<GunRig>();
            if (rig != null && rig.muzzle != null) return rig.muzzle.position;
            return _guns[i].TransformPoint(new Vector3(0f, 0.15f, 1.35f));
        }

        /// <summary>The aimed barrel axis of the gun that fired, so its round leaves the muzzle
        /// along that barrel rather than toward a point in sim space.</summary>
        public Vector3 MuzzleForward(int gunIndex)
        {
            if (_guns.Count == 0) return transform.forward;
            int i = Mathf.Clamp(gunIndex, 0, _guns.Count - 1);
            var rig = _guns[i].GetComponent<GunRig>();
            if (rig != null && rig.muzzle != null) return rig.muzzle.forward;
            return _guns[i].forward;
        }
    }
}

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
        Transform _locoModel;

        public Vector3 LeadPosition => _wagons.Count > 0 ? _wagons[0].position : transform.position;
        public Quaternion LeadRotation => _wagons.Count > 0 ? _wagons[0].rotation : transform.rotation;

        public void Build(int wagons, int guns)
        {
            _body = Palette.Get("TrainBody", Palette.Body);
            _accent = Palette.Get("TrainAccent", Palette.Accent);
            _metal = Palette.Get("Metal", Palette.Metal);
            _glass = Palette.Get("CabGlass", new Color(0.05f, 0.09f, 0.14f));

            BuildLocomotive();
            for (int i = 1; i < wagons; i++) BuildFreightWagon(i);

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

            // The export's axis convention is not reliable: stand it up on its wheels first, then
            // put its longest horizontal axis down the track. Both steps are measured, not assumed.
            Bounds Measure()
            {
                Bounds bb = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bb.Encapsulate(renderers[i].bounds);
                return bb;
            }

            Bounds b = Measure();
            if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.2f)
            {
                // the sign matters: -90 and +90 both make the box the right shape, but only one
                // leaves the roof up. dev.locoPitch overrides it without a rebuild.
                float pitch = DevTuning.Get("dev.locoPitch", -90f);
                model.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                b = Measure();
                Debug.Log("[loco] model was lying along Y — pitched " + pitch);
            }
            if (b.size.x > b.size.z * 1.2f)
            {
                var q = model.transform.localRotation;
                model.transform.localRotation = q * Quaternion.Euler(0f, 90f, 0f);
                b = Measure();
                Debug.Log("[loco] length was along X — yawed 90");
            }

            // authored facing correction: the export points its cab at -Z, which is the side the chase
            // camera looks at, so 0 keeps the cab (not the smoke-box door) facing the player
            float yaw = DevTuning.Get("dev.locoYaw", 0f);
            model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, yaw, 0f);

            b = Measure();
            float scale = LocoLength / Mathf.Max(b.size.z, 0.01f);
            model.transform.localScale = Vector3.one * scale;

            // re-measure and drop onto the rail, centred on the wagon pivot
            b = Measure();
            Vector3 localCenter = root.InverseTransformPoint(b.center);
            float dropTo = root.position.y;
            model.transform.localPosition = new Vector3(-localCenter.x, dropTo - b.min.y, -localCenter.z);

            _locoMat = MakeLocoMaterial();
            if (_locoMat != null)
                foreach (var r in renderers) r.sharedMaterial = _locoMat;

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


        /// <summary>
        /// Instantiate a modelled asset under a wagon/gun root: stand it up, put its long axis down
        /// the track, scale it to the wanted length or height, and sit it on the floor of its parent.
        /// Returns false if the model cannot be used, so callers fall back to primitives.
        /// </summary>
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

            Bounds Measure()
            {
                Bounds bb = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bb.Encapsulate(renderers[i].bounds);
                return bb;
            }

            Bounds b = Measure();
            if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.15f)
            {
                model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                b = Measure();
            }
            if (b.size.x > b.size.z * 1.15f)
            {
                model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 90f, 0f);
                b = Measure();
            }
            model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, yaw, 0f);

            b = Measure();
            float lengthScale = targetLength / Mathf.Max(b.size.z, 0.01f);
            float heightScale = targetHeight / Mathf.Max(b.size.y, 0.01f);
            model.transform.localScale = Vector3.one * Mathf.Min(lengthScale, heightScale);

            b = Measure();
            Vector3 localCenter = parent.InverseTransformPoint(b.center);
            model.transform.localPosition = new Vector3(-localCenter.x, -localCenter.y + b.extents.y, -localCenter.z);

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


        /// <summary>World position of a mount plate, from the model's current world bounds.</summary>
        Vector3 PlatePoint(Transform model, Vector3 frac)
        {
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return model.position;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return new Vector3(
                b.min.x + frac.x * b.size.x,
                b.min.y + frac.y * b.size.y,
                b.min.z + frac.z * b.size.z);
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

                var mounts = w == 0 ? LocoMounts : WagonMounts;
                for (int m = 0; m < mounts.Length; m++)
                    list.Add(new Mount { Parent = parent, Model = model, Local = mounts[m] });
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
                Vector3 world = slot.Model != null
                    ? PlatePoint(slot.Model, slot.Local)
                    : slot.Parent.TransformPoint(slot.Local);
                mount.localPosition = slot.Parent.InverseTransformPoint(world) + Vector3.up * 0.03f;
            }
            else
            {
                mount.localPosition = new Vector3(0f, 4.15f, 0.6f);
            }

            var yaw = new GameObject("Yaw").transform;
            yaw.SetParent(mount, false);

            var recoil = new GameObject("Recoil").transform;
            recoil.SetParent(yaw, false);

            var turretPrefab = Resources.Load<GameObject>("Models/turret");
            Transform model = null;
            if (turretPrefab != null)
                model = PlaceModel(turretPrefab, recoil, "Models/turret_albedo", 2.2f, 1.35f, 0f);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(recoil, false);
            if (model != null)
            {
                var rs = model.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
                    muzzle.position = new Vector3(b.center.x, b.center.y, b.max.z);
                }
            }
            else
            {
                muzzle.localPosition = new Vector3(0f, 0.35f, 1.1f);
            }

            // muzzle flash: an unlit quad at the tip, enabled for a few frames per shot
            var flashGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(flashGo.GetComponent<Collider>());
            flashGo.name = "MuzzleFlash";
            flashGo.transform.SetParent(muzzle, false);
            flashGo.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
            // a bespoke URP/Unlit material gets its shader stripped from the Android build and
            // renders magenta; reuse the tracer material that already ships in Resources/M0
            var flashMat = Resources.Load<Material>("M0/Tracer");
            if (flashMat == null) flashMat = Resources.Load<Material>("M0/Spark");
            if (flashMat != null) flashGo.GetComponent<Renderer>().sharedMaterial = flashMat;
            else Debug.LogWarning("[gun] no flash material found");
            var flash = flashGo.GetComponent<Renderer>();
            flash.enabled = false;

            var rig = mount.gameObject.AddComponent<GunRig>();
            rig.gunIndex = i;
            rig.Init(GetComponentInParent<GameLoop>(), mount, yaw, recoil, muzzle, flash, model);

            _guns.Add(mount);
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

        public void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;

            float s = loop.World.Train.S;

            int wantWagons = Mathf.Min(SimConfig.MaxWagons, loop.World.Train.Wagons);
            while (_wagons.Count < wantWagons) BuildFreightWagon(_wagons.Count);

            int wantGuns = Mathf.Min(SimConfig.MaxGuns, loop.World.Train.Guns);
            while (_guns.Count < wantGuns) BuildGun(_guns.Count);

            for (int i = 0; i < _wagons.Count; i++)
            {
                float ws = s - i * WagonLength;
                _wagons[i].SetPositionAndRotation(
                    TrackSpace.Point(ws, 0f, 0f),
                    TrackSpace.Facing(ws));
            }

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
    }
}

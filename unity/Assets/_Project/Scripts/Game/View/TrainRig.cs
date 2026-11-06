using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Builds the train out of primitives and places it on the rail every frame.
    /// Wagon 0 is the locomotive (the thing zombies eat); the rest trail behind it.
    /// </summary>
    public sealed class TrainRig : MonoBehaviour
    {
        const float WagonLength = 6.6f;

        readonly List<Transform> _wagons = new List<Transform>();
        readonly List<Transform> _guns = new List<Transform>();
        Material _body, _accent, _metal, _glass;

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

            MakeBox(root, "Hull", new Vector3(0f, 1.3f, 0f), new Vector3(2.5f, 1.4f, 6.4f), _body);
            MakeBox(root, "Skirt", new Vector3(0f, 0.62f, 0f), new Vector3(2.62f, 0.28f, 6.2f), _metal);

            // Cab sits at the back (we watch it from behind), so it is low and navy with a
            // bright roof band and a rear window instead of a giant red slab.
            MakeBox(root, "Cab", new Vector3(0f, 2.45f, -1.75f), new Vector3(2.2f, 1.05f, 1.9f), _body);
            MakeBox(root, "CabRoof", new Vector3(0f, 3.02f, -1.75f), new Vector3(2.34f, 0.16f, 2.04f), _accent);
            MakeBox(root, "CabWindow", new Vector3(0f, 2.52f, -2.72f), new Vector3(1.35f, 0.55f, 0.06f), _glass);
            MakeBox(root, "CabStripe", new Vector3(0f, 2.0f, -2.71f), new Vector3(2.1f, 0.18f, 0.05f), _accent);

            // Boiler barrel + chimney, visible over the cab from the chase camera.
            MakeCylinder(root, "Boiler", new Vector3(0f, 2.15f, 1.15f), 0.78f, 3.0f, _body,
                         Quaternion.Euler(90f, 0f, 0f));
            MakeCylinder(root, "Chimney", new Vector3(0f, 3.05f, 2.35f), 0.28f, 1.0f, _metal);
            MakeCylinder(root, "Dome", new Vector3(0f, 2.95f, 0.35f), 0.42f, 0.55f, _accent);

            // Cowcatcher — the visual promise that ramming works.
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

        void BuildFreightWagon(int index)
        {
            var root = new GameObject("Wagon" + index).transform;
            root.SetParent(transform, false);
            _wagons.Add(root);

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
            var wagon = _wagons[i % _wagons.Count];
            var root = new GameObject("Gun" + i).transform;
            root.SetParent(wagon, false);

            float x = (i % 2 == 0 ? -0.55f : 0.55f) + (i / 2) * 0.22f;
            root.localPosition = new Vector3(x, 2.0f, 0.9f);

            MakeCylinder(root, "Mount", Vector3.zero, 0.22f, 0.3f, _metal);
            float barrelLength = 1.25f;
            MakeCylinder(root, "Barrel", new Vector3(0f, 0.15f, barrelLength * 0.5f), 0.085f, barrelLength, _metal);

            // Point the barrel down the track.
            var barrel = root.Find("Barrel");
            if (barrel != null) barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _guns.Add(root);
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

            // Cards grow the train at runtime: hitch new wagons, bolt on new guns.
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

            // Guns only exist for as many as the sim says.
            int guns = loop.World.Train.Guns;
            for (int i = 0; i < _guns.Count; i++) _guns[i].gameObject.SetActive(i < guns);
        }

        public Vector3 MuzzlePosition(int gunIndex)
        {
            if (_guns.Count == 0) return LeadPosition + Vector3.up * 2f;
            int i = Mathf.Clamp(gunIndex, 0, _guns.Count - 1);
            return _guns[i].TransformPoint(new Vector3(0f, 0.15f, 1.35f));
        }
    }
}

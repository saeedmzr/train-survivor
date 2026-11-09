using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// One turret: a fixed mount on the locomotive/wagon, a yaw pivot for aiming, a recoil sleeve
    /// that kicks back on every shot, and a muzzle transform with a flash. Aim and recoil live on
    /// separate transforms, so a shot never fights the tracking.
    ///
    /// The gun owns target selection and reports (target, aligned) to the sim; the sim only fires
    /// a gun whose barrel is on target, and damages that same enemy — aim and damage cannot diverge.
    /// </summary>
    public sealed class GunRig : MonoBehaviour
    {
        [Header("Identity")]
        public int gunIndex;

        [Header("Aiming")]
        [Tooltip("Degrees of error allowed before the gun is considered on target")]
        [Range(0.2f, 20f)] public float aimToleranceDeg = 1f;
        [Tooltip("How fast the turret swings, in degrees per second")]
        [Range(30f, 720f)] public float aimSpeedDeg = 220f;
        [Tooltip("Only enemies inside this range are considered")]
        public float rangeM = 45f;
        [Tooltip("Maximum elevation, up or down, in degrees")]
        [Range(0f, 30f)] public float maxPitchUpDeg = 25f;
        [Range(0f, 85f)] public float maxPitchDownDeg = 70f;

        [Header("Recoil")]
        [Tooltip("How far the recoil sleeve travels back, in metres")]
        [Range(0f, 0.2f)] public float recoilTravel = 0.10f;
        [Tooltip("Recoil return speed — higher snaps back faster")]
        [Range(1f, 40f)] public float recoilReturn = 8f;

        [Header("Wiring (built by TrainRig)")]
        public Transform mount;
        public Transform yaw;
        public Transform pitch;
        public Transform recoil;
        public Transform muzzle;

        Transform _model;
        float _recoilOffset;
        int _targetId = -1;
        bool _aligned;
        GameLoop _loop;

        public int TargetId => _targetId;
        public bool Aligned => _aligned;

        public void Init(GameLoop loop, Transform mountT, Transform yawT, Transform pitchT,
                         Transform recoilT, Transform muzzleT, Transform model)
        {
            _loop = loop;
            mount = mountT;
            yaw = yawT;
            pitch = pitchT;
            recoil = recoilT;
            muzzle = muzzleT;
            _model = model;
            _recoilOffset = 0f;
        }

        bool _subscribed;

        /// <summary>Report the current barrel state before the first sim step, so the sim never
        /// fires a startup round from the rest pose (GunAimDriven would still be false).</summary>
        void Start()
        {
            var loop = _loop != null ? _loop : GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            _loop = loop;
            Aim(loop.World, 0f);
        }

        void Update()
        {
            var loop = _loop != null ? _loop : GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            _loop = loop;

            if (!_subscribed)
            {
                loop.SimEventRaised += OnSimEvent;
                _subscribed = true;
            }

            Aim(loop.World, Time.deltaTime);
            Recoil(Time.deltaTime);

            // Inspection-only fixed pose (dev.gunYaw / dev.gunPitch / dev.gunRecoil): no
            // timed toggle, the same pose every frame, and the sim gets no aim so a posed
            // turret cannot fire during the shot.
            float forcedYaw = TrainSurvivor.Game.DevTuning.Get("dev.gunYaw", -999f);
            if (forcedYaw > -900f) ApplyForcedPose(loop.World, forcedYaw);
        }

        void ApplyForcedPose(World world, float forcedYaw)
        {
            if (yaw != null) yaw.localRotation = Quaternion.Euler(0f, forcedYaw, 0f);
            float forcedPitch = TrainSurvivor.Game.DevTuning.Get("dev.gunPitch", 0f);
            if (pitch != null) pitch.localRotation = Quaternion.Euler(forcedPitch, 0f, 0f);

            _recoilOffset = Mathf.Clamp(TrainSurvivor.Game.DevTuning.Get("dev.gunRecoil", 0f),
                                        0f, recoilTravel);
            if (recoil != null)
            {
                Vector3 p = recoil.localPosition;
                p.z = -_recoilOffset;
                recoil.localPosition = p;
            }

            if (world != null) world.SetGunAim(gunIndex, -1, false, false);
        }

        void OnDisable()
        {
            if (_subscribed && _loop != null) _loop.SimEventRaised -= OnSimEvent;
            _subscribed = false;
        }

        /// <summary>One recoil kick and one flash per confirmed shot from this gun.</summary>
        void OnSimEvent(SimEvent e)
        {
            if (e.Kind != SimEventKind.Shot || e.GunIndex != gunIndex) return;
            Kick();
        }

        // ---------------------------------------------------------------- aiming

        void Aim(World world, float dt)
        {
            if (yaw == null || muzzle == null) return;

            int targetId;
            float targetS, targetX;
            bool isBlockade;
            bool found = FindTarget(world, out targetId, out targetS, out targetX, out isBlockade);

            if (!found)
            {
                _targetId = -1;
                _aligned = false;
                world.SetGunAim(gunIndex, -1, false, false);
                return;
            }

            Vector3 muzzlePos = muzzle.position;
            Vector3 to = TrackSpace.Point(targetS, targetX, isBlockade ? 1.1f : 0.9f) - muzzlePos;

            float dist = to.magnitude;
            if (dist < 0.25f)
            {
                _aligned = true;
                _targetId = targetId;
                world.SetGunAim(gunIndex, targetId, true, isBlockade);
                return;
            }

            Vector3 flat = new Vector3(to.x, 0f, to.z);
            Vector3 wantDir = flat.sqrMagnitude > 0.0001f ? flat.normalized : yaw.forward;
            Quaternion want = Quaternion.LookRotation(wantDir, Vector3.up);

            // swing at a fixed angular rate, so a distant target does not snap the turret
            float step = aimSpeedDeg * dt;
            yaw.rotation = Quaternion.RotateTowards(yaw.rotation, want, step);

            // bounded elevation toward the full 3D target (negative pitch aims down)
            if (pitch != null && flat.magnitude > 0.01f)
            {
                float wantPitch = -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
                // lower target -> negative to.y -> POSITIVE wantPitch (nose down)
                wantPitch = Mathf.Clamp(wantPitch, -maxPitchUpDeg, maxPitchDownDeg);
                pitch.localRotation = Quaternion.RotateTowards(pitch.localRotation,
                    Quaternion.Euler(wantPitch, 0f, 0f), step);
            }

            float err = Vector3.Angle(muzzle.forward, to.normalized);
            _aligned = err <= aimToleranceDeg;
            _targetId = targetId;
            world.SetGunAim(gunIndex, targetId, _aligned, isBlockade);
        }

        /// <summary>
        /// Nearest enemy in range; the barricade the train is pinned against outranks everything,
        /// and with no enemies in range a blockade ahead is a legal target so the wall can be
        /// pre-broken. Without this the turrets only ever saw enemies and a barricade never died.
        /// </summary>
        bool FindTarget(World world, out int id, out float s, out float x, out bool isBlockade)
        {
            id = -1;
            s = 0f;
            x = 0f;
            isBlockade = false;

            Vector3 p = muzzle != null ? muzzle.position : transform.position;
            float trainS = world.Train.S;

            if (world.Train.Mode != TrainMode.Moving)
            {
                bool haveWall = false;
                float wallGap = float.MaxValue;
                for (int i = 0; i < world.BlockadeCount; i++)
                {
                    Blockade b = world.Blockades[i];
                    if (!b.Alive) continue;
                    float gap = b.S - trainS;
                    if (gap < -1f || gap > 7.5f) continue;
                    if (gap < wallGap) { wallGap = gap; id = b.Id; s = b.S; x = b.X; haveWall = true; }
                }
                if (haveWall) { isBlockade = true; return true; }
            }

            float bestSqr = rangeM * rangeM;
            bool found = false;

            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive) continue;

                // Roof turrets traverse 360°: anything inside range is fair game, including the
                // walkers that get behind the convoy and bite the wagons. Excluding them (the
                // old forward-only rule) left rear attackers unkillable.
                Vector3 ep = TrackSpace.Point(e.S, e.X, 0.9f);
                float d = (ep - p).sqrMagnitude;
                if (d > bestSqr) continue;

                bestSqr = d;
                id = e.Id;
                s = e.S;
                x = e.X;
                found = true;
            }
            if (found) return true;

            // no zombies: put rounds into the next barricade instead of standing idle
            for (int i = 0; i < world.BlockadeCount; i++)
            {
                Blockade b = world.Blockades[i];
                if (!b.Alive) continue;
                if (b.S < trainS) continue;
                Vector3 bp = TrackSpace.Point(b.S, b.X, 1.1f);
                if ((bp - p).sqrMagnitude > bestSqr) continue;
                id = b.Id;
                s = b.S;
                x = b.X;
                isBlockade = true;
                found = true;
            }
            return found;
        }

        // ---------------------------------------------------------------- recoil + flash

        /// <summary>Called by the view when the sim confirms a shot from this gun.</summary>
        public void Kick()
        {
            _recoilOffset = Mathf.Max(_recoilOffset, recoilTravel);
        }

        void Recoil(float dt)
        {
            if (recoil == null) return;
            _recoilOffset = Mathf.MoveTowards(_recoilOffset, 0f, recoilReturn * dt);
            Vector3 p = recoil.localPosition;
            p.z = -_recoilOffset;
            recoil.localPosition = p;
        }
    }
}

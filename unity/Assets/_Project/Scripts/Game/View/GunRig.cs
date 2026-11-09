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
        [Range(0.5f, 20f)] public float aimToleranceDeg = 6f;
        [Tooltip("How fast the turret swings, in degrees per second")]
        [Range(30f, 720f)] public float aimSpeedDeg = 220f;
        [Tooltip("Only enemies inside this range are considered")]
        public float rangeM = 45f;

        [Header("Recoil")]
        [Tooltip("How far the recoil sleeve travels back, in metres")]
        [Range(0f, 0.5f)] public float recoilTravel = 0.16f;
        [Tooltip("Recoil return speed — higher snaps back faster")]
        [Range(1f, 40f)] public float recoilReturn = 14f;
        [Tooltip("How long the muzzle flash stays lit")]
        [Range(0.01f, 0.2f)] public float flashTime = 0.045f;

        [Header("Wiring (built by TrainRig)")]
        public Transform mount;
        public Transform yaw;
        public Transform recoil;
        public Transform muzzle;
        public Renderer flash;

        Transform _model;
        float _recoilOffset;
        float _flashUntil;
        int _targetId = -1;
        bool _aligned;
        GameLoop _loop;

        public int TargetId => _targetId;
        public bool Aligned => _aligned;

        public void Init(GameLoop loop, Transform mountT, Transform yawT, Transform recoilT,
                         Transform muzzleT, Renderer flashR, Transform model)
        {
            _loop = loop;
            mount = mountT;
            yaw = yawT;
            recoil = recoilT;
            muzzle = muzzleT;
            flash = flashR;
            _model = model;
            _recoilOffset = 0f;
            if (flash != null) flash.enabled = false;
        }

        bool _subscribed;

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
            Flash();
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

            Enemy target;
            bool found = FindTarget(world, out target);

            if (!found)
            {
                _targetId = -1;
                _aligned = false;
                world.SetGunAim(gunIndex, -1, false);
                return;
            }

            Vector3 muzzlePos = muzzle.position;
            Vector3 to = TrackSpace.Point(target.S, target.X, 0.9f) - muzzlePos;
            to.y = 0f;

            float dist = to.magnitude;
            if (dist < 0.25f)
            {
                _aligned = true;
                _targetId = target.Id;
                world.SetGunAim(gunIndex, target.Id, true);
                return;
            }

            Vector3 wantDir = to / dist;
            Quaternion want = Quaternion.LookRotation(wantDir, Vector3.up);

            // swing at a fixed angular rate, so a distant target does not snap the turret
            float step = aimSpeedDeg * dt;
            yaw.rotation = Quaternion.RotateTowards(yaw.rotation, want, step);

            float err = Vector3.Angle(yaw.forward, wantDir);
            _aligned = err <= aimToleranceDeg;
            _targetId = target.Id;
            world.SetGunAim(gunIndex, target.Id, _aligned);
        }

        bool FindTarget(World world, out Enemy best)
        {
            best = default;
            float bestSqr = rangeM * rangeM;
            Vector3 p = muzzle != null ? muzzle.position : transform.position;
            float trainS = world.Train.S;
            bool found = false;

            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive) continue;

                // ignore anything already behind the train — the guns face forward
                if (e.S < trainS - 2f) continue;

                Vector3 ep = TrackSpace.Point(e.S, e.X, 0.9f);
                float d = (ep - p).sqrMagnitude;
                if (d > bestSqr) continue;

                bestSqr = d;
                best = e;
                found = true;
            }
            return found;
        }

        // ---------------------------------------------------------------- recoil + flash

        /// <summary>Called by the view when the sim confirms a shot from this gun.</summary>
        public void Kick()
        {
            _recoilOffset = Mathf.Max(_recoilOffset, recoilTravel);
            if (flash != null)
            {
                flash.enabled = true;
                _flashUntil = Time.time + flashTime;
            }
        }

        void Recoil(float dt)
        {
            if (recoil == null) return;
            _recoilOffset = Mathf.MoveTowards(_recoilOffset, 0f, recoilReturn * dt);
            Vector3 p = recoil.localPosition;
            p.z = -_recoilOffset;
            recoil.localPosition = p;
        }

        void Flash()
        {
            if (flash != null && flash.enabled && Time.time > _flashUntil) flash.enabled = false;
        }
    }
}

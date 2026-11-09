using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Chase camera. Yaw is locked to the track heading at the train — aiming at a point
    /// far down a curving track swings the train out of frame — and pitch comes from the
    /// camera height over the look distance, so framing stays identical through every curve.
    /// </summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        public TrainRig Train;
        // Framing is now computed from the whole convoy envelope instead of a fixed
        // distance: a longer train or a taller turret zooms out, and the fit is bounded by
        // the real viewport aspect so a 20:9 phone does not crop the last wagon.
        public float Distance = 20f;      // minimum chase distance
        public float Height = 16.5f;      // retained for tuning compatibility
        public float WagonPushback = 2.2f; // legacy; bounds fitting owns wagon expansion now
        public float ZoomSmoothing = 2.2f;
        public float LookAhead = 0f;      // aim at the combat envelope centre, not up the road
        public float LookHeight = 1.3f;
        public float Lateral = 0f;
        public float Smoothing = 9f;

        [Tooltip("Rear camera elevation above the convoy midpoint, degrees")]
        public float ElevationDeg = 34f;
        [Tooltip("Fraction of the viewport the convoy may use, leaving UI safe margins")]
        [Range(0.5f, 1f)] public float FitMarginX = 0.94f;
        [Range(0.4f, 1f)] public float FitMarginY = 0.82f;
        [Tooltip("Extra lateral room so walkers closing in from the sides stay visible")]
        public float ThreatMargin = 6f;

        Camera _cam;
        float _distance;
        bool _placed;

        // Convoy envelope samples (tail..nose, both sides, ground/roof). Fixed array: no
        // per-frame allocation and no scene searches.
        readonly Vector3[] _fitPoints = new Vector3[20];

        // Camera occlusion: only scenery (layer 6) is tested, so the train and horde never pull
        // the lens in. Radius keeps thin posts from slicing the view; hits closer than
        // IgnoreWithin are props beside the train, not between it and the lens.
        const float OcclusionRadius = 0.55f;
        const float MinDistance = 12f;
        const float IgnoreWithin = 8f;
        static readonly int SceneryMask = 1 << 6;

        public void Init(TrainRig train, Camera cam)
        {
            Train = train;
            _cam = cam;

            Distance = DevTuning.Get("cam.distance", Distance);
            Height = DevTuning.Get("cam.height", Height);
            LookAhead = DevTuning.Get("cam.lookAhead", LookAhead);
            LookHeight = DevTuning.Get("cam.lookHeight", LookHeight);
            Lateral = DevTuning.Get("cam.lateral", Lateral);
            Smoothing = DevTuning.Get("cam.smoothing", Smoothing);
            WagonPushback = DevTuning.Get("cam.wagonPush", WagonPushback);
            ElevationDeg = DevTuning.Get("cam.elevation", ElevationDeg);
            FitMarginX = DevTuning.Get("cam.fitX", FitMarginX);
            FitMarginY = DevTuning.Get("cam.fitY", FitMarginY);
            cam.fieldOfView = DevTuning.Get("cam.fov", cam.fieldOfView);

            _distance = Distance;
        }

        void LateUpdate()
        {
            if (Train == null || _cam == null) return;

            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            World w = loop.World;
            float s = w.Train.S;
            int wagons = Mathf.Max(1, w.Train.Wagons);

            // Whole combat envelope: a real ground margin behind the last wagon (rear
            // attackers must be visible), both flanks, and the cowcatcher. The train is not
            // the frame — the fight around it is.
            float tail = s - SimConfig.WagonSpan * wagons - 10f;
            float nose = s + SimConfig.TrainNose + 2f;
            float halfW = SimConfig.TrainHalfWidth + ThreatMargin;
            const float roof = 7.4f;
            int n = 0;
            for (int k = 0; k <= 4; k++)
            {
                float ss = Mathf.Lerp(tail, nose, k / 4f);
                for (int side = -1; side <= 1; side += 2)
                {
                    _fitPoints[n++] = TrackSpace.Point(ss, side * halfW, 0.6f);
                    _fitPoints[n++] = TrackSpace.Point(ss, side * halfW, roof);
                }
            }

            Bounds hull = new Bounds(_fitPoints[0], Vector3.zero);
            for (int i = 1; i < n; i++) hull.Encapsulate(_fitPoints[i]);

            float midS = (tail + nose) * 0.5f;
            Vector3 forward = TrackSpace.Facing(midS) * Vector3.forward;
            Vector3 target = hull.center + forward * LookAhead + Vector3.up * LookHeight;

            float aspect = Mathf.Max(0.5f, _cam.aspect);
            float fit = FitDistance(target, forward, aspect);
            float wantDistance = Mathf.Max(Distance, fit);
            if (!_placed)
            {
                _distance = wantDistance;
                _placed = true;
            }
            else
            {
                float zoomK = 1f - Mathf.Exp(-ZoomSmoothing * dt);
                _distance = Mathf.Lerp(_distance, wantDistance, zoomK);
            }

            float pitch = ElevationDeg * Mathf.Deg2Rad;
            Vector3 wantPos = target - forward * (Mathf.Cos(pitch) * _distance)
                                     + Vector3.up * (Mathf.Sin(pitch) * _distance);
            wantPos += TrackSpace.Right(midS) * Lateral;

            // Scenery only pulls the lens in when that still leaves the fitted convoy in
            // frame; otherwise keep the fitted shot and accept the occlusion for a moment.
            Vector3 offset = wantPos - target;
            float offsetLen = offset.magnitude;
            if (offsetLen > IgnoreWithin &&
                Physics.SphereCast(target, OcclusionRadius, offset / offsetLen, out RaycastHit hit,
                                   offsetLen, SceneryMask, QueryTriggerInteraction.Ignore))
            {
                float pull = hit.distance - 0.5f;
                if (pull > wantDistance * 0.85f && pull > MinDistance)
                    wantPos = target + offset / offsetLen * pull;
            }

            Vector3 toAim = target - wantPos;
            Quaternion wantRot = toAim.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toAim, Vector3.up)
                : TrackSpace.Facing(midS);

            float smooth = 1f - Mathf.Exp(-Smoothing * dt);
            transform.position = Vector3.Lerp(transform.position, wantPos, smooth);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, smooth);
        }

        /// <summary>Smallest rear distance at which every convoy envelope sample fits inside
        /// the aspect-aware viewport margins. Monotonic in distance, so a short bisection is
        /// exact enough and allocation-free.</summary>
        float FitDistance(Vector3 target, Vector3 forward, float aspect)
        {
            float vHalf = _cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float tanV = Mathf.Tan(vHalf) * FitMarginY;
            float tanH = Mathf.Tan(vHalf) * aspect * FitMarginX;
            float pitch = ElevationDeg * Mathf.Deg2Rad;

            float lo = 8f, hi = 120f;
            for (int it = 0; it < 18; it++)
            {
                float d = (lo + hi) * 0.5f;
                Vector3 pos = target - forward * (Mathf.Cos(pitch) * d)
                                     + Vector3.up * (Mathf.Sin(pitch) * d);
                Vector3 dir = target - pos;
                float len = dir.magnitude;
                if (len < 0.01f) { lo = d; continue; }
                dir /= len;
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
                else right.Normalize();
                Vector3 up = Vector3.Cross(dir, right);

                bool ok = true;
                for (int i = 0; i < _fitPoints.Length; i++)
                {
                    Vector3 v = _fitPoints[i] - pos;
                    float z = Vector3.Dot(v, dir);
                    if (z < 1f) { ok = false; break; }
                    if (Mathf.Abs(Vector3.Dot(v, right)) > z * tanH ||
                        Mathf.Abs(Vector3.Dot(v, up)) > z * tanV)
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok) hi = d; else lo = d;
            }
            return hi;
        }
    }
}

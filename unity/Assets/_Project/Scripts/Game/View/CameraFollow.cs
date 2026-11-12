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
        public float Distance = 8.3f;   // measured: silhouette was 26% of 960 at ~12 m       // minimum chase distance (the fit decides the rest)
        public float Height = 16.5f;      // retained for tuning compatibility
        public float WagonPushback = 2.2f; // legacy; bounds fitting owns wagon expansion now
        public float ZoomSmoothing = 2.2f;
        public float LookAhead = 0f;      // aim at the combat envelope centre, not up the road
        public float LookHeight = 2.9f;    // aim higher so the train sits low in frame (smokestack ~42%)
        public float Lateral = 0f;
        public float Smoothing = 9f;

        [Tooltip("Rear camera elevation above the convoy midpoint, degrees")]
        public float ElevationDeg = 27f;   // flatter rear chase: the convoy reads long, not top-down
        [Tooltip("Fraction of the viewport the convoy may use, leaving UI safe margins")]
        [Range(0.5f, 1f)] public float FitMarginX = 1.0f;  // portrait waist is the limiting axis
        [Range(0.4f, 1f)] public float FitMarginY = 1.0f;
        [Tooltip("Extra lateral room so walkers closing in from the sides stay visible")]
        public float ThreatMargin = 3f;

        Camera _cam;
        float _distance;
        bool _placed;
        float _blockLogAt;

        // Convoy envelope samples (tail..nose, both sides, ground/roof). Fixed array: no
        // per-frame allocation and no scene searches.
        readonly Vector3[] _fitPoints = new Vector3[20];
        // The rendered train alone (rear coupler..cowcatcher). The envelope keeps the fight
        // in frame; this box is what must stay above the bottom HUD.
        readonly Vector3[] _trainPoints = new Vector3[128];
        Renderer[] _trainRenderers;
        int _trainWagonCount = -1;

        // Camera occlusion: only scenery (layer 6) is tested, so the train and horde never pull
        // the lens in. Radius keeps thin posts from slicing the view; hits closer than
        // IgnoreWithin are props beside the train, not between it and the lens.
        const float OcclusionRadius = 0.55f;
        const float MinDistance = 8f;
        /// <summary>The fit solves for the whole bounding box plus padding, which left the starter convoy at
        /// 26% of portrait height in real renders. Scaling the solved distance brings the visible
        /// silhouette to the reference range while keeping the same relative fit for longer convoys.</summary>
        public float FitScale = 0.66f;
        public float OfferDistanceScale = 1.86f;   // settled offer: convoy at ~26-29% height
        public float OfferLookDrop = 7.8f;   // measured: train sat 90 px too high under the tray         // aim lower so the rear clears the tray edge   // rendered silhouette was 27% tall; 12m floor kept it far
        /// <summary>Gap kept between the last rendered wagon and the top of the bottom HUD, as a
        /// fraction of screen height. FitScale crops the *fight envelope* towards the lens; this
        /// keeps the *train itself* above the live XP bar at any convoy length.</summary>
        public float HudClearance = 0.014f;
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
            float dtU = Time.unscaledDeltaTime;
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

            // Rendered train box: every convoy renderer's own world bounds corners (loco +
            // wagons, rear coupler and gun barrels included). Per-renderer boxes stay tight on
            // curves, unlike one convoy-wide axis-aligned box, and they are the same surfaces
            // the silhouette check measures.
            if (_trainRenderers == null || _trainRenderers.Length == 0 || _trainWagonCount != wagons)
            {
                var all = Train.GetComponentsInChildren<Renderer>();
                _trainRenderers = System.Array.FindAll(all, r => r is MeshRenderer || r is SkinnedMeshRenderer);
                _trainWagonCount = wagons;
            }
            int m = 0;
            for (int i = 0; i < _trainRenderers.Length && m + 8 <= _trainPoints.Length; i++)
            {
                var rend = _trainRenderers[i];
                Bounds b = rend.localBounds;                 // mesh bounds in the model's own axes
                Matrix4x4 mt = rend.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    _trainPoints[m++] = mt.MultiplyPoint3x4(new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z));
                }
            }
            bool haveTrainBox = m > 0;

            float midS = (tail + nose) * 0.5f;
            Vector3 forward = TrackSpace.Facing(midS) * Vector3.forward;
            float lookH = LookHeight + (loop.World != null && loop.World.OfferPending ? OfferLookDrop : 0f);
            Vector3 target = hull.center + forward * LookAhead + Vector3.up * lookH;

            float aspect = Mathf.Max(0.5f, _cam.aspect);
            float fit = FitDistance(target, forward, aspect);
            float wantDistance = Mathf.Max(Distance, fit * FitScale);
            // FitScale deliberately crops the fight envelope towards the lens (the approved
            // framing). The rendered train is then re-checked against the live bottom HUD: at
            // five or six wagons that crop put the rear coupler behind the XP bar, so pull back
            // only as far as the train's own box needs. The settled picker keeps its verified
            // anchor and is skipped here — at its 1.86x distance the tail already clears.
            float hudBand = Mathf.Clamp(TrainSurvivor.Game.UI.HudView.BottomHudFraction + HudClearance, 0.06f, 0.4f);
            float fitTrain = haveTrainBox ? FitDistance(target, forward, aspect, _trainPoints, m, hudBand, false) : 0f;
            bool offer = w.OfferPending;
            if (!offer && fitTrain > wantDistance)
            {
                wantDistance = fitTrain;
                if (_blockLogAt <= 0f)
                {
                    _blockLogAt = 2f;
                    Debug.Log(string.Format(
                        "[cam] long-convoy safe fit {0:0.0} m (envelope fit {1:0.0} m x {2:0.00}, wagons {3}, HUD band {4:0.0}%)",
                        fitTrain, fit, FitScale, wagons, hudBand * 100f));
                }
            }
            // Settled upgrade offer: the tray covers the top 58% of the portrait, so use a
            // separate restrained anchor (further back, aim lower) to keep the locomotive,
            // turret and wagon visible in the strip below it. Blended with unscaled time so it
            // still eases while the game is paused.
            if (w.OfferPending) wantDistance *= OfferDistanceScale;
            if (!_placed)
            {
                _distance = wantDistance;
                _placed = true;
            }
            else
            {
                float zoomK = 1f - Mathf.Exp(-ZoomSmoothing * Mathf.Max(dt, dtU));
                _distance = Mathf.Lerp(_distance, wantDistance, zoomK);
            }

            float pitch = ElevationDeg * Mathf.Deg2Rad;
            Vector3 wantPos = target - forward * (Mathf.Cos(pitch) * _distance)
                                     + Vector3.up * (Mathf.Sin(pitch) * _distance);
            wantPos += TrackSpace.Right(midS) * Lateral;

            // Scenery occlusion: instead of accepting a shot from inside a building (which
            // renders a black frame for a long convoy whose fitted distance puts the lens
            // behind a warehouse), first climb over the obstruction, then — if that ray is
            // still blocked — pull in front of it. Framing is preserved whenever possible.
            Vector3 offset = wantPos - target;
            float offsetLen = offset.magnitude;
            if (offsetLen > 1f && SceneryHit(target, wantPos, out RaycastHit hit))
            {
                float raise = (hit.point.y + 2.5f) - wantPos.y;
                if (raise > 0f) wantPos.y += raise;

                if (SceneryHit(target, wantPos, out RaycastHit hit2))
                {
                    float pull = Mathf.Max(6f, hit2.distance - 0.5f);
                    wantPos = target + (wantPos - target).normalized * pull;
                }
                if (_blockLogAt <= 0f)
                {
                    _blockLogAt = 2f;
                    Debug.Log(string.Format("[cam] scenery occlusion at {0:0.0} m, raised {1:0.0} m -> distance {2:0.0} m",
                        hit.distance, raise, (wantPos - target).magnitude));
                }
            }
            _blockLogAt -= dt;

            Vector3 toAim = target - wantPos;
            Quaternion wantRot = toAim.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toAim, Vector3.up)
                : TrackSpace.Facing(midS);

            float smooth = 1f - Mathf.Exp(-Smoothing * dt);
            transform.position = Vector3.Lerp(transform.position, wantPos, smooth);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, smooth);
        }

        /// <summary>Scenery-only occlusion test, ignoring thin posts beside the train.</summary>
        bool SceneryHit(Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 dir = to - from;
            float d = dir.magnitude;
            if (d < 0.01f) { hit = default; return false; }
            return Physics.SphereCast(from, OcclusionRadius, dir / d, out hit, d,
                                      SceneryMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Smallest rear distance at which every convoy envelope sample fits inside
        /// the aspect-aware viewport margins. Monotonic in distance, so a short bisection is
        /// exact enough and allocation-free.</summary>
        float FitDistance(Vector3 target, Vector3 forward, float aspect)
        {
            return FitDistance(target, forward, aspect, _fitPoints, _fitPoints.Length, 0f, true);
        }

        /// <summary>Same solve over an arbitrary sample set. <paramref name="bottomBand"/> above
        /// zero replaces the symmetric lower limit with the real screen edge: the fraction of
        /// height the bottom HUD covers, so the sample box can never end up behind it.</summary>
        float FitDistance(Vector3 target, Vector3 forward, float aspect, Vector3[] pts, int count, float bottomBand, bool checkWidth)
        {
            float vHalf = _cam.fieldOfView * Mathf.Deg2Rad * 0.5f;
            float tanBase = Mathf.Tan(vHalf);
            float tanV = tanBase * FitMarginY;
            float tanH = tanBase * aspect * FitMarginX;
            float downV = bottomBand > 0f ? tanBase * Mathf.Clamp01(1f - 2f * bottomBand) : tanV;
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
                for (int i = 0; i < count; i++)
                {
                    Vector3 v = pts[i] - pos;
                    float z = Vector3.Dot(v, dir);
                    if (z < 1f) { ok = false; break; }
                    float upDot = Vector3.Dot(v, up);
                    // Width is owned by the envelope fit; a rotated wagon's axis-aligned box
                    // is far wider than the wagon, and testing it here would zoom the whole
                    // shot out for no visual gain.
                    if ((checkWidth && Mathf.Abs(Vector3.Dot(v, right)) > z * tanH) ||
                        upDot > z * tanV || upDot < -z * downV)
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

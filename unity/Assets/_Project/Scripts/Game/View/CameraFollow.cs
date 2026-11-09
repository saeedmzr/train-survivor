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
        // Framing tuned on device for the v2 roster: the horde has to be readable at the
        // distance it is first engaged (~25-35 m), which the old 16.5/11 chase shot was not.
        // pulled back and raised for the modelled 4.2 m locomotive: the old 15.5/10.4 framed
        // the low box train, but with the real loco the cab filled half the screen
        public float Distance = 20f;
        public float Height = 16.5f;

        /// <summary>Extra chase distance per wagon beyond the engine, so a long convoy still
        /// fits in frame. The camera eases toward it instead of snapping.</summary>
        public float WagonPushback = 2.2f;
        public float ZoomSmoothing = 2.2f;
        // the chase shot used to stare 25 m up the track; 10 keeps the fight (and the cars
        // beside the rails) in frame instead of empty road
        public float LookAhead = 10f;     // used only to derive the pitch
        public float LookHeight = 1.3f;
        public float Lateral = 0f;
        public float Smoothing = 9f;

        Camera _cam;
        float _distance;

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
            cam.fieldOfView = DevTuning.Get("cam.fov", cam.fieldOfView);

            _distance = Distance;
        }

        void LateUpdate()
        {
            if (Train == null || _cam == null) return;

            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;

            float s = loop.World.Train.S;

            // A bigger train needs more room: every wagon past the engine pushes the camera
            // back, which also lowers the horizon so the convoy still reads as one object.
            int wagons = loop.World.Train.Wagons;
            float wantDistance = Distance + Mathf.Max(0, wagons - 1) * WagonPushback;
            float zoomK = 1f - Mathf.Exp(-ZoomSmoothing * Time.deltaTime);
            _distance = Mathf.Lerp(_distance, wantDistance, zoomK);

            float height = Height * (_distance / Distance);
            Vector3 wantPos = TrackSpace.Point(s - _distance, Lateral, height);

            // Aim at the train, not at a point far up the road: on a curve a fixed track
            // heading leaves the convoy hanging off one edge of the frame. A short look-ahead
            // past the nose keeps the road visible without losing the train.
            Quaternion facing = TrackSpace.Facing(s);
            Vector3 forward = facing * Vector3.forward;
            Vector3 aim = Train.LeadPosition
                          + forward * (2f + LookAhead * 0.25f)
                          + Vector3.up * LookHeight;
            Vector3 toAim = aim - wantPos;
            Quaternion wantRot = toAim.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toAim, Vector3.up)
                : facing;

            float k = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, wantPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, k);
        }
    }
}

using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Procedural grounded stride for the Walker's lower body, layered on top of the imported
    /// shamble. The clip keeps driving hips/spine/arms/head; this replaces only the two legs
    /// with a two-bone IK solve against world-space foot anchors, so the support foot is
    /// genuinely stationary for the whole contact phase instead of dragging at body speed.
    ///
    /// Anchors live in world space and are released on lift-off, on over-extension (speed
    /// change) and on a hard turn; the whole solve is blended in/out so stop, attack, death
    /// and pool reuse never pop. Only bones below the pelvis are touched.
    /// </summary>
    public sealed class WalkerStride
    {
        // --- tuning (metres / seconds, in world scale after the model fit) ---
        public float Stride = 0.72f;          // ground covered per step
        public float ContactFraction = 0.58f; // share of a foot's cycle spent planted
        public float Lift = 0.16f;            // swing foot clearance
        public float BlendRate = 7f;          // weight change per second
        public float HipDrop = 0.12f;          // pelvis lowered to give the legs IK slack

        public float AnkleHeight;             // foot bone height above ground at bind
        public float LegLength;               // thigh + shin
        public float Thigh, Shin;
        public float FootHalfWidth = 0.26f;

        public bool Ready { get; private set; }
        public float Weight { get; private set; }

        // --- telemetry for validation ---
        public float LastAnchorDistance;
        public int ReleasedByReach;
        public int Contacts;
        public bool LeftContact, RightContact;
        public Vector3 AnchorL => _anchor[0];
        public Vector3 AnchorR => _anchor[1];

        Transform _root;
        Transform _hips, _upLegL, _legL, _footL, _upLegR, _legR, _footR;
        Quaternion _footBindLocalRotL, _footBindLocalRotR;

        float _phase;
        float _yaw;
        bool _seeded;
        readonly bool[] _released = new bool[2];

        readonly Vector3[] _anchor = new Vector3[2];
        readonly Vector3[] _swingFrom = new Vector3[2];
        readonly Vector3[] _swingTo = new Vector3[2];
        readonly bool[] _contact = new bool[2];
        readonly bool[] _wasContact = new bool[2];

        public bool Bind(Transform model, Transform hips, Transform upLegL, Transform legL, Transform footL,
                         Transform upLegR, Transform legR, Transform footR)
        {
            _root = model;
            _hips = hips;
            _upLegL = upLegL; _legL = legL; _footL = footL;
            _upLegR = upLegR; _legR = legR; _footR = footR;
            if (_upLegL == null || _legL == null || _footL == null ||
                _upLegR == null || _legR == null || _footR == null) return false;

            Thigh = Vector3.Distance(_legL.position, _upLegL.position);
            Shin = Vector3.Distance(_footL.position, _legL.position);
            LegLength = Thigh + Shin;
            AnkleHeight = _footL.position.y - _root.position.y;
            // stride and clearance follow the fitted skeleton, not a hard-coded size
            Stride = Mathf.Clamp(LegLength * 0.425f, 0.20f, 1.40f);
            Lift = Mathf.Clamp(LegLength * 0.094f, 0.04f, 0.35f);

            // Bind orientation of each foot in the root's frame: reused every frame so the
            // sole stays flat regardless of what the shin solve does.
            _footBindLocalRotL = Quaternion.Inverse(_root.rotation) * _footL.rotation;
            _footBindLocalRotR = Quaternion.Inverse(_root.rotation) * _footR.rotation;
            FootHalfWidth = Mathf.Abs(_root.InverseTransformPoint(_footL.position).x -
                                      _root.InverseTransformPoint(_footR.position).x) * 0.5f;
            Ready = true;
            return true;
        }

        public void Reset()
        {
            _phase = 0f;
            _seeded = false;
            _released[0] = _released[1] = false;
            Weight = 0f;
            Contacts = 0;
            ReleasedByReach = 0;
            for (int i = 0; i < 2; i++)
            {
                _contact[i] = _wasContact[i] = false;
                _anchor[i] = _swingFrom[i] = _swingTo[i] = Vector3.zero;
            }
        }

        /// <summary>
        /// Steps the gait and applies the leg solve. Call in LateUpdate, after the Animation
        /// component has written this frame's pose. When <paramref name="walking"/> is false
        /// the weight blends out and the clip pose is left untouched.
        /// </summary>
        public void Step(float dt, float speed, Vector3 rootPos, Quaternion rootRot, bool walking)
        {
            if (!Ready || dt <= 0f) return;

            float target = walking ? 1f : 0f;
            Weight = Mathf.MoveTowards(Weight, target, BlendRate * dt);
            if (Weight <= 0f) { _seeded = false; return; }

            // hard turn: pivot the anchors with the body instead of twisting the legs out of
            // their sockets, then restart the cycle from the current stance
            float yaw = rootRot.eulerAngles.y;
            float dYaw = Mathf.DeltaAngle(_yaw, yaw);
            _yaw = yaw;
            if (_seeded && Mathf.Abs(dYaw) > 2.0f)
            {
                Quaternion pivot = Quaternion.Euler(0f, dYaw, 0f);
                for (int i = 0; i < 2; i++)
                {
                    _anchor[i] = rootPos + pivot * (_anchor[i] - rootPos);
                    _swingFrom[i] = rootPos + pivot * (_swingFrom[i] - rootPos);
                    _swingTo[i] = rootPos + pivot * (_swingTo[i] - rootPos);
                }
            }

            if (!_seeded)
            {
                Seed(rootPos, rootRot);
                _seeded = true;
            }

            float stepRate = Mathf.Max(0.15f, speed / Mathf.Max(0.2f, Stride)); // steps per second
            _phase = Mathf.Repeat(_phase + stepRate * dt * 0.5f, 1f);          // 2 steps per cycle

            // The rest pose is nearly straight-legged (hip 1.66 m above the ankle against a
            // 1.695 m leg), so there is no slack to re-target a foot. The pelvis is lowered by
            // exactly what each stance leg needs to stay inside 94% extension — a walk's
            // natural crouch, not a fixed offset — and only while the gait has weight.
            Vector3 fwd = rootRot * Vector3.forward;
            Vector3 right = rootRot * Vector3.right;

            float drop = HipDrop;
            for (int i = 0; i < 2; i++)
            {
                if (!_contact[i]) continue;
                Vector3 hip = _hip(i);
                float dx = hip.x - _anchor[i].x;
                float dz = hip.z - _anchor[i].z;
                float reach = LegLength * 0.94f;
                float maxV = Mathf.Sqrt(Mathf.Max(0f, reach * reach - (dx * dx + dz * dz)));
                drop = Mathf.Max(drop, hip.y - _anchor[i].y - maxV);
            }
            drop = Mathf.Clamp(drop, 0f, 0.40f) * Weight;
            if (_hips != null) _hips.position += Vector3.down * drop;

            // still over-extended (drop clamped, or a fast speed change): let the foot go
            // early rather than stretch the leg — the release is a swing, never a teleport
            for (int i = 0; i < 2; i++)
            {
                if (!_contact[i]) continue;
                float reach = Vector3.Distance(_hip(i), _anchor[i]);
                LastAnchorDistance = reach;
                if (reach <= LegLength * 0.985f) continue;
                _contact[i] = false;
                _wasContact[i] = false;
                _released[i] = true;
                ReleasedByReach++;
                LiftOff(i, rootPos, fwd, right);
            }

            for (int i = 0; i < 2; i++)
            {
                float p = Mathf.Repeat(_phase + (i == 0 ? 0f : 0.5f), 1f);
                bool inContact = p < ContactFraction && !_released[i];
                if (p >= ContactFraction) _released[i] = false;
                _wasContact[i] = _contact[i];
                _contact[i] = inContact;

                if (inContact)
                {
                    if (!_wasContact[i]) Touchdown(i, rootPos, fwd, right);
                }
                else
                {
                    if (_wasContact[i]) LiftOff(i, rootPos, fwd, right);
                }
            }

            LeftContact = _contact[0];
            RightContact = _contact[1];

            Vector3 targetL = FootTarget(0, rootPos, fwd, right);
            Vector3 targetR = FootTarget(1, rootPos, fwd, right);

            Apply(0, targetL);
            Apply(1, targetR);
        }

        void Seed(Vector3 rootPos, Quaternion rootRot)
        {
            Vector3 fwd = rootRot * Vector3.forward;
            Vector3 right = rootRot * Vector3.right;
            _yaw = rootRot.eulerAngles.y;
            for (int i = 0; i < 2; i++)
            {
                _anchor[i] = GroundPos(i, rootPos, fwd, right, i == 0 ? 0f : Stride * 0.5f);
                _swingFrom[i] = _swingTo[i] = _anchor[i];
                _contact[i] = i == 0;
                _wasContact[i] = _contact[i];
            }
        }

        void Touchdown(int i, Vector3 rootPos, Vector3 fwd, Vector3 right)
        {
            if (_swingTo[i] == Vector3.zero)
                _swingTo[i] = GroundPos(i, rootPos, fwd, right, Stride * 0.5f);
            _anchor[i] = _swingTo[i];
            Contacts++;
        }

        void LiftOff(int i, Vector3 rootPos, Vector3 fwd, Vector3 right)
        {
            _swingFrom[i] = _anchor[i];
            _swingTo[i] = GroundPos(i, rootPos, fwd, right, Stride * 0.5f);
        }

        Vector3 FootTarget(int i, Vector3 rootPos, Vector3 fwd, Vector3 right)
        {
            if (_contact[i]) return _anchor[i];

            float shift = i == 0 ? 0f : 0.5f;
            float p = Mathf.Repeat(_phase + shift, 1f);
            float s = Mathf.Clamp01((p - ContactFraction) / Mathf.Max(0.001f, 1f - ContactFraction));
            float e = s * s * (3f - 2f * s);
            Vector3 v = Vector3.Lerp(_swingFrom[i], _swingTo[i], e);
            v.y += Mathf.Sin(s * Mathf.PI) * Lift;
            return v;
        }

        /// <summary>Foot anchor relative to that leg's own hip, so the IK never starts a
        /// stance already at full extension (the bind pose leans the hips 0.36 m back).</summary>
        Vector3 GroundPos(int i, Vector3 rootPos, Vector3 fwd, Vector3 right, float ahead)
        {
            Vector3 hip = _hip(i);
            Vector3 p = new Vector3(hip.x, rootPos.y + AnkleHeight, hip.z)
                      + fwd * ahead
                      + right * ((i == 0 ? -1f : 1f) * FootHalfWidth * 0.55f);
            return p;
        }

        Vector3 _hip(int i) => (i == 0 ? _upLegL : _upLegR).position;

        void Apply(int i, Vector3 ankleTarget)
        {
            Transform upLeg = i == 0 ? _upLegL : _upLegR;
            Transform leg = i == 0 ? _legL : _legR;
            Transform foot = i == 0 ? _footL : _footR;

            // capture the clip's pose, solve at full strength, then blend so the change can
            // never pop when the gait engages or releases
            Quaternion animUp = upLeg.localRotation;
            Quaternion animLeg = leg.localRotation;
            Quaternion animFoot = foot.localRotation;

            Vector3 hip = upLeg.position;
            Vector3 toTarget = ankleTarget - hip;
            float want = toTarget.magnitude;
            float minReach = Mathf.Abs(Thigh - Shin) + 0.02f;
            float maxReach = Thigh + Shin - 0.01f;
            float dist = Mathf.Clamp(want, minReach, maxReach);
            Vector3 dir = want > 1e-4f ? toTarget / want : Vector3.down;

            // knee bends forward along the body's facing, offset slightly outward
            Vector3 fwd = _root.forward;
            Vector3 perp = Vector3.ProjectOnPlane(fwd, dir);
            if (perp.sqrMagnitude < 1e-4f) perp = Vector3.ProjectOnPlane(_root.right * (i == 0 ? 1f : -1f), dir);
            if (perp.sqrMagnitude < 1e-4f) perp = Vector3.forward;
            perp.Normalize();

            float a = Thigh, b = Shin, c = dist;
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            Vector3 knee = hip + (dir * cosA + perp * sinA) * a;

            // aim thigh at the knee, then shin at the ankle (axis-agnostic delta rotations)
            Aim(upLeg, leg, knee);
            Aim(leg, foot, hip + dir * c);

            // keep the sole level with the body's ground plane
            Quaternion bind = i == 0 ? _footBindLocalRotL : _footBindLocalRotR;
            foot.rotation = _root.rotation * bind;
            // but respect the clip's roll on the planted foot? keep the flat solve: it is the
            // whole point of the correction and matches a boot sole on ballast.

            Quaternion ikUp = upLeg.localRotation;
            Quaternion ikLeg = leg.localRotation;
            Quaternion ikFoot = foot.localRotation;

            float w = Weight;
            upLeg.localRotation = Quaternion.Slerp(animUp, ikUp, w);
            leg.localRotation = Quaternion.Slerp(animLeg, ikLeg, w);
            foot.localRotation = Quaternion.Slerp(animFoot, ikFoot, w);
        }

        static void Aim(Transform bone, Transform child, Vector3 target)
        {
            Vector3 cur = child.position - bone.position;
            Vector3 want = target - bone.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;
using TrainSurvivor.Game.Audio;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Draws walker enemies with the finalized 3D model (Resources/Models/walker3d): one pooled
    /// instance per live walker, legacy clips Walk / Attack / Death. Death is triggered by the sim's
    /// Kill event — the same channel Fx and audio listen to — so it fires exactly when a walker dies.
    /// </summary>
    public sealed class WalkerModelView : MonoBehaviour
    {
        public static WalkerModelView Instance { get; private set; }

        const string ModelResource = "Models/walker3d";
        const string MaterialResource = "Models/WalkerMat";
        const float TargetHeight = 4.0f;      // twice the source height, as asked
        const int MaxGlowLights = 4;          // real lights on heads: mobile budget
        static Material _glowMat;
        const int PoolSize = 34;      // 30 live walkers can be on the field at once
        const int CorpseReserve = 4;  // bodies kept back so a death always has one to play on
        const float AttackRange = 5.5f;    // SimConfig.ContactRange
        // Measured on the imported Walk clip by Assets/Editor/WalkerGaitProbe: the stance foot's
        // backward integral is 0.93 m/s of body travel at the 3.598x fit scale, i.e. ~0.93 m
        // per footfall at playback 1.0. Expressed per unit of fit scale so the gait follows
        // the measured model, not a note from Blender.
        const float WalkGroundSpeedUnit = 0.2585f;
        // measured ground distance per footfall; the gait already supplies the speed
        const float FootstepStride = 0.93f;
        // the swing cue fires a third of the way into the attack clip, not on the trigger
        const float AttackCueFraction = 0.3f;
        // beyond this the cue is inaudible-by-design (volume shaping only, never 3D fade).
        // 40 m matches the gun range: walkers are usually killed during the approach, so a
        // 22 m cue radius left footsteps and swings silent in normal play.
        const float CueRange = 40f;

        [Tooltip("Yaw offset on top of facing the train, to correct the model's authored forward axis")]
        public float facingYaw = 0f;     // authored rig forward matches +Z; 180 showed its back

        [Tooltip("Scale multiplier on top of the automatic 2 m fit")]
        public float scaleBias = 1f;

        GameObject _prefab;
        Material _mat;
        AnimationClip _walk, _attack, _death;
        float _walkLen = 1.2f, _attackLen = 1.2f, _deathLen = 2.5f;
        float _walkGroundSpeed = 2.7f;
        float _fitScale = 1f;

        struct Death { public float S, X; }
        readonly List<Death> _pending = new List<Death>();

        sealed class Body
        {
            public GameObject Go;
            public Animation Anim;
            public int EnemyId = -1;
            public float FreeAt;
            public string Playing;
            public Vector3 LastPos;
            public float LastT;
            public float Gait;          // measured ground speed, m/s
            public Transform Root;      // prefab root child; guards against clip root motion
            public Vector3 RootLocal;
            public HitProxy Proxy;      // ballistics target, moved with the body
            public float StepAccum;     // metres walked since the last footstep cue
            public float SwingAt;       // time the current attack clip reaches its swing
            public Light GlowLight;     // head lantern, enabled only for the nearest bodies
            public Transform FootL, FootR;   // measured foot contacts for the gait lock
            public bool LockOn;
            public bool LockLeft;
            public Vector3 LockPoint;        // world XZ the planted foot must hold
            public Vector3 RootOffset;       // visual-only correction, world metres
            public Vector3 LastFoot;         // previous frame's planted-foot world position
            public Vector3 LastL, LastR;     // previous frame's both feet, for stance picking
            public bool FeetPrimed;
            public float DriftSpeedSum;      // cm/s of planted-foot slide, summed per frame
            public float DriftAfterSum;      // residual after the correction, same units
            public float DriftMax;           // worst single-frame slide, cm/s
            public int DriftSamples;
            public float DriftWindowStart;
            public WalkerStride Stride;      // procedural grounded stride (legs only)
            public int FootstepContacts;     // stride contacts already cued
            public bool Claimed;             // assigned to a live enemy this frame
        }

        readonly List<Body> _pool = new List<Body>();
        readonly HashSet<int> _liveIds = new HashSet<int>();
        int _poolCursor;
        readonly List<AnimationClip> _clips = new List<AnimationClip>();
        int _diag;
        float _diagAt;
        Camera _walkerCam;
        readonly List<Camera> _hiddenCams = new List<Camera>();
        bool _rootLogged;
        bool _verbose;
        bool _footLock;   // opt-in: dev.footlock=1 (see handoff: contact lock not converged)
        bool _strideOn;   // dev.stride=0 disables the procedural grounded stride
        bool _footLog;
        const float MaxRootOffset = 0.30f;

        public bool Active => _prefab != null && _walk != null;

        void Awake()
        {
            Instance = this;
            _verbose = TrainSurvivor.Game.DevTuning.Get("dev.verbose", 0f) > 0.5f;
            _footLock = TrainSurvivor.Game.DevTuning.Get("dev.footlock", 0f) > 0.5f;
            _strideOn = TrainSurvivor.Game.DevTuning.Get("dev.stride", 1f) > 0.5f;
            _footLog = TrainSurvivor.Game.DevTuning.Get("dev.footlog", 0f) > 0.5f || _verbose;
            _prefab = Resources.Load<GameObject>(ModelResource);
            if (_prefab == null)
            {
                Debug.LogWarning("[walker3d] Resources/" + ModelResource + " not found — sprites stay");
                return;
            }

            foreach (var c in Resources.LoadAll<AnimationClip>("Models"))
            {
                if (c.name == "Walk") _walk = c;
                else if (c.name == "Attack") _attack = c;
                else if (c.name == "Death") _death = c;
                _clips.Add(c);
            }
            _walkLen = _walk != null ? Mathf.Max(0.3f, _walk.length) : 1.2f;
            _attackLen = _attack != null ? Mathf.Max(0.3f, _attack.length) : 1.2f;
            _deathLen = _death != null ? Mathf.Max(0.5f, _death.length) : 2.5f;
            _mat = Resources.Load<Material>(MaterialResource);
            // dev.face flips the model forward axis without a rebuild
            facingYaw = TrainSurvivor.Game.DevTuning.Get("dev.face", facingYaw);

            var probe = Instantiate(_prefab, transform);
            probe.name = "Walker3D_probe";
            _fitScale = ComputeFit(probe);
            Destroy(probe);

            // The clip's foot travel scales with the model: measure once, then derive.
            _walkGroundSpeed = WalkGroundSpeedUnit * _fitScale;

            Debug.Log(string.Format(
                "[walker3d] ready: walk {0:0.00}s attack {1:0.00}s death {2:0.00}s fit {3:0.00} " +
                "clipSpeed {4:0.000}m/s footlock {5} clips {6}",
                _walkLen, _attackLen, _deathLen, _fitScale, _walkGroundSpeed, _footLock, _clips.Count));

            for (int i = 0; i < PoolSize; i++) _pool.Add(Spawn(i));

            if (TrainSurvivor.Game.DevTuning.Get("dev.walkercam", 0f) > 0.5f)
            {
                var camGo = new GameObject("WalkerInspectCam");
                _walkerCam = camGo.AddComponent<Camera>();
                _walkerCam.depth = 90f;
                _walkerCam.fieldOfView = 38f;
                _walkerCam.nearClipPlane = 0.1f;
                _walkerCam.clearFlags = CameraClearFlags.Skybox;
                foreach (var c in Camera.allCameras)
                {
                    if (c == _walkerCam) continue;
                    _hiddenCams.Add(c);
                    c.enabled = false;
                }
                if (_verbose) Debug.Log("[walker3d] inspection camera on");
            }
        }

        void OnEnable()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop != null) loop.SimEventRaised += OnSimEvent;
        }

        void OnDisable()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop != null) loop.SimEventRaised -= OnSimEvent;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _hiddenCams.Count; i++)
                if (_hiddenCams[i] != null) _hiddenCams[i].enabled = true;
        }

        void OnSimEvent(SimEvent ev)
        {
            if (ev.Kind != SimEventKind.Kill || ev.EnemyKind != EnemyKind.Walker) return;
            _pending.Add(new Death { S = ev.ToS, X = ev.ToX });
            if (_diag < 5)
            {
                _diag++;
                Debug.Log(string.Format("[walker3d] death clip at S {0:0.0} X {1:0.0} (queued {2})",
                    ev.ToS, ev.ToX, _pending.Count));
            }
        }

        float ComputeFit(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float h = b.size.y;
            return h > 0.01f ? TargetHeight / h : 1f;
        }

        static Material GlowMaterial()
        {
            if (_glowMat != null) return _glowMat;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            _glowMat = new Material(shader) { name = "WalkerHeadGlow" };
            _glowMat.SetColor("_BaseColor", new Color(0.30f, 1f, 0.38f));
            _glowMat.SetColor("_EmissionColor", new Color(0.45f, 2.2f, 0.55f));
            _glowMat.EnableKeyword("_EMISSION");
            _glowMat.SetFloat("_Smoothness", 0.2f);
            return _glowMat;
        }

        Body Spawn(int index)
        {
            var go = Instantiate(_prefab, transform);
            go.name = "Walker3D_" + index;
            go.transform.localScale = Vector3.one * (_fitScale * scaleBias);

            if (_mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = _mat;

            var anim = go.GetComponent<Animation>();
            if (anim == null) anim = go.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var c in _clips) anim.AddClip(c, c.name);

            float worldPerUnit = _fitScale * scaleBias;
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(glow.GetComponent<Collider>());
            glow.name = "HeadGlow";
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(
                0f, (TargetHeight * 0.92f) / worldPerUnit, 0.05f / worldPerUnit);
            glow.transform.localScale = Vector3.one * (0.20f / worldPerUnit);
            glow.GetComponent<Renderer>().sharedMaterial = GlowMaterial();

            var glowLight = glow.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = new Color(0.55f, 1f, 0.45f);
            glowLight.range = 4.5f;
            glowLight.intensity = 1.0f;
            glowLight.shadows = LightShadows.None;
            glowLight.enabled = false;

            Transform root = go.transform.childCount > 0 ? go.transform.GetChild(0) : null;
            // Ballistics proxy lives outside the scaled/rotated model: the collider is authored
            // in world metres and only follows the body's position.
            var proxy = HitProxy.CreateCapsule(transform, "WalkerHit" + index, 0.55f, 2.4f);
            var body = new Body
            {
                Go = go, Anim = anim, Root = root, Proxy = proxy,
                RootLocal = root != null ? root.localPosition : Vector3.zero,
                GlowLight = glowLight,
                FootL = FindBone(go.transform, "LeftFoot"),
                FootR = FindBone(go.transform, "RightFoot")
            };

            // Two-bone leg solve on the real skeleton; bound once per pooled body.
            var hips = FindBone(go.transform, "Hips");
            var upL = FindBone(go.transform, "LeftUpLeg");
            var legL = FindBone(go.transform, "LeftLeg");
            var upR = FindBone(go.transform, "RightUpLeg");
            var legR = FindBone(go.transform, "RightLeg");
            if (hips != null && upL != null && legL != null && upR != null && legR != null &&
                body.FootL != null && body.FootR != null)
            {
                body.Stride = new WalkerStride();
                body.Stride.Bind(root != null ? root : go.transform, hips, upL, legL, body.FootL, upR, legR, body.FootR);
            }
            go.SetActive(false);
            return body;
        }

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.EndsWith(":" + name)) return t;
            return null;
        }

        void Play(Body b, AnimationClip clip, bool loop, float speed = 1f)
        {
            if (clip == null || b.Anim == null) return;
            b.Playing = clip.name;
            var state = b.Anim[clip.name];
            if (state != null) state.speed = speed;
            b.Anim.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            b.Anim.Play(clip.name);
            b.Go.SetActive(true);
        }

        /// <summary>Nearest point on the actual convoy hull (nose to tail), not the loco centre.</summary>
        Quaternion FaceConvoy(Vector3 from, float ownS, float trainS, int wagons)
        {
            float nose = trainS + SimConfig.TrainNose;
            float tail = trainS - SimConfig.WagonSpan * Mathf.Max(1, wagons);
            float cs = Mathf.Clamp(ownS, tail, nose);
            Vector3 target = TrackSpace.Point(cs, 0f, 0f);
            Vector3 dir = target - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.Euler(0f, facingYaw, 0f);
            return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, facingYaw, 0f);
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World);
        }

        /// <summary>
        /// Planted-foot lock (OPT-IN, off by default).
        ///
        /// Offline validation on the real Walk clip (Assets/Editor/WalkerGaitValidation) shows
        /// the bounded positional correction does NOT fix this asset: at 2.4 m/s the stance
        /// foot's world skate is 174 cm/s mean / 285 p95 with no correction, and 176 / 474 with
        /// the correction, while the visual root slews 6.7 cm per frame and the offset pins at
        /// its clamp. The clip is a shuffle whose contact foot moves at ~0.6 m/s against a
        /// 2.4 m/s body, so a position offset either saturates or pops. Travel-per-cycle is
        /// instead matched by the measured clip speed (0.93 m/s at playback 1) in Sync().
        /// Enable with dev.footlock=1 only when re-validating a new clip.
        /// </summary>
        void UpdateFootLock(Body b, float now, float dt, bool walking)
        {
            if (b.FootL == null || b.FootR == null) return;

            if (!walking || !_footLock || dt <= 0.0001f)
            {
                b.LockOn = false;
                b.RootOffset = Vector3.MoveTowards(b.RootOffset, Vector3.zero, 1.8f * dt);
                return;
            }

            Vector3 l = b.FootL.position;
            Vector3 r = b.FootR.position;

            // Pick the stance foot by horizontal motion, not by height: this shamble keeps
            // both feet low, and the lower one can still be the foot swinging forward. The
            // planted foot is the one travelling backward along the body's forward axis.
            Vector3 fwd = b.Go.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();

            if (!b.FeetPrimed)
            {
                b.LastL = l;
                b.LastR = r;
                b.FeetPrimed = true;
                b.LockOn = false;
                return;
            }

            float dL = Vector3.Dot(l - b.LastL, fwd);
            float dR = Vector3.Dot(r - b.LastR, fwd);
            b.LastL = l;
            b.LastR = r;

            bool left;
            const float margin = 0.015f;   // metres per frame; ignore sub-millimetre jitter
            if (b.LockOn)
                left = b.LockLeft ? (dL <= dR + margin) : (dR <= dL + margin);
            else
                left = dL <= dR;

            Vector3 foot = left ? l : r;

            if (!b.LockOn || b.LockLeft != left)
            {
                // Touch-down: this is the ground point the foot has to keep.
                b.LockOn = true;
                b.LockLeft = left;
                b.LockPoint = new Vector3(foot.x, 0f, foot.z);
                b.LastFoot = foot;
                return;
            }

            Vector3 delta = foot - b.LastFoot;
            delta.y = 0f;
            b.LastFoot = foot;

            Vector3 err = new Vector3(foot.x - b.LockPoint.x, 0f, foot.z - b.LockPoint.z);

            // Telemetry: planted-foot slide per second before the correction, and the
            // accumulated world-space error (the number that matters for visible skating).
            float slide = delta.magnitude / dt * 100f;                 // cm/s
            float held = err.magnitude * 100f;                          // cm from touch-down point
            b.DriftSpeedSum += slide;
            b.DriftAfterSum += held;
            b.DriftMax = Mathf.Max(b.DriftMax, held);
            b.DriftSamples++;

            Vector3 fix = Vector3.ClampMagnitude(-err, 0.08f);
            b.RootOffset = Vector3.ClampMagnitude(b.RootOffset + fix, MaxRootOffset);
        }

        /// <summary>Periodic proof line: gait, playback, lock state and planted-foot error.</summary>
        void LogFootLock(Body b, float now, bool walking, bool attacking)
        {
            float n = Mathf.Max(1, b.DriftSamples);
            Debug.Log(string.Format(
                "[walker3d] gait foot={0} walking={1} attacking={2} lock={3} slide {4:0.0}cm/s heldErr {5:0.0}cm " +
                "worst {6:0.0}cm offset {7:0.0}cm gait {8:0.00}m/s playback {9:0.00} samples {10}",
                b.LockLeft ? "L" : "R", walking, attacking, b.LockOn,
                b.DriftSamples > 0 ? b.DriftSpeedSum / n : 0f,
                b.DriftSamples > 0 ? b.DriftAfterSum / n : 0f, b.DriftMax,
                b.RootOffset.magnitude * 100f, b.Gait,
                b.Anim != null && b.Anim[_walk.name] != null ? b.Anim[_walk.name].speed : 0f,
                b.DriftSamples));
            b.DriftSpeedSum = 0f;
            b.DriftAfterSum = 0f;
            b.DriftMax = 0f;
            b.DriftSamples = 0;
            b.DriftWindowStart = now;
        }

        public void Sync(World world)
        {
            if (!Active || world == null) return;

            float now = Time.time;
            float trainS = world.Train.S;
            int used = 0;

            // Stable body <-> enemy binding. The old "first N live walkers" indexing
            // reshuffled every mesh whenever one died, which teleported the visual bodies,
            // restarted their walk clips and poisoned the measured gait (it spiked to
            // 4 m/s, pinning playback at its clamp and driving the foot lock into its
            // limit). Now a body keeps its enemy until that enemy dies.
            _liveIds.Clear();
            int liveWalkers = 0;
            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive || e.Kind != EnemyKind.Walker) continue;
                _liveIds.Add(e.Id);
                liveWalkers++;
            }
            // Head lanterns: every walker keeps the emissive glow, but only the few nearest
            // to the camera get a real point light so the count stays mobile-safe.
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i].GlowLight != null) _pool[i].GlowLight.enabled = false;

            Vector3 eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            for (int slot = 0; slot < MaxGlowLights; slot++)
            {
                Body best = null;
                float bestSqr = float.MaxValue;
                for (int i = 0; i < _pool.Count; i++)
                {
                    Body cand = _pool[i];
                    if (cand.GlowLight == null || cand.EnemyId < 0 || cand.Go == null) continue;
                    if (!cand.Go.activeSelf || cand.GlowLight.enabled) continue;
                    float d = (cand.Go.transform.position - eye).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = cand; }
                }
                if (best == null) break;
                best.GlowLight.enabled = true;
            }

            for (int i = 0; i < _pool.Count; i++)
            {
                Body b = _pool[i];
                b.Claimed = false;
                if (b.EnemyId >= 0 && !_liveIds.Contains(b.EnemyId)) b.EnemyId = -1;
            }

            if (_diag < 3 && now > _diagAt)
            {
                _diag++;
                _diagAt = now + 2f;
                int alive = 0;
                for (int i = 0; i < world.EnemyCount; i++) if (world.Enemies[i].Alive) alive++;
                Vector3 p0 = alive > 0 ? TrackSpace.Point(world.Enemies[0].S, world.Enemies[0].X, 0f) : Vector3.zero;
                Debug.Log(string.Format("[walker3d] sync alive {0} (walkers {1}) pool {2} firstPos {3} camS {4:0.0}",
                    alive, liveWalkers, _pool.Count, p0, trainS));
            }

            Body focus = null;
            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive || e.Kind != EnemyKind.Walker) continue;
                if (used >= _pool.Count - CorpseReserve) break;

                Body body = FindBoundBody(e.Id);
                if (body == null)
                {
                    body = TakeFreeBody(now);
                    if (body == null) break;
                }
                body.Claimed = true;
                used++;
                if (focus == null) focus = body;

                Vector3 pos = TrackSpace.Point(e.S, e.X, 0f);
                body.Go.transform.position = pos;
                body.Go.transform.rotation = FaceConvoy(pos, e.S, trainS, world.Train.Wagons);

                if (body.Proxy != null)
                {
                    body.Proxy.Bind(e.Id, false);
                    body.Proxy.Move(pos + Vector3.up * 1.2f);
                }

                if (body.EnemyId != e.Id)
                {
                    body.EnemyId = e.Id;
                    body.FreeAt = 0f;
                    body.LastPos = pos;
                    body.LastT = now;
                    body.Gait = e.Speed;
                    body.StepAccum = 0f;
                    body.SwingAt = 0f;
                    body.LockOn = false;
                    body.RootOffset = Vector3.zero;
                    body.FeetPrimed = false;
                    body.DriftSpeedSum = 0f;
                    body.DriftAfterSum = 0f;
                    body.DriftMax = 0f;
                    body.DriftSamples = 0;
                    body.DriftWindowStart = Time.unscaledTime;
                    if (body.Stride != null) { body.Stride.Reset(); body.FootstepContacts = 0; }
                    Play(body, _walk, true);
                }

                // Gait from measured world displacement, so contact/slow cases do not moonwalk.
                float dt = now - body.LastT;
                if (dt > 0.0001f)
                {
                    float inst = Vector3.Distance(pos, body.LastPos) / dt;
                    body.Gait = Mathf.Lerp(body.Gait, inst, 0.35f);
                }
                body.LastPos = pos;
                body.LastT = now;

                // Distance to the nearest convoy hull point, not to the locomotive centre: the
                // sim bites at the hull (wagons included), so a rear/side attacker must read as
                // attacking here too or its attack clip and swing cue never start.
                float hullTail = trainS - SimConfig.WagonSpan * Mathf.Max(1, world.Train.Wagons);
                float hullNose = trainS + SimConfig.TrainNose;
                float hullClampS = Mathf.Clamp(e.S, hullTail, hullNose);
                float alongHull = e.S - hullClampS;
                float lateralHull = Mathf.Max(0f, Mathf.Abs(e.X) - SimConfig.TrainHalfWidth);
                float hullGap = Mathf.Sqrt(alongHull * alongHull + lateralHull * lateralHull);
                bool attacking = hullGap <= AttackRange;
                float cueClose = Mathf.Clamp01(1f - hullGap / CueRange);

                if (attacking && _attack != null && body.Playing != "Attack")
                {
                    body.FreeAt = now + _attackLen;
                    body.SwingAt = now + _attackLen * AttackCueFraction;
                    Play(body, _attack, false);
                }
                else if (attacking && _attack != null && body.Playing == "Attack" && now > body.FreeAt)
                {
                    // Still biting: restart the clip so every attack cycle has its swing.
                    body.FreeAt = now + _attackLen;
                    body.SwingAt = now + _attackLen * AttackCueFraction;
                    Play(body, _attack, false);
                }
                else if (!attacking && body.Playing == "Attack" && now > body.FreeAt)
                {
                    Play(body, _walk, true);
                }
                bool walking = body.Playing == "Walk" && !attacking && body.Gait > 0.2f;
                if (body.Playing == "Walk")
                {
                    var st = body.Anim[_walk.name];
                    // playback tracks measured ground speed: no stride/slide mismatch
                    if (st != null)
                        st.speed = walking ? Mathf.Clamp(body.Gait / _walkGroundSpeed, 0.05f, 3f) : 0f;
                }

                UpdateFootLock(body, now, dt, walking);

                // Grounded stride: replaces only the legs, after the clip has written the pose.
                var audioRef = AudioDirector.Instance;
                if (body.Stride != null && body.Stride.Ready)
                {
                    bool strideWalking = _strideOn && walking && !attacking && body.Playing == "Walk";
                    body.Stride.Step(dt, body.Gait, body.Go.transform.position, body.Go.transform.rotation, strideWalking);

                    if (audioRef != null && strideWalking)
                    {
                        int c = body.Stride.Contacts;
                        if (body.FootstepContacts == 0) body.FootstepContacts = c;
                        for (; body.FootstepContacts < c; body.FootstepContacts++) audioRef.WalkerFootstep(cueClose);
                    }
                    else if (audioRef == null)
                    {
                        body.FootstepContacts = body.Stride.Contacts;
                    }
                }
                float wall = Time.unscaledTime;
                if (_footLog && body == focus && wall - body.DriftWindowStart > 3f)
                    LogFootLock(body, wall, walking, attacking);

                // Attack and footstep cues are view cues: the swing fires when the clip reaches
                // its contact beat, footsteps are earned by measured stride while walking.
                var audio = AudioDirector.Instance;
                if (audio != null)
                {
                    if (body.SwingAt > 0f && now >= body.SwingAt)
                    {
                        body.SwingAt = 0f;
                        if (attacking) audio.WalkerSwing(cueClose);
                    }
                    else if (!attacking && body.SwingAt > 0f)
                    {
                        body.SwingAt = 0f;
                    }

                    if (walking && dt > 0.0001f && !(body.Stride != null && body.Stride.Ready && _strideOn))
                    {
                        body.StepAccum += body.Gait * dt;
                        if (body.StepAccum >= FootstepStride)
                        {
                            body.StepAccum -= FootstepStride;
                            audio.WalkerFootstep(cueClose);
                        }
                    }
                    else
                    {
                        body.StepAccum = 0f;
                    }
                }

                // Legacy clips can carry root translation; keep the visual on the sim's position.
                if (body.Root != null)
                {
                    Vector3 before = body.Root.localPosition;
                    body.Root.localPosition = body.RootLocal
                        + body.Go.transform.InverseTransformVector(body.RootOffset);
                    if (!_rootLogged)
                    {
                        _rootLogged = true;
                        Debug.Log(string.Format("[walker3d] clip root offset {0} (clip {1})",
                            (before - body.RootLocal).magnitude, body.Playing));
                    }
                }

                if (_walkerCam != null && body == focus)
                {
                    var rs = body.Go.GetComponentsInChildren<Renderer>();
                    Bounds wb = rs.Length > 0 ? rs[0].bounds : new Bounds(body.Go.transform.position, Vector3.one * 4f);
                    for (int ri = 1; ri < rs.Length; ri++) wb.Encapsulate(rs[ri].bounds);
                    float h = Mathf.Max(1f, wb.size.y);
                    // Frame the whole body, feet included: eye lower than the old 0.6h and a
                    // look point near the waist, from a fixed distance derived from the vFOV.
                    float d = (h * 0.5f) / Mathf.Tan(_walkerCam.fieldOfView * Mathf.Deg2Rad * 0.5f) * 1.7f;
                    float hullS = Mathf.Clamp(e.S, trainS - SimConfig.WagonSpan * Mathf.Max(1, world.Train.Wagons),
                                                     trainS + SimConfig.TrainNose);
                    Vector3 toTrain = TrackSpace.Point(hullS, 0f, 0f) - pos;
                    toTrain.y = 0f;
                    if (toTrain.sqrMagnitude < 0.0001f) toTrain = Vector3.forward;
                    toTrain.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, toTrain).normalized;
                    _walkerCam.transform.position = pos + toTrain * d + side * (0.28f * d)
                                                        + Vector3.up * (h * 0.38f);
                    _walkerCam.transform.LookAt(new Vector3(wb.center.x, pos.y + h * 0.46f, wb.center.z));
                }
            }

            // deaths queued from the sim's Kill event
            for (int d = 0; d < _pending.Count; d++)
            {
                Body body = TakeFreeBody(now);
                if (body == null) break;
                body.Claimed = true;
                body.EnemyId = -1;
                Vector3 pos = TrackSpace.Point(_pending[d].S, _pending[d].X, 0f);
                // cosmetic acid burst exactly where the walker died; once per death, pooled
                AcidBurst.Play(pos + Vector3.up * 0.75f);
                if (body.Proxy != null) body.Proxy.Disable();
                body.Go.transform.position = pos;
                body.Go.transform.rotation = FaceConvoy(pos, _pending[d].S, trainS, world.Train.Wagons);
                body.FreeAt = now + _deathLen;
                Play(body, _death, false);
            }
            _pending.Clear();

            for (int i = 0; i < _pool.Count; i++)
            {
                Body b = _pool[i];
                if (b.Claimed) continue;
                bool holdingCorpse = b.Playing == "Death" && now < b.FreeAt;
                if (!holdingCorpse)
                {
                    if (b.Go.activeSelf) b.Go.SetActive(false);
                    if (b.Proxy != null) b.Proxy.Disable();
                    b.EnemyId = -1;
                    b.Playing = null;
                }
            }
        }

        Body FindBoundBody(int enemyId)
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                Body b = _pool[i];
                if (b.EnemyId == enemyId && b.Go.activeSelf && b.Playing != "Death") return b;
            }
            return null;
        }

        /// <summary>Next body that is neither bound to a live enemy nor holding a corpse.</summary>
        Body TakeFreeBody(float now)
        {
            for (int k = 0; k < _pool.Count; k++)
            {
                int i = (_poolCursor + k) % _pool.Count;
                Body b = _pool[i];
                if (b.Claimed || b.EnemyId >= 0) continue;
                if (b.Playing == "Death" && now < b.FreeAt) continue;
                _poolCursor = (i + 1) % _pool.Count;
                return b;
            }
            return null;
        }
    }
}

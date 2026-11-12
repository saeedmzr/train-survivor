using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// The real projectile driver. Every accepted <see cref="SimEventKind.Shot"/> with a
    /// projectile id becomes one pooled round that leaves the firing <see cref="GunRig"/>'s
    /// muzzle along that barrel and flies straight — no homing, no teleport. Each frame the
    /// travelled segment is swept with a non-allocating sphere cast against the enemy/blockade
    /// <see cref="HitProxy"/> colliders and the scenery colliders; only an actual hit calls
    /// <c>World.ResolveImpact</c>. A round that leaves the range, times out, or strikes scenery
    /// is cancelled and does no damage.
    ///
    /// While this component is bound to a live World it owns hit timing: the core's timer
    /// damage is off (<c>World.ViewDrivenProjectiles</c>) and the old homing tracer in
    /// <see cref="Effects"/> is disabled. On a stage/run reset every round is dropped so an
    /// old bullet can never resolve against the new track.
    /// </summary>
    public sealed class ProjectileDriver : MonoBehaviour
    {
        sealed class Round
        {
            public SpriteBillboard Sprite;
            public TracerView Tracer;
            public bool Live;
            public int Id;
            public int Gun;
            public int TargetId;
            public bool Blockade;
            public Vector3 Pos;
            public Vector3 Dir;
            public float Age;
            public float Range;
            public float TrailAcc;
        }

        sealed class Trail
        {
            public SpriteBillboard Sprite;
            public float Life;
        }

        const int RoundCount = 32;
        const int TrailCount = 96;

        /// <summary>Barrel speed, m/s — the same 170 the old tracer and the timer delay used.</summary>
        const float Speed = 170f;
        /// <summary>Radius of the swept projectile sphere. Proxies are ~0.5 m in radius, so
        /// this closes a frame gap without making near-misses count.</summary>
        const float HitRadius = 0.22f;
        const float MaxLife = 1.2f;
        const float MaxRange = 60f;
        const float TrailStep = 1.1f;

        static readonly Color RoundTint = new Color(1f, 0.80f, 0.40f);
        static readonly Color TrailTint = new Color(1f, 0.58f, 0.18f);
        static readonly Color TracerTint = new Color(1f, 0.86f, 0.56f);

        /// <summary>Length/thickness of the tapered streak behind a round, metres. The art
        /// only fills ~52% of the sprite width and ~20% of its height, so the quad is sized
        /// up: at a 25 m chase distance this reads as a 10-14 px streak, not a hairline.</summary>
        const float TracerLength = 3.4f;
        const float TracerThickness = 0.55f;

        readonly Round[] _rounds = new Round[RoundCount];
        readonly Trail[] _trails = new Trail[TrailCount];
        readonly RaycastHit[] _hits = new RaycastHit[12];

        GameLoop _loop;
        TrainRig _train;
        Effects _fx;
        Camera _cam;
        World _world;
        int _stage = -1;
        int _sceneryLayer = 6;
        int _trailCursor;

        int _accepted, _impacts, _misses;
        int _spawnLogs, _hitLogs, _missLogs;
        bool _summaryLogged;
        bool _verbose;

        public int AcceptedShots => _accepted;
        public int Impacts => _impacts;
        public int Misses => _misses;

        public void Build(GameLoop loop, TrainRig train, Effects fx)
        {
            _loop = loop;
            _train = train;
            _fx = fx;
            _verbose = DevTuning.Get("dev.verbose", 0f) > 0.5f;

            int layer = LayerMask.NameToLayer("Scenery");
            if (layer >= 0) _sceneryLayer = layer;
            _cam = Camera.main;
            if (_cam == null) _cam = Object.FindObjectOfType<Camera>();

            var roundsRoot = new GameObject("Rounds").transform;
            roundsRoot.SetParent(transform, false);
            for (int i = 0; i < _rounds.Length; i++)
            {
                _rounds[i] = new Round
                {
                    Sprite = SpriteBillboard.Create(roundsRoot, "Round" + i, true),
                    Tracer = TracerView.Create(roundsRoot, "Tracer" + i, "proj_bullet_tracer")
                };
            }

            var trailRoot = new GameObject("Trails").transform;
            trailRoot.SetParent(transform, false);
            for (int i = 0; i < _trails.Length; i++)
                _trails[i] = new Trail { Sprite = SpriteBillboard.Create(trailRoot, "Trail" + i, true) };

            Debug.Log(string.Format("[proj] tracer art={0} cam={1} pool={2}",
                ArtSprites.Has("proj_bullet_tracer"), _cam != null ? _cam.name : "none", _rounds.Length));

            if (loop != null) loop.SimEventRaised += OnSimEvent;
            Bind(loop != null ? loop.World : null);
        }

        void OnEnable()
        {
            if (_loop == null) return;
            _loop.SimEventRaised -= OnSimEvent;
            _loop.SimEventRaised += OnSimEvent;
            Bind(_loop.World);
        }

        void OnDisable()
        {
            if (_loop != null) _loop.SimEventRaised -= OnSimEvent;
            Unbind();
        }

        // ---------------------------------------------------------------- binding

        void Bind(World world)
        {
            if (world == _world) return;
            Unbind();
            _world = world;
            if (world == null) return;

            world.StageRebuilt += OnStageRebuilt;
            // Only now that the pool, sweep and event hook exist does the core hand over
            // hit timing; timer damage stays active until this line.
            world.ViewDrivenProjectiles = true;
            _stage = world.Train.Stage;
            if (_fx != null) _fx.SetTimerTracers(false);
            if (_verbose) Debug.Log("[proj] driver bound — view-driven projectiles ON (timer damage off)");
        }

        void Unbind()
        {
            ClearRounds("unbind");
            if (_world != null)
            {
                _world.StageRebuilt -= OnStageRebuilt;
                _world.ViewDrivenProjectiles = false;
            }
            _world = null;
            if (_fx != null) _fx.SetTimerTracers(true);
        }

        void OnStageRebuilt() => ClearRounds("reset");

        void ClearRounds(string reason)
        {
            int n = 0;
            for (int i = 0; i < _rounds.Length; i++)
            {
                var r = _rounds[i];
                if (!r.Live) continue;
                if (_world != null) _world.CancelProjectile(r.Id);
                r.Live = false;
                r.Sprite.Hide();
                if (r.Tracer != null) r.Tracer.Hide();
                n++;
            }
            if (_verbose && n > 0) Debug.Log("[proj] cleared " + n + " round(s): " + reason);
        }

        // ---------------------------------------------------------------- spawn

        void OnSimEvent(SimEvent e)
        {
            if (e.Kind != SimEventKind.Shot) return;
            if (_world == null || !_world.ViewDrivenProjectiles || e.ProjectileId <= 0) return;

            var r = FreeRound();
            if (r == null)
            {
                // Pool exhausted: drop the core record so the shot can never damage later.
                _world.CancelProjectile(e.ProjectileId);
                return;
            }

            Vector3 from = _train != null ? _train.MuzzlePosition(e.GunIndex)
                                          : TrackSpace.Point(e.FromS, e.FromX, 2.2f);
            Vector3 dir = _train != null ? _train.MuzzleForward(e.GunIndex) : Vector3.forward;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            r.Live = true;
            r.Id = e.ProjectileId;
            r.Gun = e.GunIndex;
            r.TargetId = e.EnemyId > 0 ? e.EnemyId : e.BlockadeId;
            r.Blockade = e.BlockadeId > 0;
            r.Pos = from;
            r.Dir = dir;
            r.Age = 0f;
            r.Range = 0f;
            r.TrailAcc = 0f;

            r.Sprite.Show("bullet_core", 0.55f, RoundTint);
            r.Sprite.transform.position = from;
            if (r.Tracer != null)
            {
                r.Tracer.Show(from, dir, _cam, TracerLength, TracerThickness, TracerTint, 0.09f, 1f);
                r.Tracer.KeepAlive();
            }

            if (_fx != null) _fx.MuzzleFlash(from, dir);

            _accepted++;
            if (_verbose && _spawnLogs < 12)
            {
                _spawnLogs++;
                Vector3 aimPoint = TrackSpace.Point(e.ToS, e.ToX, 1.0f);
                float err = Vector3.Angle(dir, aimPoint - from);
                Debug.Log(string.Format(
                    "[proj] spawn pid={0} gun={1} target={2}{3} muzzle=({4:0.00},{5:0.00},{6:0.00}) dir=({7:0.00},{8:0.00},{9:0.00}) aimErr={10:0.0}deg",
                    r.Id, r.Gun, r.TargetId, r.Blockade ? " wall" : "",
                    from.x, from.y, from.z, dir.x, dir.y, dir.z, err));
            }
        }

        Round FreeRound()
        {
            for (int i = 0; i < _rounds.Length; i++)
                if (!_rounds[i].Live) return _rounds[i];
            return null;
        }

        // ---------------------------------------------------------------- flight

        void LateUpdate()
        {
            if (_loop == null) return;
            if (_loop.World != _world) Bind(_loop.World);
            if (_world == null) return;

            if (_world.Train.Stage != _stage) { ClearRounds("stage"); _stage = _world.Train.Stage; }

            float dt = Time.deltaTime;
            StepTrails(dt);
            // Only retired streaks fade. A live round refreshes its tracer every frame, and
            // ticking it as well would let a long frame (low fps / hitch) expire it before
            // the next refresh and hide it mid-flight.
            for (int i = 0; i < _rounds.Length; i++)
                if (!_rounds[i].Live && _rounds[i].Tracer != null) _rounds[i].Tracer.Tick(dt);
            if (dt <= 0f) return;

            // Walker/blockade proxies are placed in other LateUpdates; query the freshest pose.
            Physics.SyncTransforms();

            for (int i = 0; i < _rounds.Length; i++)
            {
                var r = _rounds[i];
                if (r.Live) StepRound(r, dt);
            }

            if (!_summaryLogged && _verbose && _accepted >= 40)
            {
                _summaryLogged = true;
                Debug.Log(string.Format("[proj] summary accepted {0} impacts {1} misses {2}",
                    _accepted, _impacts, _misses));
            }
        }

        void StepRound(Round r, float dt)
        {
            r.Age += dt;

            if (r.Age > MaxLife || r.Range > MaxRange)
            {
                Miss(r, r.Age > MaxLife ? "expired" : "range", r.Pos);
                return;
            }

            float remaining = Speed * dt;
            int guard = 0;
            while (remaining > 0.0001f && guard++ < 8)
            {
                int count = Physics.SphereCastNonAlloc(r.Pos, HitRadius, r.Dir, _hits, remaining,
                                                       Physics.DefaultRaycastLayers,
                                                       QueryTriggerInteraction.Collide);
                int best = -1;
                float bestDist = float.MaxValue;
                HitProxy bestProxy = null;
                bool bestScenery = false;

                for (int i = 0; i < count; i++)
                {
                    if (_hits[i].distance >= bestDist) continue;
                    var proxy = HitProxy.Of(_hits[i].collider);
                    bool scenery = _hits[i].collider.gameObject.layer == _sceneryLayer;
                    if (proxy == null && !scenery) continue;   // train, shooter, anything else: ignored
                    best = i;
                    bestDist = _hits[i].distance;
                    bestProxy = proxy;
                    bestScenery = scenery;
                }

                if (best < 0)
                {
                    Advance(r, remaining);
                    remaining = 0f;
                    break;
                }

                Vector3 point = r.Pos + r.Dir * Mathf.Max(0f, _hits[best].distance);
                r.Pos = point;

                if (bestProxy != null)
                {
                    Impact(r, bestProxy, point);
                    return;
                }

                if (bestScenery)
                {
                    Miss(r, "scenery", point);
                    return;
                }

                // A collider that is neither: nothing to stop on, keep sweeping.
                float past = Mathf.Min(remaining, Mathf.Max(0f, _hits[best].distance) + 0.02f);
                Advance(r, past);
                remaining -= past;
            }

            r.Sprite.transform.position = r.Pos;
            PlaceTracer(r);
        }

        void PlaceTracer(Round r)
        {
            if (r.Tracer == null) return;
            r.Tracer.Place(r.Pos, r.Dir, _cam);
            r.Tracer.KeepAlive();
        }

        void Advance(Round r, float distance)
        {
            if (distance <= 0f) return;
            r.Pos += r.Dir * distance;
            r.Range += distance;

            r.TrailAcc += distance;
            if (r.TrailAcc >= TrailStep)
            {
                r.TrailAcc = 0f;
                EmitTrail(r.Pos);
            }
        }

        void Impact(Round r, HitProxy proxy, Vector3 point)
        {
            float before = HpOf(proxy.IsBlockade, proxy.EntityId);
            bool landed = _world.ResolveImpact(r.Id, proxy.IsBlockade, proxy.EntityId);
            float after = HpOf(proxy.IsBlockade, proxy.EntityId);
            _impacts++;

            if (r.Tracer != null) r.Tracer.FadeOut(0.06f);

            if (_verbose && _hitLogs < 12)
            {
                _hitLogs++;
                Debug.Log(string.Format(
                    "[proj] impact pid={0} {1}={2} at=({3:0.00},{4:0.00},{5:0.00}) landed={6} hp {7:0.0}->{8:0.0}",
                    r.Id, proxy.IsBlockade ? "wall" : "enemy", proxy.EntityId,
                    point.x, point.y, point.z, landed, before, after));
            }

            Despawn(r);
        }

        void Miss(Round r, string reason, Vector3 at)
        {
            if (_world != null) _world.CancelProjectile(r.Id);
            _misses++;

            if (r.Tracer != null) r.Tracer.FadeOut(0.06f);

            if (_verbose && _missLogs < 8)
            {
                _missLogs++;
                Debug.Log(string.Format("[proj] miss pid={0} gun={1} reason={2} at=({3:0.00},{4:0.00},{5:0.00})",
                    r.Id, r.Gun, reason, at.x, at.y, at.z));
            }

            if (reason == "scenery" && _fx != null)
            {
                // Steel, not a body: brighter ring, white sparks, and a ping the flesh hit
                // does not have. This is the only place a scenery strike is observable.
                _fx.ImpactAt(at, r.Dir, true);
                var audio = Audio.AudioDirector.Instance;
                if (audio != null && _train != null)
                {
                    float d = Vector3.Distance(at, _train.transform.position);
                    audio.ImpactMetal(Mathf.Clamp01(1f - d / 40f));
                }
            }
            Despawn(r);
        }

        void Despawn(Round r)
        {
            r.Live = false;
            r.Sprite.Hide();
        }

        float HpOf(bool blockade, int id)
        {
            if (id <= 0 || _world == null) return -1f;
            if (blockade)
            {
                for (int i = 0; i < _world.BlockadeCount; i++)
                    if (_world.Blockades[i].Id == id) return _world.Blockades[i].Hp;
                return -1f;
            }
            for (int i = 0; i < _world.EnemyCount; i++)
                if (_world.Enemies[i].Id == id) return _world.Enemies[i].Hp;
            return -1f;
        }

        // ---------------------------------------------------------------- tracer

        void EmitTrail(Vector3 at)
        {
            var t = _trails[_trailCursor];
            _trailCursor = (_trailCursor + 1) % _trails.Length;
            t.Life = 0.12f;
            t.Sprite.Show("dot", 0.7f, TrailTint);
            t.Sprite.transform.position = at;
        }

        void StepTrails(float dt)
        {
            for (int i = 0; i < _trails.Length; i++)
            {
                var t = _trails[i];
                if (t.Life <= 0f) continue;
                t.Life -= dt;
                if (t.Life <= 0f) t.Sprite.Hide();
            }
        }
    }
}

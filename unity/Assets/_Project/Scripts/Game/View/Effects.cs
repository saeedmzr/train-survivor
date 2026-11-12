using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// The juice layer: travelling bullets, muzzle flashes, impact bursts, brake sparks, gore.
    ///
    /// Shots used to be a stretched cube held for 45 ms — that is the "line, then the enemy
    /// dies" the playtest reported. Now a shot spawns a painted projectile that actually
    /// crosses the distance (the sim still resolves damage instantly), and a killed enemy
    /// holds its last pose for the length of that flight so the hit reads as connected.
    /// </summary>
    public sealed class Effects : MonoBehaviour
    {
        sealed class Bullet
        {
            public SpriteBillboard Sprite;
            public Vector3 From, To;
            public float T, Dur;
            public bool Live;
            public int TargetId;
            public bool Blockade;
        }

        sealed class Splat
        {
            public SpriteBillboard Sprite;
            public float Life;
        }

        sealed class Corpse
        {
            public SpriteBillboard Sprite;
            public EnemyKind Kind;
            public Vector3 At;
            public float Life;
        }

        /// <summary>Metres per second. Fast enough that the sim's instant hit still reads.</summary>
        const float BulletSpeed = 170f;
        const float CorpseHold = 0.09f;   // ≈ flight time; the bullet lands, then the gore

        /// <summary>Dev only (tune.txt dev.bulletTime): stretches the flight so a single frame
        /// can be caught mid-shot for screenshots. 1 = shipping speed.</summary>
        float _flightScale = 1f;

        /// <summary>False while ProjectileDriver owns the rounds: the old homing tracer is a
        /// duplicate visual and must not draw a second bullet or impact.</summary>
        bool _timerTracers = true;

        readonly List<Bullet> _bullets = new List<Bullet>(48);
        readonly List<Splat> _flashes = new List<Splat>(48);
        readonly List<Splat> _splats = new List<Splat>(20);
        readonly List<Corpse> _corpses = new List<Corpse>(18);

        ParticleSystem _sparks;
        ParticleSystem _embers;
        Material _sparkMat;
        TrainRig _train;
        Camera _cam;
        readonly List<TracerView> _flares = new List<TracerView>(24);
        int _flareCursor;
        bool _vfxBeam;
        int _bulletCursor, _flashCursor, _splatCursor, _corpseCursor;
        float _trailCd;

        public void Build(TrainRig train)
        {
            _train = train;
            _cam = Camera.main;
            if (_cam == null) _cam = Object.FindObjectOfType<Camera>();
            _vfxBeam = DevTuning.Get("dev.vfxbeam", 0f) > 0.5f;
            _flightScale = Mathf.Max(0.05f, DevTuning.Get("dev.bulletTime", 1f));
            ArtSprites.Preload();
            _sparkMat = Palette.Get("Spark", Palette.Neon);

            var bulletsRoot = new GameObject("Bullets").transform;
            bulletsRoot.SetParent(transform, false);
            for (int i = 0; i < 48; i++)
                _bullets.Add(new Bullet { Sprite = SpriteBillboard.Create(bulletsRoot, "Bullet" + i, true) });

            var flashesRoot = new GameObject("Flashes").transform;
            flashesRoot.SetParent(transform, false);
            for (int i = 0; i < 48; i++)
                _flashes.Add(new Splat { Sprite = SpriteBillboard.Create(flashesRoot, "Flash" + i, true) });

            for (int i = 0; i < 20; i++)
                _splats.Add(new Splat { Sprite = SpriteBillboard.Create(transform, "Splat" + i) });

            var corpseRoot = new GameObject("Corpses").transform;
            corpseRoot.SetParent(transform, false);
            for (int i = 0; i < 18; i++)
                _corpses.Add(new Corpse { Sprite = SpriteBillboard.Create(corpseRoot, "Corpse" + i) });

            // Muzzle flares: the same tapered streak art as the tracer, held for a few
            // frames at the barrel. Pooled, so a burst of fire never allocates.
            var flareRoot = new GameObject("MuzzleFlares").transform;
            flareRoot.SetParent(transform, false);
            for (int i = 0; i < 24; i++)
                _flares.Add(TracerView.Create(flareRoot, "Flare" + i, "proj_bullet_tracer"));

            BuildParticles();
        }

        void BuildParticles()
        {
            _sparks = MakeParticles("Sparks", new Color(0.4f, 0.95f, 1f), 0.55f, 7.5f, 0.13f, 1.6f, 500);
            _embers = MakeParticles("Embers", new Color(1f, 0.55f, 0.12f), 0.6f, 2.4f, 0.22f, 0.35f, 300);
        }

        ParticleSystem MakeParticles(string name, Color color, float life, float speed, float size,
                                     float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = gravity;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 48f;
            shape.radius = 0.35f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var dot = Resources.Load<Texture2D>("Art/spark_dot");
            if (dot != null)
            {
                var mat = new Material(_sparkMat) { name = _sparkMat.name + " (dot)" };
                mat.mainTexture = dot;
                renderer.sharedMaterial = mat;
            }
            else
            {
                renderer.sharedMaterial = _sparkMat;
            }
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        public void Handle(SimEvent e)
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            OnSimEvent(e, loop.World);
        }

        public void OnSimEvent(SimEvent e, World world)
        {
            switch (e.Kind)
            {
                case SimEventKind.Shot:
                    // A real driver draws the round from the barrel; the timer tracer would be a
                    // second, homing bullet on top of it.
                    if (!world.ViewDrivenProjectiles) FireBullet(e, world);
                    break;
                case SimEventKind.EnemyHit:
                    // Bullet hurt, not the train being bitten (ContactHit): a hit flash on the
                    // body the round actually struck. Flesh, not steel — warm tint, embers.
                    ShowOne(_flashes, "impact_core", TrackSpace.Point(e.ToS, e.ToX, 1.0f), 0.70f, 0.10f,
                            new Color(1f, 0.62f, 0.42f));
                    BurstSparksAt(e.ToS, e.ToX, 4, _embers);
                    break;
                case SimEventKind.Brake:
                    if (world.Train.Speed < 1.5f) BurstSparks(world.Train.S, 22, _sparks);
                    break;
                case SimEventKind.Ram:
                    BurstSparks(world.Train.S + 2.5f, 14, _sparks);
                    break;
                case SimEventKind.BlockadeHit:
                    BurstSparksAt(e.ToS, e.ToX, 4, _sparks);
                    break;
                case SimEventKind.Kill:
                    // hold the pose white-hot; the gore and sparks fire when the corpse drops
                    ShowCorpse(e);
                    break;
                case SimEventKind.Blast:
                    // bloater going up: ring, embers, and a chunk of track debris
                    ShowOne(_flashes, "fx_blast_ring", TrackSpace.Point(e.ToS, e.ToX, 0.9f), 4.2f, 0.22f, Color.white);
                    BurstSparksAt(e.ToS, e.ToX, 30, _embers);
                    BurstSparksAt(e.ToS, e.ToX, 14, _sparks);
                    ShowOne(_splats, "block_debris", TrackSpace.Point(e.ToS, e.ToX, 0.04f), 1.6f, 1.0f, Color.white);
                    break;
                case SimEventKind.BlockadeBroken:
                    BurstSparksAt(e.ToS, e.ToX, 26, _embers);
                    ShowOne(_flashes, "fx_blast_ring", TrackSpace.Point(e.ToS, e.ToX, 0.8f), 3.4f, 0.18f, Color.white);
                    ShowOne(_splats, "block_debris", TrackSpace.Point(e.ToS, e.ToX, 0.04f), 2.2f, 1.2f, Color.white);
                    break;
                case SimEventKind.PickupTaken:
                    BurstSparksAt(e.ToS, e.ToX, 2, _embers);
                    break;
            }
        }

        // ---------------------------------------------------------------- driver hooks

        /// <summary>Turn the legacy homing tracer off while a real projectile driver owns hit
        /// timing, and back on if that driver goes away.</summary>
        public void SetTimerTracers(bool on)
        {
            _timerTracers = on;
            if (on) return;
            for (int i = 0; i < _bullets.Count; i++)
            {
                _bullets[i].Live = false;
                _bullets[i].Sprite.Hide();
            }
        }

        /// <summary>Muzzle bloom at the barrel that actually fired (driver-driven shots):
        /// a warm core plus a short cone of flame along the barrel, both pooled.</summary>
        public void MuzzleFlash(Vector3 at, Vector3 dir)
        {
            ShowOne(_flashes, "muzzle_core", at, 0.62f, 0.055f, new Color(1f, 0.86f, 0.62f));
            if (dir.sqrMagnitude < 1e-5f) return;

            var flare = _flares[_flareCursor];
            _flareCursor = (_flareCursor + 1) % _flares.Count;
            flare.Show(at + dir.normalized * 0.18f, dir.normalized, _cam,
                       1.95f, 1.15f, new Color(1f, 0.80f, 0.45f), 0.075f, 1f);
        }

        /// <summary>Compatibility overload: a bloom with no direction (legacy tracer path).</summary>
        public void MuzzleFlash(Vector3 at)
        {
            ShowOne(_flashes, "muzzle_core", at, 0.62f, 0.055f, new Color(1f, 0.86f, 0.62f));
        }

        /// <summary>A round struck something that is not an entity (scenery). Steel answers
        /// differently from a body: a kinetic ring and white sparks, not a wet puff.</summary>
        public void ImpactAt(Vector3 at, Vector3 dir, bool metal)
        {
            if (metal)
            {
                ShowOne(_flashes, "fx_impact_kinetic", at, 0.55f, 0.09f, new Color(0.85f, 0.95f, 1f));
                _sparks.transform.position = at;
                _sparks.Emit(10);
            }
            else
            {
                ShowOne(_flashes, "impact_core", at, 0.45f, 0.09f, new Color(1f, 0.58f, 0.42f));
                _sparks.transform.position = at;
                _sparks.Emit(4);
            }
        }

        public void ImpactAt(Vector3 at) => ImpactAt(at, Vector3.forward, false);

        // ---------------------------------------------------------------- shots

        void FireBullet(SimEvent e, World world)
        {
            var b = _bullets[_bulletCursor];
            _bulletCursor = (_bulletCursor + 1) % _bullets.Count;

            Vector3 from = _train != null ? _train.MuzzlePosition(e.GunIndex)
                                          : TrackSpace.Point(e.FromS, e.FromX, 2.2f);
            Vector3 to = TrackSpace.Point(e.ToS, e.ToX, 1.0f);

            b.From = from;
            b.To = to;
            b.T = 0f;
            b.Dur = Mathf.Clamp(Vector3.Distance(from, to) / BulletSpeed, 0.045f, 0.13f) * _flightScale;
            b.Live = true;
            b.TargetId = e.EnemyId > 0 ? e.EnemyId : e.BlockadeId;
            b.Blockade = e.BlockadeId > 0;
            b.Sprite.Show("bullet_core", 0.55f, Color.white);
            b.Sprite.transform.position = from;

            // muzzle flash: a warm core plus a short flare down the barrel
            MuzzleFlash(from, to - from);
        }

        void Impact(Vector3 at)
        {
            ShowOne(_flashes, "impact_core", at, 0.5f, 0.10f, new Color(1f, 0.62f, 0.42f));
            _sparks.transform.position = at;
            _sparks.Emit(4);
        }

        // ---------------------------------------------------------------- death

        void ShowCorpse(SimEvent e)
        {
            var c = _corpses[_corpseCursor];
            _corpseCursor = (_corpseCursor + 1) % _corpses.Count;

            c.Kind = e.EnemyKind;
            c.At = TrackSpace.Point(e.ToS, e.ToX, 0.05f);
            c.Life = CorpseHold;
            c.Sprite.Show(EnemySpecies.Sprite(c.Kind), SizeOf(c.Kind), Color.white, 1f);
            c.Sprite.Tilt = Random.Range(-8f, 8f);
            c.Sprite.transform.position = c.At;
        }

        void Gore(Vector3 at, EnemyKind kind)
        {
            ShowOne(_splats, DeathFx(kind), at, SizeOf(kind), 0.5f, Color.white);
            _sparks.transform.position = at;
            _sparks.Emit(6);
        }

        static float SizeOf(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Tank: return 2.9f;
                case EnemyKind.Rimeback: return 2.6f;
                case EnemyKind.Bulwark: return 2.3f;
                case EnemyKind.Bloater: return 2.1f;
                case EnemyKind.Drillmaw: return 1.5f;
                case EnemyKind.Sprinter: return 1.9f;
                case EnemyKind.Crawler: return 1.15f;
                case EnemyKind.Glider: return 1.6f;
                case EnemyKind.Lantern: return 2.2f;
                case EnemyKind.Shrieker: return 1.75f;
                case EnemyKind.Spitter: return 1.8f;
                default: return 2f;   // walker
            }
        }

        static string DeathFx(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Tank:
                case EnemyKind.Bulwark:
                case EnemyKind.Drillmaw: return "fx_death_metal";
                case EnemyKind.Rimeback: return "fx_death_ice";
                case EnemyKind.Crawler:
                case EnemyKind.Spitter:
                case EnemyKind.Lantern: return "fx_death_bone";
                case EnemyKind.Glider: return "fx_death_swarm";
                default: return "fx_death_goo";
            }
        }

        // ---------------------------------------------------------------- helpers

        void ShowOne(List<Splat> pool, string sprite, Vector3 at, float size, float life, Color tint)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var s = pool[i];
                if (s.Life > 0f) continue;
                s.Life = life;
                s.Sprite.Show(sprite, size, tint);
                s.Sprite.Tilt = Random.Range(0f, 360f);
                s.Sprite.transform.position = at;
                return;
            }
        }

        void BurstSparks(float s, int count, ParticleSystem system)
        {
            if (system == null) return;
            system.transform.position = TrackSpace.Point(s, 0f, 0.45f);
            system.Emit(count);
        }

        void BurstSparksAt(float s, float x, int count, ParticleSystem system)
        {
            if (system == null) return;
            system.transform.position = TrackSpace.Point(s, x, 0.45f);
            system.Emit(count);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            var aimLoop = GetComponentInParent<GameLoop>();
            World aimWorld = aimLoop != null ? aimLoop.World : null;

            for (int i = 0; i < _bullets.Count; i++)
            {
                var b = _bullets[i];
                if (!_timerTracers || !b.Live) continue;

                // home onto the live target: damage lands by id a few ticks later, so the tracer
                // must land where the enemy/wall actually is, not where it was at the trigger
                if (aimWorld != null && b.TargetId > 0)
                {
                    if (b.Blockade)
                    {
                        for (int ti = 0; ti < aimWorld.BlockadeCount; ti++)
                        {
                            Blockade bl = aimWorld.Blockades[ti];
                            if (bl.Alive && bl.Id == b.TargetId)
                            {
                                b.To = TrackSpace.Point(bl.S, bl.X, 1.0f);
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int ti = 0; ti < aimWorld.EnemyCount; ti++)
                        {
                            Enemy e = aimWorld.Enemies[ti];
                            if (e.Alive && e.Id == b.TargetId)
                            {
                                b.To = TrackSpace.Point(e.S, e.X, 1.0f);
                                break;
                            }
                        }
                    }
                }

                b.T += dt;
                float k = Mathf.Clamp01(b.T / b.Dur);
                Vector3 p = Vector3.Lerp(b.From, b.To, k);
                p += Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.05f;   // a hair of drop-off arc
                b.Sprite.transform.position = p;
                b.Sprite.transform.localScale = b.Sprite.BaseScale * Mathf.Lerp(1.15f, 0.7f, k);

                // comet tail: a short-lived glow dropped at every frame it flies through, so
                // the shot reads as a streak even on a phone at 60 fps
                ShowOne(_flashes, "dot", p, 0.75f, 0.14f, new Color(1f, 0.82f, 0.55f));

                if (k >= 1f)
                {
                    b.Live = false;
                    b.Sprite.Hide();
                    Impact(b.To);
                }
            }

            for (int i = 0; i < _flashes.Count; i++)
            {
                var f = _flashes[i];
                if (f.Life <= 0f) continue;
                f.Life -= dt;
                if (f.Life <= 0f) f.Sprite.Hide();
            }

            for (int i = 0; i < _flares.Count; i++) _flares[i].Tick(dt);

            // Diagnostic: three fixed streaks in front of the camera answer "does this art
            // render at all" independently of shot timing. dev.vfxbeam=1 only.
            if (_vfxBeam && _cam != null)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = _cam.transform.position + _cam.transform.forward * 9f
                              + _cam.transform.right * ((i - 1) * 1.6f)
                              + _cam.transform.up * (0.4f - i * 0.35f);
                    var f = _flares[i];
                    Vector3 beamDir = (_cam.transform.forward + _cam.transform.right * 0.45f).normalized;
                    f.Show(p, beamDir, _cam, 2.2f, 0.28f,
                           new Color(1f, 0.85f, 0.55f), 1f, 1f);
                    f.KeepAlive();
                }
            }

            for (int i = 0; i < _corpses.Count; i++)
            {
                var c = _corpses[i];
                if (c.Life <= 0f) continue;
                c.Life -= dt;
                c.Sprite.Tilt += dt * 55f;      // tips as it drops
                if (c.Life <= 0f)
                {
                    c.Sprite.Hide();
                    Gore(c.At, c.Kind);
                }
            }

            for (int i = 0; i < _splats.Count; i++)
            {
                var sp = _splats[i];
                if (sp.Life <= 0f) continue;
                sp.Life -= dt;
                if (sp.Life <= 0f) sp.Sprite.Hide();
            }

            // Fire trail: embers off the wheels while the scorched track burns.
            var loop = GetComponentInParent<GameLoop>();
            if (loop != null && loop.World != null &&
                loop.World.Train.CardStacksOf(CardId.FireTrail) > 0 &&
                loop.World.Train.Speed > SimConfig.FireTrailMinSpeed)
            {
                _trailCd -= dt;
                if (_trailCd <= 0f)
                {
                    _trailCd = 0.09f;
                    BurstSparks(loop.World.Train.S - 2f, 3, _embers);
                }
            }
        }
    }
}

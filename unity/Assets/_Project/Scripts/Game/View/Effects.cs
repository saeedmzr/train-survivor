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

        readonly List<Bullet> _bullets = new List<Bullet>(48);
        readonly List<Splat> _flashes = new List<Splat>(48);
        readonly List<Splat> _splats = new List<Splat>(20);
        readonly List<Corpse> _corpses = new List<Corpse>(18);

        ParticleSystem _sparks;
        ParticleSystem _embers;
        Material _sparkMat;
        TrainRig _train;
        int _bulletCursor, _flashCursor, _splatCursor, _corpseCursor;
        float _trailCd;

        public void Build(TrainRig train)
        {
            _train = train;
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
                    FireBullet(e, world);
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
            b.Sprite.Show("bullet_core", 1.2f, Color.white);
            b.Sprite.transform.position = from;

            // muzzle flash: a short hot burst at the barrel, not a light source
            ShowOne(_flashes, "muzzle_core", from, 1.5f, 0.08f, Color.white);
        }

        void Impact(Vector3 at)
        {
            ShowOne(_flashes, "impact_core", at, 1.35f, 0.11f, Color.white);
            _sparks.transform.position = at;
            _sparks.Emit(5);
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

            for (int i = 0; i < _bullets.Count; i++)
            {
                var b = _bullets[i];
                if (!b.Live) continue;

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

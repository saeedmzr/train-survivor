using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Pooled painted enemies (art-work/enemies → atlas_enemies). One sprite per species, so
    /// the motion has to come from code: every species gets its own gait (bob rate, roll,
    /// sway), a forward lean while it closes, a squash on contact and a hover for the flyer.
    /// </summary>
    public sealed class EnemyViewPool : MonoBehaviour
    {
        struct Gait
        {
            public float Height;
            public float BobAmp, BobRate;   // vertical idle/walk bob
            public float TiltDeg;           // roll amplitude
            public float Sway;              // lateral drift
            public float Hover;             // metres above the ballast (flyers)
            public float Lean;              // lean per m/s of closing speed
        }

        sealed class Body
        {
            public SpriteBillboard Sprite;
            public float Phase;
            public float Step;      // walk-cycle phase, in frames
            public EnemyKind Kind;
            public HitProxy Proxy;  // ballistics target for sprite species
        }

        /// <summary>Six poses per species (tools/make_enemy_walk.py), packed as en_<id>_f1..f5.</summary>
        const int Frames = 6;

        static Gait GaitOf(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Sprinter:
                    return new Gait { Height = 1.9f, BobAmp = 0.055f, BobRate = 5.6f, TiltDeg = 9f, Sway = 0.08f, Lean = 1.6f };
                case EnemyKind.Tank:
                    return new Gait { Height = 2.9f, BobAmp = 0.018f, BobRate = 2.1f, TiltDeg = 1.6f, Sway = 0.03f, Lean = 0.3f };
                case EnemyKind.Crawler:
                    return new Gait { Height = 1.15f, BobAmp = 0.02f, BobRate = 6.4f, TiltDeg = 3.5f, Sway = 0.1f, Lean = 0.9f };
                case EnemyKind.Bloater:
                    return new Gait { Height = 2.1f, BobAmp = 0.045f, BobRate = 2.4f, TiltDeg = 4f, Sway = 0.09f, Lean = 0.4f };
                case EnemyKind.Spitter:
                    return new Gait { Height = 1.75f, BobAmp = 0.03f, BobRate = 3.4f, TiltDeg = 3f, Sway = 0.05f, Lean = 0.8f };
                case EnemyKind.Shrieker:
                    return new Gait { Height = 1.8f, BobAmp = 0.06f, BobRate = 4.2f, TiltDeg = 6f, Sway = 0.14f, Lean = 0.5f };
                case EnemyKind.Bulwark:
                    return new Gait { Height = 2.35f, BobAmp = 0.02f, BobRate = 2.3f, TiltDeg = 1.2f, Sway = 0.02f, Lean = 0.3f };
                case EnemyKind.Glider:
                    return new Gait { Height = 1.5f, BobAmp = 0.16f, BobRate = 1.7f, TiltDeg = 7f, Sway = 0.16f, Hover = 1.5f, Lean = 1.1f };
                case EnemyKind.Drillmaw:
                    return new Gait { Height = 1.45f, BobAmp = 0.04f, BobRate = 4.6f, TiltDeg = 5f, Sway = 0.06f, Lean = 1.0f };
                case EnemyKind.Lantern:
                    return new Gait { Height = 2.2f, BobAmp = 0.035f, BobRate = 2.7f, TiltDeg = 2.5f, Sway = 0.07f, Lean = 0.4f };
                case EnemyKind.Rimeback:
                    return new Gait { Height = 2.4f, BobAmp = 0.025f, BobRate = 2.0f, TiltDeg = 2f, Sway = 0.035f, Lean = 0.4f };
                default:
                    return new Gait { Height = 2.05f, BobAmp = 0.03f, BobRate = 3.1f, TiltDeg = 2.2f, Sway = 0.05f, Lean = 0.7f };
            }
        }

        readonly List<Body> _pool = new List<Body>();

        /// <summary>The scene is dark and the card art is painted dark; lift it a little so a
        /// horde reads at 30 m. Also a small scale bump for the same reason.</summary>
        static readonly Color Lift = new Color(1.18f, 1.16f, 1.12f, 1f);
        const float HeightBoost = 1.08f;

        public void Build()
        {
            ArtSprites.Preload();
            // Prewarm the pool so a horde never instantiates mid-fight.
            for (int i = 0; i < 48; i++) Get(i);
        }

        Body Get(int index)
        {
            while (_pool.Count <= index) _pool.Add(Create(_pool.Count));
            return _pool[index];
        }

        Body Create(int index)
        {
            return new Body
            {
                Sprite = SpriteBillboard.Create(transform, "Zombie" + index),
                Phase = index * 0.63f,
                Proxy = HitProxy.CreateCapsule(transform, "ZombieHit" + index, 0.5f, 1.8f)
            };
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World, Time.time);
        }

        public void Sync(World world, float time)
        {
            if (world == null) return;

            int used = 0;
            float trainS = world.Train.S;
            float trainSpeed = world.Train.Speed;

            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive) continue;

                // walkers are drawn by WalkerModelView with the finalized 3D model
                if (e.Kind == EnemyKind.Walker && WalkerModelView.Instance != null && WalkerModelView.Instance.Active)
                    continue;

                var body = Get(used++);
                Vector3 pos = TrackSpace.Point(e.S, e.X, 0f);
                Gait g = GaitOf(e.Kind);

                float t = time + body.Phase;
                float gap = e.S - trainS;

                // how fast this thing is eating the distance to the train
                float closing = Mathf.Clamp(e.Speed + trainSpeed, 0f, 12f);
                float urgency = Mathf.Clamp01(1f - gap / 30f);

                // The painted silhouette is a single pose, so the walk comes from the
                // generated frame set. It runs faster the harder the thing is closing, and
                // stops dead when it is stationary.
                float stride = 5.0f + Mathf.Abs(e.Speed) * 1.1f;
                body.Step += Time.deltaTime * stride;
                if (body.Step >= Frames) body.Step -= Frames;
                int frame = Mathf.Clamp((int)body.Step, 0, Frames - 1);

                // heartbeat bob — a lurch that speeds up as it closes
                float bob = Mathf.Sin(t * g.BobRate * (0.85f + urgency * 0.5f)) * g.BobAmp * 0.5f;

                // heavy things barely roll; runners swing and lean into the sprint
                float roll = Mathf.Sin(t * g.BobRate * 0.55f) * g.TiltDeg * (0.5f + urgency * 0.5f);
                roll -= Mathf.Sign(gap) * g.Lean * closing * 0.35f;
                float sway = Mathf.Sin(t * g.BobRate * 0.8f) * g.Sway * (1f - urgency * 0.6f);

                // contact beat: a quick shallow squash when it reaches the train
                float contact = Mathf.Clamp01(1f - Mathf.Abs(gap) / 4.5f);
                float beat = contact > 0f ? Mathf.Max(0f, Mathf.Sin(t * 9f)) * contact : 0f;
                float squash = 1f + Mathf.Sin(t * g.BobRate) * 0.02f + beat * 0.12f;

                // the flyer rides above the ballast
                float hover = g.Hover + Mathf.Sin(t * g.BobRate) * g.BobAmp * 2f;

                string sprite = frame == 0 ? EnemySpecies.Sprite(e.Kind)
                                           : EnemySpecies.Sprite(e.Kind) + "_f" + frame;
                body.Sprite.Show(sprite, g.Height * HeightBoost, Lift,
                                 e.Flash > 0.45f ? 0f : e.Flash * 0.55f);
                body.Sprite.transform.position = pos + Vector3.up * (bob + hover);
                body.Sprite.Tilt = roll * 0.55f;   // the frames already lean; keep this subtle
                body.Sprite.Squash = new Vector2(2f - squash, squash);
                body.Kind = e.Kind;

                if (body.Proxy != null)
                {
                    float h = g.Height * HeightBoost;
                    body.Proxy.SetCapsuleSize(h * 0.3f, h * 0.9f);
                    body.Proxy.Bind(e.Id, false);
                    body.Proxy.Move(pos + Vector3.up * (h * 0.5f));
                }
            }

            for (int i = used; i < _pool.Count; i++)
            {
                var b = _pool[i];
                if (b.Sprite.Visible) b.Sprite.Hide();
                if (b.Proxy != null) b.Proxy.Disable();
            }
        }
    }
}

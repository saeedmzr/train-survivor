using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>XP gems and health packs: they bob, they spin, they get vacuumed in.</summary>
    public sealed class PickupView : MonoBehaviour
    {
        readonly List<SpriteBillboard> _pool = new List<SpriteBillboard>();

        public void Build()
        {
            ArtSprites.Preload();
            for (int i = 0; i < 24; i++) Get(i);
        }

        SpriteBillboard Get(int i)
        {
            while (_pool.Count <= i) _pool.Add(SpriteBillboard.Create(transform, "Pickup" + _pool.Count));
            return _pool[i];
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
            for (int i = 0; i < world.PickupCount; i++)
            {
                Pickup p = world.Pickups[i];
                if (!p.Alive) continue;

                var bb = Get(used++);

                string sprite = p.Kind == PickupKind.HealthPack
                    ? "health_pack"
                    : (p.Id % 3 == 0 ? "gem_green" : p.Id % 3 == 1 ? "gem_blue" : "gem_purple");

                float height = p.Kind == PickupKind.HealthPack ? 0.9f : 0.62f;
                bb.Show(sprite, height, Color.white, 0f);

                float bob = Mathf.Abs(Mathf.Sin(p.Spin * 1.6f + p.Id)) * 0.18f;
                bb.transform.position = TrackSpace.Point(p.S, p.X, 0.12f + bob);

                // Fake a spin by squashing the quad horizontally.
                float squash = 0.55f + 0.45f * Mathf.Abs(Mathf.Cos(p.Spin));
                var s = bb.BaseScale;
                bb.transform.localScale = new Vector3(s.x * squash, s.y, s.z);

                // Fade out in the last seconds so loot never pops out of existence.
                if (p.Life < 3f)
                {
                    float a = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(p.Life * 9f));
                    bb.SetLook(new Color(1f, 1f, 1f, a), 0f);
                }
            }

            for (int i = used; i < _pool.Count; i++) _pool[i].Hide();
        }
    }
}

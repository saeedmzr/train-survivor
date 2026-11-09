using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>Destructible roadblocks: barrier, barrels, wreck. They stop the train dead.</summary>
    public sealed class BlockadeView : MonoBehaviour
    {
        readonly List<SpriteBillboard> _pool = new List<SpriteBillboard>();
        readonly List<HitProxy> _proxies = new List<HitProxy>();

        public void Build()
        {
            ArtSprites.Preload();
            for (int i = 0; i < 8; i++) Get(i);
        }

        SpriteBillboard Get(int i)
        {
            while (_pool.Count <= i)
            {
                _pool.Add(SpriteBillboard.Create(transform, "Blockade" + _pool.Count));
                // Ballistics proxy: a wall is a wide flat slab, not a capsule.
                _proxies.Add(HitProxy.CreateBox(transform, "BlockadeHit" + _proxies.Count,
                                                new Vector3(2.8f, 2.6f, 1.5f)));
            }
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
            for (int i = 0; i < world.BlockadeCount; i++)
            {
                Blockade b = world.Blockades[i];
                if (!b.Alive) continue;

                string sprite = b.Kind == BlockadeKind.Barrier ? "block_barrier"
                              : b.Kind == BlockadeKind.Barrels ? "block_barrels"
                              : "block_wreck";
                float height = b.Kind == BlockadeKind.Barrier ? 2.5f
                             : b.Kind == BlockadeKind.Barrels ? 2.9f
                             : 3.2f;

                var bb = Get(used++);
                bb.Show(sprite, height, Color.white, Mathf.Clamp01(b.Flash) * 0.5f);
                Vector3 at = TrackSpace.Point(b.S, b.X, 0f);
                bb.transform.position = at;

                var proxy = _proxies[used - 1];
                proxy.SetBoxSize(new Vector3(2.8f, height * 0.9f, 1.5f));
                proxy.Bind(b.Id, true);
                proxy.Move(at + Vector3.up * (height * 0.45f));
            }

            for (int i = used; i < _pool.Count; i++)
            {
                _pool[i].Hide();
                _proxies[i].Disable();
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>Destructible roadblocks: barrier, barrels, wreck. They stop the train dead.</summary>
    public sealed class BlockadeView : MonoBehaviour
    {
        readonly List<SpriteBillboard> _pool = new List<SpriteBillboard>();

        public void Build()
        {
            ArtSprites.Preload();
            for (int i = 0; i < 8; i++) Get(i);
        }

        SpriteBillboard Get(int i)
        {
            while (_pool.Count <= i) _pool.Add(SpriteBillboard.Create(transform, "Blockade" + _pool.Count));
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
                bb.transform.position = TrackSpace.Point(b.S, b.X, 0f);
            }

            for (int i = used; i < _pool.Count; i++) _pool[i].Hide();
        }
    }
}

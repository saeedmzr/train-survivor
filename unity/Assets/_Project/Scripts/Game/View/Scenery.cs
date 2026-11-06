using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Track-side dressing from the art sheet: crates and barrels down both sides, flags
    /// marking the stage's quarter points, and a warning sign before every blockade.
    /// </summary>
    public sealed class Scenery : MonoBehaviour
    {
        static readonly string[] Props = { "crate", "barrel_rust", "barrel_toxic", "crate", "barrel_rust" };

        readonly List<SpriteBillboard> _props = new List<SpriteBillboard>();
        readonly List<SpriteBillboard> _flags = new List<SpriteBillboard>();
        readonly List<SpriteBillboard> _warn = new List<SpriteBillboard>();

        public void Build()
        {
            ArtSprites.Preload();

            for (int i = 0; i < 64; i++)
            {
                var bb = SpriteBillboard.Create(transform, "Prop" + i);
                _props.Add(bb);

                float s = 14f + i * 13.5f;
                bool left = i % 2 == 0;
                float lateral = (left ? -1f : 1f) * (6.3f + (i % 3) * 0.6f);
                bb.Show(Props[i % Props.Length], 1.0f + (i % 3) * 0.1f, Color.white);
                bb.transform.position = TrackSpace.Point(s, lateral, 0f);
                // Static props still billboard; their rotation is set by SpriteBillboard.
            }

            for (int i = 0; i < 3; i++)
            {
                var bb = SpriteBillboard.Create(transform, "Milestone" + i);
                bb.Show(i == 1 ? "flag_red" : "flag_green", 2.3f, Color.white);
                _flags.Add(bb);
            }

            for (int i = 0; i < 4; i++)
            {
                var bb = SpriteBillboard.Create(transform, "Warn" + i);
                bb.Show("warn_0", 1.1f, Color.white);
                _warn.Add(bb);
            }
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World, Time.time);
        }

        public void Sync(World world, float time)
        {
            float len = world.StageLength;
            for (int i = 0; i < _flags.Count; i++)
            {
                float s = len * (0.25f + 0.25f * i);
                _flags[i].transform.position = TrackSpace.Point(s, 5.4f, 0f);
                _flags[i].SetLook(Color.white, 0f);
            }

            // A flashing warning banner sits just in front of each blockade, from 70 m out.
            int used = 0;
            for (int i = 0; i < world.BlockadeCount && used < _warn.Count; i++)
            {
                var b = world.Blockades[i];
                if (!b.Alive) continue;

                float gap = b.S - world.Train.S;
                if (gap < -2f || gap > 70f) continue;

                var w = _warn[used++];
                float blink = 0.6f + 0.4f * Mathf.Sin(time * 9f);
                w.Show("warn_0", 1.0f, new Color(1f, 1f, 1f, blink));
                w.transform.position = TrackSpace.Point(b.S - 5f, 0f, 2.6f);
            }
            for (int i = used; i < _warn.Count; i++) _warn[i].Hide();
        }
    }
}

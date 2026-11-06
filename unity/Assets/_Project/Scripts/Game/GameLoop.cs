using System;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Fixed 60 Hz simulation with a catch-up accumulator, decoupled from frame rate.
    /// Views read the world in LateUpdate so they always see a fully stepped state.
    /// </summary>
    public sealed class GameLoop : MonoBehaviour
    {
        public World World { get; private set; }
        public InputRouter Input { get; set; }
        public Action<SimEvent> SimEventRaised;

        /// <summary>Debug slow-mo hook for the juice pass. Does not change sim math.</summary>
        public float TimeScale = 1f;

        /// <summary>Dev freeze (tune.txt dev.pause=1): views keep drawing, sim does not step.
        /// Used to photograph a line-up or a telegraph without the horde closing in.</summary>
        public bool Paused;

        public float SimTime { get; private set; }
        public int StepsLastFrame { get; private set; }

        float _acc;

        void Awake()
        {
            World = new World();
        }

        void Update()
        {
            if (Paused) { StepsLastFrame = 0; return; }

            Intent intent = Input != null ? Input.Current : Intent.Stop;

            _acc += Mathf.Min(Time.deltaTime, 0.25f) * TimeScale;
            StepsLastFrame = 0;

            while (_acc >= SimConfig.Dt && StepsLastFrame < 3)
            {
                World.Advance(SimConfig.Dt, intent);
                _acc -= SimConfig.Dt;
                StepsLastFrame++;
                SimTime += SimConfig.Dt;

                if (SimEventRaised != null)
                {
                    for (int i = 0; i < World.Events.Count; i++) SimEventRaised(World.Events[i]);
                }
            }

            if (StepsLastFrame == 3) _acc = 0f;   // don't spiral after a hitch
        }
    }
}

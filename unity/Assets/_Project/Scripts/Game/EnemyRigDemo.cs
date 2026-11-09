using UnityEngine;
using TrainSurvivor.Game.View;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Dev-only side-by-side: spawns rigged walkers next to the frame-based ones so the two
    /// animation paths can be judged in the same screenshot. Enabled with <c>dev.rig=1</c>
    /// in tune.txt; <c>dev.rigMode=1</c> forces the CPU skinning fallback, 0 keeps 2D Animation.
    /// </summary>
    public sealed class EnemyRigDemo : MonoBehaviour
    {
        const int Count = 3;

        EnemyRig[] _rigs;
        float[] _offsetS;
        float[] _offsetX;
        Camera _cam;
        GameLoop _loop;
        bool _logged;

        void Start()
        {
            _loop = GetComponentInParent<GameLoop>() ?? FindAnyObjectByType<GameLoop>();
            _cam = Camera.main;

            var data = EnemyRigData.Of("walker");
            if (data == null || _loop == null || _loop.World == null)
            {
                Debug.LogWarning("[RigDemo] missing rig data or sim");
                enabled = false;
                return;
            }

            int mode = Mathf.RoundToInt(DevTuning.Get("dev.rigMode", 0f));
            if (mode != 0) Debug.LogWarning("[RigDemo] dev.rigMode is ignored: rigs render through CPU skinning");
            float spacing = DevTuning.Get("dev.rigSpacing", 3.4f);
            float ahead = DevTuning.Get("dev.rigAhead", 9f);

            _rigs = new EnemyRig[Count];
            _offsetS = new float[Count];
            _offsetX = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                _offsetS[i] = ahead + i * spacing + 1.5f;
                _offsetX[i] = (i - 1) * 2.6f;
                _rigs[i] = EnemyRig.Create(transform, data);
                if (_rigs[i] != null) _rigs[i].AmpScale = DevTuning.Get("dev.rigAmp", 1f);
            }
        }

        void LateUpdate()
        {
            if (_rigs == null || _loop == null || _loop.World == null) return;

            float trainS = _loop.World.Train.S;
            float speed = DevTuning.Get("dev.rigSpeed", 2.2f);

            for (int i = 0; i < _rigs.Length; i++)
            {
                var rig = _rigs[i];
                if (rig == null) continue;

                var pos = TrackSpace.Point(trainS + _offsetS[i], _offsetX[i], 0f);
                rig.transform.position = pos;
                rig.FaceCamera(_cam != null ? _cam.transform : null);
                rig.Tick(Time.deltaTime, speed, 0.4f, Color.white, 0f);
            }

            if (!_logged)
            {
                _logged = true;
                Debug.Log("[RigDemo] " + _rigs.Length + " rigs, bones=" + (_rigs[0] != null ? _rigs[0].BoneCount : 0));
            }
        }
    }
}

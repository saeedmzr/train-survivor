using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Keeps the key light aligned with the rails: the sun shines down the track rather than
    /// across it, so the line reads as a lit corridor and the sleepers catch a long shadow.
    /// Pitch stays fixed; yaw follows the track heading at the locomotive.
    /// </summary>
    public sealed class RailLight : MonoBehaviour
    {
        public Light Sun;
        [Tooltip("Elevation above the track, degrees")]
        public float Pitch = 34f;
        [Tooltip("Yaw offset from the track heading")]
        public float YawOffsetDeg = 0f;
        [Tooltip("How quickly the light swings when the track curves")]
        public float SlewDegPerSec = 45f;

        GameLoop _loop;

        void LateUpdate()
        {
            if (Sun == null) return;
            if (_loop == null) _loop = GetComponentInParent<GameLoop>();
            if (_loop == null || _loop.World == null) return;

            Quaternion want = TrackSpace.Facing(_loop.World.Train.S) *
                              Quaternion.Euler(Pitch, YawOffsetDeg, 0f);
            Sun.transform.rotation = Quaternion.RotateTowards(
                Sun.transform.rotation, want, SlewDegPerSec * Time.deltaTime);
        }
    }
}

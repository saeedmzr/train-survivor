using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>World-space conversion shared by every view. Sim speaks meters, Unity speaks Vector3.</summary>
    public static class TrackSpace
    {
        public static Vector3 Point(float s, float lateral, float y = 0f)
        {
            TrackPath.Point(s, lateral, out float x, out float z);
            return new Vector3(x, y, z);
        }

        public static Vector3 Right(float s)
        {
            TrackPath.Right(s, out float rx, out float rz);
            return new Vector3(rx, 0f, rz);
        }

        public static Quaternion Facing(float s, float yawOffsetDeg = 0f)
        {
            return Quaternion.Euler(0f, TrackPath.HeadingDegrees(s) + yawOffsetDeg, 0f);
        }
    }
}

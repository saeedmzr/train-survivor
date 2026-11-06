using System;

namespace TrainSurvivor.Core
{
    /// <summary>
    /// The rail centerline as pure math, so sim and view agree without an engine dependency.
    /// A gentle sine gives the camera something to swing around. Forks will later become
    /// piecewise segments with their own lateral offsets; the sampling API stays the same.
    /// </summary>
    public static class TrackPath
    {
        public const float Length = 900f;
        public const float CurveAmplitude = 14f;
        public const float CurveWavelength = 160f;

        const float TwoPi = (float)(Math.PI * 2.0);

        /// <summary>Center of the rail at distance <paramref name="s"/> (meters from start).</summary>
        public static void Center(float s, out float x, out float z)
        {
            x = CurveAmplitude * (float)Math.Sin(s / CurveWavelength * TwoPi);
            z = s;
        }

        /// <summary>Unit vector pointing right of travel, for lateral offsets.</summary>
        public static void Right(float s, out float rx, out float rz)
        {
            float dx = CurveAmplitude * (TwoPi / CurveWavelength) * (float)Math.Cos(s / CurveWavelength * TwoPi);
            float inv = 1f / (float)Math.Sqrt(dx * dx + 1f);
            float tx = dx * inv;   // tangent (tx, tz), normalized
            float tz = inv;
            rx = tz;               // right = perpendicular, pointing +x at s=0
            rz = -tx;
        }

        /// <summary>World XZ for a point on the track at distance s and lateral offset.</summary>
        public static void Point(float s, float lateral, out float x, out float z)
        {
            Center(s, out float cx, out float cz);
            Right(s, out float rx, out float rz);
            x = cx + rx * lateral;
            z = cz + rz * lateral;
        }

        /// <summary>Yaw in degrees for something facing along the track.</summary>
        public static float HeadingDegrees(float s)
        {
            float dx = CurveAmplitude * (TwoPi / CurveWavelength) * (float)Math.Cos(s / CurveWavelength * TwoPi);
            return (float)(Math.Atan2(dx, 1.0) * 180.0 / Math.PI);
        }
    }
}

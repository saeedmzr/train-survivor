using UnityEngine;

namespace TrainSurvivor.Game.Audio
{
    /// <summary>
    /// Master-bus safety limiter. The 2D pool can stack a shot, two swings, a death and the
    /// engine in the same buffer; measured peaks reached 1.055 (hard clip). This sits on the
    /// AudioListener and soft-knees anything above 0.92 into a tanh curve, so the mix keeps
    /// its level and can never exceed full scale. Allocation-free, one branch per sample.
    /// </summary>
    public sealed class AudioLimiter : MonoBehaviour
    {
        [Range(0.5f, 0.999f)] public float threshold = 0.92f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            float th = Mathf.Clamp(threshold, 0.5f, 0.999f);
            float inv = 1f - th;
            for (int i = 0; i < data.Length; i++)
            {
                float x = data[i];
                float a = x < 0f ? -x : x;
                if (a <= th) continue;
                float over = (a - th) / inv;          // 0..1
                float y = th + inv * (float)System.Math.Tanh(over);
                data[i] = x < 0f ? -y : y;
            }
        }
    }
}

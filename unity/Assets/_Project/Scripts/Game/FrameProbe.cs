using System;
using UnityEngine;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Bounded frame-time diagnostic, enabled only by dev.frameprobe=1 in tune.txt. Samples
    /// Time.unscaledDeltaTime after a warmup, then logs mean/median/p95/p99 once and disables
    /// itself. Inert in normal play: without the tune key it is never added.
    /// </summary>
    public sealed class FrameProbe : MonoBehaviour
    {
        public float warmup = 6f;
        public float window = 15f;

        float _start;
        float[] _samples;
        int _n;

        public static void AttachIfRequested(GameObject host)
        {
            if (DevTuning.Get("dev.frameprobe", 0f) < 0.5f) return;
            var probe = host.AddComponent<FrameProbe>();
            probe.warmup = DevTuning.Get("dev.frameprobe.warmup", probe.warmup);
            probe.window = DevTuning.Get("dev.frameprobe.window", probe.window);
        }

        void Awake()
        {
            _start = Time.unscaledTime;
            _samples = new float[Mathf.CeilToInt(window * 240f) + 16];
            Debug.Log("[perf] frame probe armed");
        }

        void Update()
        {
            float t = Time.unscaledTime - _start;
            if (t < warmup) return;
            if (t > warmup + window || _n >= _samples.Length)
            {
                Report();
                enabled = false;
                return;
            }
            _samples[_n++] = Time.unscaledDeltaTime;
        }

        void Report()
        {
            if (_n < 10) { Debug.Log("[perf] probe: not enough samples"); return; }
            var arr = new float[_n];
            Array.Copy(_samples, arr, _n);
            Array.Sort(arr);
            double sum = 0;
            for (int i = 0; i < _n; i++) sum += arr[i];
            float mean = (float)(sum / _n);
            float median = arr[_n / 2];
            float p95 = arr[Mathf.Clamp((int)(_n * 0.95f), 0, _n - 1)];
            float p99 = arr[Mathf.Clamp((int)(_n * 0.99f), 0, _n - 1)];
            float max = arr[_n - 1];
            Debug.Log(string.Format(
                "[perf] frames {0} mean {1:0.00} ms ({2:0.0} fps) median {3:0.00} p95 {4:0.00} p99 {5:0.00} max {6:0.00}",
                _n, mean * 1000f, mean > 0f ? 1f / mean : 0f, median * 1000f,
                p95 * 1000f, p99 * 1000f, max * 1000f));
        }
    }
}

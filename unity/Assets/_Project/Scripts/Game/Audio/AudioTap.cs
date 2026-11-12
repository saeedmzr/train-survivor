using System.IO;
using UnityEngine;

namespace TrainSurvivor.Game.Audio
{
    /// <summary>
    /// Diagnostic tap on the AudioListener. OnAudioFilterRead on the listener GameObject sees the
    /// final mixed output on the audio thread, which is the reliable place to prove that samples
    /// exist (AudioListener.GetOutputData is only a recent-sample tap). No logging or disk I/O
    /// happens on the audio thread: samples go into a preallocated buffer and the main thread
    /// writes the WAV afterwards.
    /// </summary>
    public sealed class AudioTap : MonoBehaviour
    {
        public int maxSeconds = 12;

        float[] _buf;
        int _n;
        int _rate = 48000;
        int _ch;

        public int Callbacks;
        public float Peak;
        public double SumSq;
        public long Frames;
        public bool Ready => _buf != null && _n >= _buf.Length;

        // AudioSettings.outputSampleRate is a main-thread-only call; reading it from
        // OnAudioFilterRead throws UnityException on the audio thread and killed the capture.
        void Awake() { _rate = Mathf.Max(8000, AudioSettings.outputSampleRate); }

        public int Channels => _ch;
        public int SampleRate => _rate;
        public float Rms => Frames > 0 ? Mathf.Sqrt((float)(SumSq / Frames)) : 0f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (_buf == null)
            {
                _ch = Mathf.Max(1, channels);
                _buf = new float[_rate * maxSeconds * _ch];
            }

            Callbacks++;
            for (int i = 0; i + _ch <= data.Length; i += _ch)
            {
                for (int c = 0; c < _ch; c++)
                {
                    float v = data[i + c];
                    if (v < 0f) v = -v;
                    if (v > Peak) Peak = v;
                }
                for (int c = 0; c < _ch; c++)
                {
                    float v = data[i + c];
                    SumSq += v * v;
                }
                Frames += _ch;
                if (_n + _ch <= _buf.Length)
                {
                    for (int c = 0; c < _ch; c++) _buf[_n++] = data[i + c];
                }
            }
        }

        public bool WriteWav(string path)
        {
            if (_buf == null || _n < _ch * 100) return false;
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                int bytes = _n * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)_ch); w.Write(_rate);
                w.Write(_rate * _ch * 2); w.Write((short)(_ch * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                for (int i = 0; i < _n; i++)
                    w.Write((short)Mathf.Clamp(_buf[i] * 32767f, -32768f, 32767f));
            }
            return true;
        }
    }
}

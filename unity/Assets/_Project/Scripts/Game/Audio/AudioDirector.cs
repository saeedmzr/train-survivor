using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.Audio
{
    /// <summary>
    /// The horde's voice. Every species owns a synthesised clip (tools/make_enemy_sfx.py);
    /// this plays them on sim events, keeps a low growl bed running, and gives each kill a
    /// pitched death squelch so a wave never sounds like one sample on repeat.
    /// </summary>
    public sealed class AudioDirector : MonoBehaviour
    {
        const int Voices = 16;

        /// <summary>The live director, for view-side cues (footsteps, swings, UI confirm).</summary>
        public static AudioDirector Instance { get; private set; }

        /// <summary>Token bucket so a horde cannot turn cues into a wall of noise: a steady
        /// rate plus a small burst. All cues still play through the same 2D pool.</summary>
        sealed class RateGate
        {
            readonly float _rate, _burst;
            float _tokens, _last;

            public RateGate(float rate, float burst)
            {
                _rate = rate;
                _burst = burst;
                _tokens = burst;
                _last = Time.time;
            }

            public bool Take()
            {
                float now = Time.time;
                _tokens = Mathf.Min(_burst, _tokens + (now - _last) * _rate);
                _last = now;
                if (_tokens < 1f) return false;
                _tokens -= 1f;
                return true;
            }
        }

        readonly RateGate _fleshGate = new RateGate(10f, 3f);
        readonly RateGate _hurtGate = new RateGate(5f, 2f);
        readonly RateGate _stepGate = new RateGate(9f, 3f);
        readonly RateGate _swingGate = new RateGate(5f, 2f);

        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        GameLoop _loop;
        int _next;

        float _ambientTimer;
        float _master = 0.75f;

        [Tooltip("Volume of one machine-gun shot")]
        [Range(0f, 1f)] public float shotVolume = 0.30f;
        [Tooltip("Minimum gap between shot sounds — 12 guns at 8/s would otherwise machine-gun the mixer")]
        public float shotMinGap = 0.045f;
        float _lastShotAt = -1f;

        /// <summary>tune.txt dev.audiolog=1 logs every triggered clip. The emulator runs with
        /// -no-audio, so this log is the only way to see the mix fire on device.</summary>
        bool _verbose;
        AudioSource _engine;
        int _shotsAccepted;
        float _diagTimer;
        AudioTap _tap;

        public static AudioDirector Create(Transform parent, GameLoop loop)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioDirector>();
            a._loop = loop;
            a.Build();
            Instance = a;
            return a;
        }

        void Build()
        {
            // No AudioListener existed anywhere in the scene, so nothing was ever rendered to
            // the device. Own one here (2D mix) unless a camera already provides it.
            bool diag = DevTuning.Get("dev.audiodiag", 0f) > 0.5f;
            if (diag)
            {
                var listenerGo = new GameObject("AudioListener+tap");
                listenerGo.transform.SetParent(transform, false);
                listenerGo.AddComponent<AudioListener>();
                _tap = listenerGo.AddComponent<AudioTap>();
                Debug.Log("[audio] diagnostic listener+tap created");
            }
            else if (Object.FindObjectOfType<AudioListener>() == null)
            {
                gameObject.AddComponent<AudioListener>();
                Debug.Log("[audio] added missing AudioListener");
            }

            for (int i = 0; i < Voices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;          // 2D: the mix is the space
                src.rolloffMode = AudioRolloffMode.Linear;
                _pool.Add(src);
            }

            foreach (var kind in (EnemyKind[])System.Enum.GetValues(typeof(EnemyKind)))
                Load(EnemyVoice(kind));

            Load("fx_shot");
            Load("fx_hit");
            Load("fx_swing");
            Load("fx_step");
            Load("fx_collapse");
            Load("vo_walker_hurt");
            Load("fx_death");
            Load("fx_blast");
            Load("fx_brake");
            Load("fx_ram");
            Load("fx_metal");
            Load("pickup_xp");
            Load("ui_levelup");
            Load("ui_clear");
            Load("ui_defeat");
            Load("ui_click");
            Load("ui_confirm");
            Load("fx_engine");

            // Locomotive bed: a looping source whose pitch/volume follow the throttle.
            var engine = gameObject.AddComponent<AudioSource>();
            engine.playOnAwake = false;
            engine.loop = true;
            engine.spatialBlend = 0f;
            engine.volume = 0f;
            engine.clip = _clips.ContainsKey("fx_engine") ? _clips["fx_engine"] : null;
            if (engine.clip != null) engine.Play();
            _engine = engine;

            _verbose = DevTuning.Get("dev.audiolog", 0f) > 0.5f;
            if (diag)
            {
                StartCoroutine(DiagLoop());
                StartCoroutine(CaptureMix());
                if (DevTuning.Get("dev.audiotone", 0f) > 0.5f) StartCoroutine(TestTone());
            }
            Debug.Log($"[Audio] {_clips.Count} clips loaded, verbose={_verbose}");
        }

        void Load(string name)
        {
            if (_clips.ContainsKey(name)) return;
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
            {
                Debug.LogWarning("[Audio] missing Resources/Audio/" + name + ".wav");
                return;
            }
            _clips[name] = clip;
        }

        public static string EnemyVoice(EnemyKind kind) => "en_" + kind.ToString().ToLowerInvariant();

        // ---------------------------------------------------------------- playback

        void Play(string name, float volume = 1f, float pitch = 1f)
        {
            if (!_clips.TryGetValue(name, out var clip)) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.Stop();
            src.clip = clip;
            src.volume = Mathf.Clamp01(volume * _master);
            src.pitch = Mathf.Clamp(pitch, 0.55f, 1.9f);
            src.Play();
            if (_verbose) Debug.Log($"[Audio] play {name} vol={src.volume:F2} pitch={src.pitch:F2}");
        }

        float PitchJitter() => 1f + Random.Range(-0.07f, 0.07f);

        /// <summary>0 at 40 m from the train, 1 at the hull. Volume shaping only: the pool is
        /// 2D, so nothing ever fades out of the listener entirely.</summary>
        float Closeness(float s)
        {
            var w = _loop != null ? _loop.World : null;
            if (w == null) return 1f;
            return Mathf.Clamp01(1f - Mathf.Abs(s - w.Train.S) / 40f);
        }

        // ---------------------------------------------------------------- view cues

        /// <summary>One walker boot on the ground, driven by measured stride; rate-limited.</summary>
        public void WalkerFootstep(float closeness)
        {
            if (closeness <= 0.02f || !_stepGate.Take()) return;
            Play("fx_step", Mathf.Lerp(0.10f, 0.30f, Mathf.Clamp01(closeness)), PitchJitter());
        }

        /// <summary>One attack swing, fired when the Walker attack clip actually starts.</summary>
        public void WalkerSwing(float closeness)
        {
            if (closeness <= 0.02f || !_swingGate.Take()) return;
            Play("fx_swing", Mathf.Lerp(0.10f, 0.30f, Mathf.Clamp01(closeness)), PitchJitter());
        }

        /// <summary>Retry / dialog confirmation.</summary>
        public void UiConfirm() => Play("ui_confirm", 0.5f, 1f);


        /// <summary>
        /// One machine-gun report. Throttled: many guns firing in the same frame collapse into a
        /// single louder sample instead of stacking into clipping.
        /// </summary>
        void PlayShot()
        {
            float now = Time.time;
            if (now - _lastShotAt < shotMinGap) return;
            _lastShotAt = now;
            Play("fx_shot", Mathf.Clamp01(shotVolume), PitchJitter() * 1.02f);
        }

        // ---------------------------------------------------------------- sim events

        public void Handle(SimEvent e)
        {
            switch (e.Kind)
            {
                case SimEventKind.Kill:
                    Play("fx_death", 0.55f, PitchJitter());
                    // the sim carries the species: the enemy is gone from the world by now
                    Play(EnemyVoice(e.EnemyKind), 0.5f, PitchJitter() * 1.06f);
                    break;
                case SimEventKind.Shot:
                    _shotsAccepted++;
                    PlayShot();
                    break;
                case SimEventKind.ContactHit:
                    // The train itself being struck: metal hull, not flesh.
                    Play("fx_metal", 0.42f, PitchJitter() * 0.9f);
                    break;
                case SimEventKind.EnemyHit:
                {
                    // A player round connecting with a body: flesh impact plus a restrained
                    // walker grunt. Both rate-limited; metal stays on train/barricade hits.
                    float close = Closeness(e.ToS);
                    if (_fleshGate.Take())
                        Play("fx_hit", Mathf.Lerp(0.28f, 0.52f, close), PitchJitter());
                    if (e.EnemyKind == EnemyKind.Walker && _hurtGate.Take())
                        Play("vo_walker_hurt", Mathf.Lerp(0.10f, 0.22f, close),
                             PitchJitter() * 0.92f);
                    break;
                }
                case SimEventKind.Ram:
                    Play("fx_ram", 0.9f, PitchJitter());
                    break;
                case SimEventKind.Blast:
                    Play("fx_blast", 1f, PitchJitter());
                    break;
                case SimEventKind.BlockadeBroken:
                    // Collapse/debris, not the bloater blast: timber cracks and patter.
                    Play("fx_collapse", 0.85f, PitchJitter());
                    break;
                case SimEventKind.BlockadeHit:
                    Play("fx_metal", 0.5f, PitchJitter());
                    break;
                case SimEventKind.PickupTaken:
                    Play("pickup_xp", 0.35f, PitchJitter() * 1.1f);
                    break;
                case SimEventKind.LevelUp:
                    Play("ui_levelup", 0.6f, 1f);
                    break;
                case SimEventKind.Cleared:
                    Play("ui_clear", 0.8f, 1f);
                    break;
                case SimEventKind.Died:
                    Play("ui_defeat", 0.9f, 1f);
                    break;
                case SimEventKind.CardChosen:
                    Play("ui_click", 0.55f, 1f);
                    break;
                case SimEventKind.Brake:
                    Play("fx_brake", 0.8f, PitchJitter());
                    break;
            }
        }

        // ---------------------------------------------------------------- ambient bed

        // ---- bounded audio diagnostics (dev.audiodiag=1), inert otherwise -------------
        IEnumerator DiagLoop()
        {
            yield return new WaitForSeconds(2f);
            var listeners = Object.FindObjectsOfType<AudioListener>();
            int enabled = 0;
            foreach (var l in listeners) if (l.isActiveAndEnabled) enabled++;
            int playing = 0;
            foreach (var src in _pool) if (src.isPlaying) playing++;
            Debug.Log(string.Format(
                "[audio] diag listeners {0}/{1} listenerVol {2:0.00} pause {3} master {4:0.00} shots {5} playing {6}/{7} engine {8:0.00} dsp {9}",
                enabled, listeners.Length, AudioListener.volume, AudioListener.pause,
                _master, _shotsAccepted, playing, _pool.Count,
                _engine != null ? _engine.volume : -1f, AudioSettings.dspTime > 0f));
            if (_tap != null)
                Debug.Log(string.Format("[audio] tap cb {0} peak {1:0.0000} rms {2:0.0000}",
                    _tap.Callbacks, _tap.Peak, _tap.Rms));
            foreach (var kv in _clips)
            {
                var c = kv.Value;
                if (c.loadState == AudioDataLoadState.Unloaded) continue;
                var data = new float[Mathf.Min(c.samples, 4096) * Mathf.Max(1, c.channels)];
                c.GetData(data, 0);
                float peak = 0f;
                for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                if (peak <= 0.0001f && c.loadState != AudioDataLoadState.Loaded)
                    Debug.Log("[audio] clip issue " + kv.Key + " state " + c.loadState);
            }
            _diagTimer = 0f;
        }

        IEnumerator CaptureMix()
        {
            yield return new WaitForSeconds(8f);

            // recent-sample tap, for comparison only
            var probe = new float[1024];
            AudioListener.GetOutputData(probe, 0);
            float probePeak = 0f;
            for (int i = 0; i < probe.Length; i++) probePeak = Mathf.Max(probePeak, Mathf.Abs(probe[i]));

            yield return new WaitForSeconds(0.1f);
            Debug.Log(string.Format(
                "[audio] tap callbacks {0} ch {1} rate {2} peak {3:0.0000} rms {4:0.0000} frames {5} | GetOutputData peak {6:0.0000}",
                _tap != null ? _tap.Callbacks : -1, _tap != null ? _tap.Channels : 0,
                _tap != null ? _tap.SampleRate : 0, _tap != null ? _tap.Peak : 0f,
                _tap != null ? _tap.Rms : 0f, _tap != null ? _tap.Frames : 0, probePeak));

            string path = Path.Combine(Application.persistentDataPath, "audiocap.wav");
            try
            {
                bool ok = _tap != null && _tap.WriteWav(path);
                Debug.Log("[audio] wrote " + path + " ok " + ok);
            }
            catch (System.Exception e) { Debug.LogWarning("[audio] capture failed: " + e.Message); }
        }

        IEnumerator TestTone()
        {
            yield return new WaitForSeconds(1f);
            int rate = 48000;
            const int len = 48000;
            var data = new float[len];
            for (int i = 0; i < len; i++)
                data[i] = Mathf.Sin(2f * Mathf.PI * 440f * i / rate) * 0.35f;
            var clip = AudioClip.Create("diag_tone", len, 1, rate, false);
            clip.SetData(data, 0);

            for (int k = 0; k < 3; k++)
            {
                var go = new GameObject("DiagTone");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.volume = 0.35f * _master;
                src.spatialBlend = 0f;
                src.Play();
                Debug.Log("[audio] diagnostic tone playing");
                yield return new WaitForSeconds(1.2f);
            }
        }

        void LateUpdate()
        {
            var w = _loop != null ? _loop.World : null;
            if (w == null) return;

            if (_engine != null)
            {
                float k = Mathf.Clamp01(w.Train.Speed / SimConfig.MoveSpeed);
                // Master gain applies to the bed too, including idle. Base is chosen so the
                // audible idle with master 0.75 stays at the old 0.05 absolute.
                _engine.volume = w.Train.Mode == TrainMode.Dead ? 0f
                                                               : (0.065f + 0.21f * k) * _master;
                _engine.pitch = 0.85f + 0.4f * k;
            }

            _ambientTimer -= Time.deltaTime;
            if (_ambientTimer > 0f) return;

            // one growl every 1.4–3.2 s, from a random living enemy ahead of the train
            _ambientTimer = Random.Range(1.4f, 3.2f);

            int alive = 0;
            for (int i = 0; i < w.EnemyCount; i++) if (w.Enemies[i].Alive) alive++;
            if (alive == 0) return;

            int pick = Random.Range(0, alive);
            for (int i = 0; i < w.EnemyCount; i++)
            {
                if (!w.Enemies[i].Alive) continue;
                if (pick-- > 0) continue;

                float gap = w.Enemies[i].S - w.Train.S;
                if (gap < 0f || gap > 55f) return;

                float closeness = Mathf.Clamp01(1f - gap / 55f);
                Play(EnemyVoice(w.Enemies[i].Kind), 0.18f + closeness * 0.30f, PitchJitter());
                return;
            }
        }
    }
}

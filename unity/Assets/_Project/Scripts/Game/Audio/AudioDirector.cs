using System.Collections.Generic;
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

        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        GameLoop _loop;
        int _next;

        float _ambientTimer;
        float _master = 0.75f;

        /// <summary>tune.txt dev.audiolog=1 logs every triggered clip. The emulator runs with
        /// -no-audio, so this log is the only way to see the mix fire on device.</summary>
        bool _verbose;

        public static AudioDirector Create(Transform parent, GameLoop loop)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioDirector>();
            a._loop = loop;
            a.Build();
            return a;
        }

        void Build()
        {
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

            Load("fx_hit");
            Load("fx_death");
            Load("fx_blast");
            Load("fx_brake");
            Load("fx_ram");

            _verbose = DevTuning.Get("dev.audiolog", 0f) > 0.5f;
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
                case SimEventKind.ContactHit:
                    Play("fx_hit", 0.7f, PitchJitter());
                    break;
                case SimEventKind.Ram:
                    Play("fx_ram", 0.9f, PitchJitter());
                    break;
                case SimEventKind.Blast:
                    Play("fx_blast", 1f, PitchJitter());
                    break;
                case SimEventKind.BlockadeBroken:
                    Play("fx_blast", 0.85f, PitchJitter());
                    break;
                case SimEventKind.Brake:
                    Play("fx_brake", 0.8f, PitchJitter());
                    break;
            }
        }

        // ---------------------------------------------------------------- ambient bed

        void LateUpdate()
        {
            var w = _loop != null ? _loop.World : null;
            if (w == null) return;

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

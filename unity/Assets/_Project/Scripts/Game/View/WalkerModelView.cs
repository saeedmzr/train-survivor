using System.Collections.Generic;
using UnityEngine;
using TrainSurvivor.Core;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Draws walker enemies with the finalized 3D model (Resources/Models/walker3d): one pooled
    /// instance per live walker, legacy clips Walk / Attack / Death. Death is triggered by the sim's
    /// Kill event — the same channel Fx and audio listen to — so it fires exactly when a walker dies.
    /// </summary>
    public sealed class WalkerModelView : MonoBehaviour
    {
        public static WalkerModelView Instance { get; private set; }

        const string ModelResource = "Models/walker3d";
        const string MaterialResource = "Models/WalkerMat";
        const float TargetHeight = 4.0f;      // twice the source height, as asked
        const int PoolSize = 34;      // 30 live walkers can be on the field at once
        const int CorpseReserve = 4;  // bodies kept back so a death always has one to play on
        const float AttackRange = 5.5f;    // SimConfig.ContactRange
        // measured in Blender: the shamble covers 1.35 m of ground per second at playback 1.0
        // and 1x model scale (32 steps over 9.96 s, ~0.42 m each); scaling the model scales it
        const float WalkGroundSpeedUnit = 1.35f;

        [Tooltip("Yaw offset on top of facing the train, to correct the model's authored forward axis")]
        public float facingYaw = 0f;

        [Tooltip("Scale multiplier on top of the automatic 2 m fit")]
        public float scaleBias = 1f;

        GameObject _prefab;
        Material _mat;
        AnimationClip _walk, _attack, _death;
        float _walkLen = 1.2f, _attackLen = 1.2f, _deathLen = 2.5f;
        float _walkGroundSpeed = 2.7f;
        float _fitScale = 1f;

        struct Death { public float S, X; }
        readonly List<Death> _pending = new List<Death>();

        sealed class Body
        {
            public GameObject Go;
            public Animation Anim;
            public int EnemyId = -1;
            public float FreeAt;
            public string Playing;
        }

        readonly List<Body> _pool = new List<Body>();
        readonly List<AnimationClip> _clips = new List<AnimationClip>();
        int _diag;
        float _diagAt;

        public bool Active => _prefab != null && _walk != null;

        void Awake()
        {
            Instance = this;
            _prefab = Resources.Load<GameObject>(ModelResource);
            if (_prefab == null)
            {
                Debug.LogWarning("[walker3d] Resources/" + ModelResource + " not found — sprites stay");
                return;
            }

            foreach (var c in Resources.LoadAll<AnimationClip>("Models"))
            {
                if (c.name == "Walk") _walk = c;
                else if (c.name == "Attack") _attack = c;
                else if (c.name == "Death") _death = c;
                _clips.Add(c);
            }
            _walkLen = _walk != null ? Mathf.Max(0.3f, _walk.length) : 1.2f;
            _attackLen = _attack != null ? Mathf.Max(0.3f, _attack.length) : 1.2f;
            _deathLen = _death != null ? Mathf.Max(0.5f, _death.length) : 2.5f;
            _mat = Resources.Load<Material>(MaterialResource);
            _walkGroundSpeed = WalkGroundSpeedUnit * (TargetHeight / 2f);
            // dev.face flips the model forward axis without a rebuild
            facingYaw = TrainSurvivor.Game.DevTuning.Get("dev.face", facingYaw);

            var probe = Instantiate(_prefab, transform);
            probe.name = "Walker3D_probe";
            _fitScale = ComputeFit(probe);
            Destroy(probe);

            Debug.Log(string.Format(
                "[walker3d] ready: walk {0:0.00}s attack {1:0.00}s death {2:0.00}s fit {3:0.00} clips {4}",
                _walkLen, _attackLen, _deathLen, _fitScale, _clips.Count));

            for (int i = 0; i < PoolSize; i++) _pool.Add(Spawn(i));
        }

        void OnEnable()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop != null) loop.SimEventRaised += OnSimEvent;
        }

        void OnDisable()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop != null) loop.SimEventRaised -= OnSimEvent;
        }

        void OnSimEvent(SimEvent ev)
        {
            if (ev.Kind != SimEventKind.Kill || ev.EnemyKind != EnemyKind.Walker) return;
            _pending.Add(new Death { S = ev.ToS, X = ev.ToX });
            if (_diag < 5)
            {
                _diag++;
                Debug.Log(string.Format("[walker3d] death clip at S {0:0.0} X {1:0.0} (queued {2})",
                    ev.ToS, ev.ToX, _pending.Count));
            }
        }

        float ComputeFit(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float h = b.size.y;
            return h > 0.01f ? TargetHeight / h : 1f;
        }

        Body Spawn(int index)
        {
            var go = Instantiate(_prefab, transform);
            go.name = "Walker3D_" + index;
            go.transform.localScale = Vector3.one * (_fitScale * scaleBias);

            if (_mat != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = _mat;

            var anim = go.GetComponent<Animation>();
            if (anim == null) anim = go.AddComponent<Animation>();
            anim.playAutomatically = false;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            foreach (var c in _clips) anim.AddClip(c, c.name);

            go.SetActive(false);
            return new Body { Go = go, Anim = anim };
        }

        void Play(Body b, AnimationClip clip, bool loop, float speed = 1f)
        {
            if (clip == null || b.Anim == null) return;
            b.Playing = clip.name;
            var state = b.Anim[clip.name];
            if (state != null) state.speed = speed;
            b.Anim.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            b.Anim.Play(clip.name);
            b.Go.SetActive(true);
        }

        Quaternion FaceTrain(Vector3 from, float trainS)
        {
            Vector3 target = TrackSpace.Point(trainS, 0f, 0f);
            Vector3 dir = target - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return Quaternion.Euler(0f, facingYaw, 0f);
            return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, facingYaw, 0f);
        }

        void LateUpdate()
        {
            var loop = GetComponentInParent<GameLoop>();
            if (loop == null || loop.World == null) return;
            Sync(loop.World);
        }

        public void Sync(World world)
        {
            if (!Active || world == null) return;

            float now = Time.time;
            float trainS = world.Train.S;
            int used = 0;

            if (_diag < 3 && now > _diagAt)
            {
                _diag++;
                _diagAt = now + 2f;
                int alive = 0;
                for (int i = 0; i < world.EnemyCount; i++) if (world.Enemies[i].Alive) alive++;
                Vector3 p0 = alive > 0 ? TrackSpace.Point(world.Enemies[0].S, world.Enemies[0].X, 0f) : Vector3.zero;
                Debug.Log(string.Format("[walker3d] sync alive {0} pool {1} firstPos {2} camS {3:0.0}",
                    alive, _pool.Count, p0, trainS));
            }

            for (int i = 0; i < world.EnemyCount; i++)
            {
                Enemy e = world.Enemies[i];
                if (!e.Alive || e.Kind != EnemyKind.Walker) continue;
                if (used >= _pool.Count - CorpseReserve) break;

                Body body = _pool[used++];
                Vector3 pos = TrackSpace.Point(e.S, e.X, 0f);
                body.Go.transform.position = pos;
                body.Go.transform.rotation = FaceTrain(pos, trainS);

                if (body.EnemyId != e.Id)
                {
                    body.EnemyId = e.Id;
                    body.FreeAt = 0f;
                    Play(body, _walk, true);
                }

                // true distance to the train, not just along the rails: they now close
                // in from the side, so the shot/attack range has to be 2D
                float dS = e.S - trainS;
                bool attacking = Mathf.Sqrt(dS * dS + e.X * e.X) <= AttackRange;

                if (attacking && _attack != null && body.Playing != "Attack")
                {
                    body.FreeAt = now + _attackLen;
                    Play(body, _attack, false);
                }
                else if (!attacking && body.Playing == "Attack" && now > body.FreeAt)
                {
                    Play(body, _walk, true);
                }
                else if (body.Playing == "Walk")
                {
                    var st = body.Anim[_walk.name];
                    // playback follows the ground speed, so the planted foot does not slide
                    if (st != null) st.speed = Mathf.Clamp(e.Speed / _walkGroundSpeed, 0.3f, 3f);
                }
            }

            // deaths queued from the sim's Kill event
            for (int d = 0; d < _pending.Count; d++)
            {
                if (used >= _pool.Count) break;
                Body body = _pool[used++];
                body.EnemyId = -1;
                Vector3 pos = TrackSpace.Point(_pending[d].S, _pending[d].X, 0f);
                body.Go.transform.position = pos;
                body.Go.transform.rotation = FaceTrain(pos, trainS);
                body.FreeAt = now + _deathLen;
                Play(body, _death, false);
            }
            _pending.Clear();

            for (int i = used; i < _pool.Count; i++)
            {
                Body b = _pool[i];
                bool holdingCorpse = b.Playing == "Death" && now < b.FreeAt;
                if (!holdingCorpse)
                {
                    if (b.Go.activeSelf) b.Go.SetActive(false);
                    b.EnemyId = -1;
                    b.Playing = null;
                }
            }
        }
    }
}

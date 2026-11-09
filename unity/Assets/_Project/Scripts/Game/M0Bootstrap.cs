using UnityEngine;
using UnityEngine.Rendering.Universal;
using TrainSurvivor.Core;
using TrainSurvivor.Game.Audio;
using TrainSurvivor.Game.UI;
using TrainSurvivor.Game.View;

namespace TrainSurvivor.Game
{
    /// <summary>
    /// Assembles M0 from code so the whole scene is reviewable in git instead of binary YAML.
    /// Everything lives under this object, which owns the GameLoop.
    /// </summary>
    public sealed class M0Bootstrap : MonoBehaviour
    {
        public GameLoop Loop { get; private set; }
        public TrainRig Train { get; private set; }
        public EnemyViewPool Enemies { get; private set; }
        public Effects Fx { get; private set; }
        public AudioDirector Audio { get; private set; }
        public Camera Cam { get; private set; }
        void Awake()
        {
            // AppUI (the Unity 6 player activity) enables EnhancedTouch on mobile before the
            // input update type is set; InputSystem 1.20 then builds Finger histories with
            // InputUpdateType.None at native device discovery. We read Touchscreen.current /
            // Mouse.current directly and never use EnhancedTouch, so turn that layer off.
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            DevTuning.Load();
            FrameProbe.AttachIfRequested(gameObject);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.075f, 0.095f, 0.135f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 220f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.16f, 0.17f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.05f, 0.06f);

            BuildLighting();
            BuildTrack();
            BuildSim();
            BuildTrain();
            BuildEnemies();
            BuildCamera();
            BuildFx();
            BuildProjectiles();
            BuildWorldArt();
            BuildHud();

            Loop.SimEventRaised += Fx.Handle;
            Audio = AudioDirector.Create(transform, Loop);
            Loop.SimEventRaised += Audio.Handle;
            ApplyDevTweaks();
        }

        /// <summary>
        /// Dev-only shortcuts driven by tune.txt, so states that take minutes to reach
        /// (level-up cards, a blockade, a death screen) can be inspected in seconds.
        /// </summary>
        void ApplyDevTweaks()
        {
            if (DevTuning.Get("dev.diag", 0f) > 0f) gameObject.AddComponent<DevDiag>();
            if (DevTuning.Get("dev.rig", 0f) > 0.5f) gameObject.AddComponent<EnemyRigDemo>();
            // dev.models=1 (default) draws walkers with the finalized 3D model instead of sprites
            if (DevTuning.Get("dev.models", 1f) > 0.5f)
            {
                gameObject.AddComponent<WalkerModelView>();
                // while the walker is the only modelled species the sprite pool is dead weight,
                // and leaving it on draws billboards on top of the 3D bodies
                if (Enemies != null) Enemies.enabled = false;
            }

            if (Loop == null || Loop.World == null) return;
            var w = Loop.World;
            w.DemoKills = DevTuning.Get("dev.kills", 0f) > 0.5f;


            // dev.timeScale=0.2 runs the whole game in slow motion, sim and view alike: the
            // only way to see a 0.12 s shot sequence on a device that screenshots at ~3 fps.
            float timeScale = DevTuning.Get("dev.timeScale", 1f);
            if (!Mathf.Approximately(timeScale, 1f)) Time.timeScale = Mathf.Max(0.02f, timeScale);

            float levelUps = DevTuning.Get("dev.levelup", 0f);
            if (levelUps > 0f) w.GrantXp(w.Train.XpToNext * levelUps);

            float blockadeGap = DevTuning.Get("dev.blockade", 0f);
            if (blockadeGap > 0f) w.SpawnBlockade(w.Train.S + blockadeGap);

            float devHp = DevTuning.Get("dev.hp", 0f);
            if (devHp > 0f)
            {
                w.Train.MaxHp = devHp;
                w.Train.Hp = devHp;
            }

            if (DevTuning.Get("dev.noguns", 0f) > 0.5f) w.GunsEnabled = false;

            // One of every species in a row ahead of the train, so the roster can be read
            // on the device in one screenshot. Pair with dev.noguns=1 and dev.hp=999.
            if (DevTuning.Get("dev.lineup", 0f) > 0.5f)
            {
                float first = DevTuning.Get("dev.lineupat", 14f);
                var kinds = (EnemyKind[])System.Enum.GetValues(typeof(EnemyKind));
                for (int i = 0; i < kinds.Length; i++)
                {
                    float s = w.Train.S + first + i * 4.2f;
                    float x = (i % 2 == 0) ? -4.6f : 4.6f;
                    w.Spawn(kinds[i], s, x);
                }

                // freeze so the row can be photographed before it closes in
                if (DevTuning.Get("dev.pause", 0f) > 0.5f) Loop.Paused = true;
            }

            // dev.wagons=5 builds the full convoy, for checking the camera push-back
            int devWagons = (int)DevTuning.Get("dev.wagons", 0f);
            for (int i = 0; i < devWagons; i++) w.GrantCard(CardId.ExtraWagon);

            // dev.guns=4 bolts turrets on, so the modelled gun can be inspected in place
            int devGuns = (int)DevTuning.Get("dev.guns", 0f);
            for (int i = 1; i < devGuns; i++) w.GrantCard(CardId.ExtraGun);

            float stageEnd = DevTuning.Get("dev.stageend", 0f);
            if (stageEnd > 0f) w.Train.S = Mathf.Max(0f, w.StageLength - stageEnd);
        }

        void BuildLighting()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(42f, 155f, 0f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;   // props need contact shadows or they float
            light.shadowStrength = 0.72f;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(transform, false);
            fill.transform.rotation = Quaternion.Euler(-30f, 20f, 0f);
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.color = new Color(0.45f, 0.6f, 1f);
            fl.intensity = 0.35f;
            fl.shadows = LightShadows.None;
        }

        void BuildTrack()
        {
            var root = new GameObject("Track").transform;
            root.SetParent(transform, false);
            TrackMesh.Build(root);
        }

        void BuildSim()
        {
            Loop = gameObject.AddComponent<GameLoop>();
            var input = gameObject.AddComponent<InputRouter>();
            Loop.Input = input;
        }

        void BuildTrain()
        {
            var go = new GameObject("Train");
            go.transform.SetParent(transform, false);
            Train = go.AddComponent<TrainRig>();
            Train.Build(Loop.World.Train.Wagons, Loop.World.Train.Guns);
        }

        void BuildEnemies()
        {
            ArtSprites.Preload();

            var go = new GameObject("Enemies");
            go.transform.SetParent(transform, false);
            Enemies = go.AddComponent<EnemyViewPool>();
            Enemies.Build();
        }

        /// <summary>Blockades, loot and track-side props — all from the generated art atlas.</summary>
        void BuildWorldArt()
        {
            var go = new GameObject("WorldArt");
            go.transform.SetParent(transform, false);

            var blockades = new GameObject("Blockades");
            blockades.transform.SetParent(go.transform, false);
            blockades.AddComponent<BlockadeView>().Build();

            var pickups = new GameObject("Pickups");
            pickups.transform.SetParent(go.transform, false);
            pickups.AddComponent<PickupView>().Build();

            // 3D district first: the sprite props below skip themselves when it is present
            var district = new GameObject("District");
            district.transform.SetParent(go.transform, false);
            district.AddComponent<DistrictScenery>();

            var scenery = new GameObject("Scenery");
            scenery.transform.SetParent(go.transform, false);
            scenery.AddComponent<Scenery>().Build();
        }

        void BuildCamera()
        {
            var go = new GameObject("MainCamera");
            go.tag = "MainCamera";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(0f, 9f, -10f);

            Cam = go.AddComponent<Camera>();
            Cam.fieldOfView = 55f;
            Cam.nearClipPlane = 0.15f;
            Cam.farClipPlane = 400f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.075f, 0.095f, 0.135f);
            go.AddComponent<AudioListener>();

            var urpData = go.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = false;
            urpData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var follow = go.AddComponent<CameraFollow>();
            follow.Init(Train, Cam);

            SpriteBillboard.CameraTransform = Cam.transform;
        }

        void BuildFx()
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(transform, false);
            Fx = go.AddComponent<Effects>();
            Fx.Build(Train);
        }

        /// <summary>The real round driver binds to this World and turns timer damage off.</summary>
        void BuildProjectiles()
        {
            var go = new GameObject("Projectiles");
            go.transform.SetParent(transform, false);
            go.AddComponent<ProjectileDriver>().Build(Loop, Train, Fx);
        }

        void BuildHud()
        {
            var go = new GameObject("Hud");
            go.transform.SetParent(transform, false);
            go.AddComponent<HudView>().Build();
        }
    }
}

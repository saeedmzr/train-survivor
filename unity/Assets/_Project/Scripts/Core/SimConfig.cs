namespace TrainSurvivor.Core
{
    /// <summary>All tuning lives here. Balance changes are diffs against this file.</summary>
    public static class SimConfig
    {
        public const int TickRate = 60;
        public const float Dt = 1f / TickRate;

        // --- train ---
        public const float MoveSpeed = 12f;          // m/s at full throttle
        public const float AccelTime = 0.6f;         // 0 -> full in this many seconds
        public const float BrakeTime = 0.18f;        // full -> 0, the "brake burst"
        public const float BaseHp = 100f;
        public const float WagonHp = 35f;
        public const int StartingGuns = 1;
        public const int StartingWagons = 1;
        public const int MaxGuns = 12;
        public const int MaxWagons = 6;

        // --- brake charge (Change B) ---
        public const float ChargeFillTime = 4f;      // seconds of movement to fill
        public const float ChargeWindow = 2f;        // seconds of bonus fire after release
        public const float ChargeBonus = 0.40f;      // +40% fire rate at full charge

        // --- weapons ---
        // Restored to the values the balance suite encodes: the 3 dmg / 0.5 s pass made a
        // barricade (90-230 hp) unbreakable at the 1 starting gun and let a single walker
        // out-heal the ram. 10 dmg at 0.12 s is 83 dps per gun.
        public const float GunDamage = 10f;
        public const float GunInterval = 0.12f;      // seconds between shots, per gun
        public const float GunRange = 34f;           // guns hold fire beyond this — zombies must close in
        public const float RearTargetRange = 2f;     // don't shoot things this far behind
        public const float PierceDamageMul = 0.6f;

        // --- ram (Change A) ---
        public const float RamDamage = 35f;
        public const float RamRange = 3f;
        public const float RamMinSpeed = 7.2f;       // 60% of MoveSpeed
        public const float RamCooldown = 0.35f;

        // --- fire trail ---
        public const float FireTrailDps = 14f;
        public const float FireTrailMinSpeed = 6f;
        public const float FireTrailBack = 5f;
        public const float FireTrailAhead = 2f;

        // --- enemies ---
        // Contact is measured from the convoy hull, not the locomotive's centre point: the old
        // centre-distance test let walkers "reach" the train from inside the rear wagons.
        public const float ContactRange = 3f;        // gap from the hull at which a bite lands
        public const float TrainNose = 4.1f;         // loco half-length ahead of Train.S
        public const float WagonSpan = 6.6f;         // length of one freight wagon
        public const float TrainHalfWidth = 1.6f;    // hull half-width
        public const float WalkerHp = 30f;
        public const float WalkerSpeed = 2.2f;
        public const float WalkerDamage = 6f;
        public const float WalkerAttackInterval = 1.2f;

        public const float SprinterHp = 12f;
        public const float SprinterSpeed = 6.5f;
        public const float SprinterDamage = 4f;
        public const float SprinterAttackInterval = 0.6f;

        public const float TankHp = 255f;
        public const float TankSpeed = 1.2f;
        public const float TankDamage = 18f;
        public const float TankAttackInterval = 2.0f;

        public const int MaxEnemies = 64;

        // --- live spawner: keeps walkers closing from every direction, not just the rails ahead ---
        public const bool LiveSpawner = true;
        public const float LiveSpawnEvery = 1.6f;     // seconds between bursts
        public const int LiveSpawnBurst = 3;          // up to this many per burst
        public const int LiveSpawnCap = 18;           // stop spawning while this many are alive
        public const float LiveSpawnBehind = -30f;    // S offset range relative to the train
        public const float LiveSpawnAhead = 65f;
        public const float LiveSpawnLateral = 22f;    // ± metres off the rails — they walk in across the ground

        // --- roster -------------------------------------------------------------
        // One row per species. XP scales with the threat; the flags drive the three
        // behaviours the sim actually simulates (ram immunity, death blast, flight).
        public struct EnemyStat
        {
            public float Hp, Speed, Damage, AttackInterval, Xp;
            public bool RamImmune;      // the cowcatcher passes through / over it
            public float BlastRadius;   // on death, damages everything around it
            public float BlastDamage;
            public bool Flying;         // ignores ground hazards, drawn higher
        }

        public static EnemyStat Stat(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Sprinter:
                    return new EnemyStat { Hp = SprinterHp, Speed = SprinterSpeed, Damage = SprinterDamage,
                                           AttackInterval = SprinterAttackInterval, Xp = 1f };
                case EnemyKind.Tank:
                    return new EnemyStat { Hp = TankHp, Speed = TankSpeed, Damage = TankDamage,
                                           AttackInterval = TankAttackInterval, Xp = 6f };
                case EnemyKind.Crawler:
                    return new EnemyStat { Hp = 22f, Speed = 4.4f, Damage = 4f, AttackInterval = 0.9f,
                                           Xp = 2f, RamImmune = true };
                case EnemyKind.Bloater:
                    return new EnemyStat { Hp = 60f, Speed = 2.2f, Damage = 12f, AttackInterval = 1.6f,
                                           Xp = 4f, BlastRadius = 3.2f, BlastDamage = 18f };
                case EnemyKind.Spitter:
                    return new EnemyStat { Hp = 45f, Speed = 2.8f, Damage = 7f, AttackInterval = 1.4f, Xp = 3.5f };
                case EnemyKind.Shrieker:
                    return new EnemyStat { Hp = 70f, Speed = 2.6f, Damage = 3f, AttackInterval = 1.4f, Xp = 4f };
                case EnemyKind.Bulwark:
                    return new EnemyStat { Hp = 110f, Speed = 2.4f, Damage = 12f, AttackInterval = 1.4f, Xp = 5f };
                case EnemyKind.Glider:
                    return new EnemyStat { Hp = 35f, Speed = 5.2f, Damage = 9f, AttackInterval = 1.0f,
                                           Xp = 3f, Flying = true };
                case EnemyKind.Drillmaw:
                    return new EnemyStat { Hp = 90f, Speed = 3.0f, Damage = 15f, AttackInterval = 1.6f, Xp = 4.5f };
                case EnemyKind.Lantern:
                    return new EnemyStat { Hp = 85f, Speed = 2.0f, Damage = 6f, AttackInterval = 1.4f, Xp = 5f };
                case EnemyKind.Rimeback:
                    return new EnemyStat { Hp = 140f, Speed = 1.8f, Damage = 16f, AttackInterval = 1.6f, Xp = 5.5f };
                default:
                    return new EnemyStat { Hp = WalkerHp, Speed = WalkerSpeed, Damage = WalkerDamage,
                                           AttackInterval = WalkerAttackInterval, Xp = 1.5f };
            }
        }

        /// <summary>Weighted species pick for a normal spawn; later stages open the nastier ones.</summary>
        /// <summary>
        /// While the walker 3D model is the only modelled enemy, the stage spawns nothing else.
        /// Flip this back to false to restore the full roster table.
        /// </summary>
        public const bool OnlyWalkers = true;

        public static EnemyKind PickKind(int stage, float roll)
        {
            if (OnlyWalkers) return EnemyKind.Walker;

            // cumulative table, tuned so stage 1 is walkers/crawlers and stage 6+ is varied
            float wWalker = 100f;
            float wCrawler = stage >= 1 ? 40f : 0f;
            float wSprinter = stage >= 2 ? 45f : 0f;
            float wSpitter = stage >= 3 ? 25f : 0f;
            float wBloater = stage >= 3 ? 22f : 0f;
            float wGlider = stage >= 4 ? 20f : 0f;
            float wShrieker = stage >= 5 ? 14f : 0f;
            float wDrillmaw = stage >= 5 ? 14f : 0f;
            float wBulwark = stage >= 6 ? 18f : 0f;
            float wRimeback = stage >= 7 ? 14f : 0f;
            float wLantern = stage >= 8 ? 12f : 0f;
            float total = wWalker + wCrawler + wSprinter + wSpitter + wBloater + wGlider +
                          wShrieker + wDrillmaw + wBulwark + wRimeback + wLantern;

            float t = roll * total;
            if ((t -= wWalker) < 0f) return EnemyKind.Walker;
            if ((t -= wCrawler) < 0f) return EnemyKind.Crawler;
            if ((t -= wSprinter) < 0f) return EnemyKind.Sprinter;
            if ((t -= wSpitter) < 0f) return EnemyKind.Spitter;
            if ((t -= wBloater) < 0f) return EnemyKind.Bloater;
            if ((t -= wGlider) < 0f) return EnemyKind.Glider;
            if ((t -= wShrieker) < 0f) return EnemyKind.Shrieker;
            if ((t -= wDrillmaw) < 0f) return EnemyKind.Drillmaw;
            if ((t -= wBulwark) < 0f) return EnemyKind.Bulwark;
            if ((t -= wRimeback) < 0f) return EnemyKind.Rimeback;
            return EnemyKind.Lantern;
        }

        // --- blockades ---
        public const float BlockStopDistance = 6f;   // train halts this far short
        public const float BarrierHp = 90f;
        public const float BarrelsHp = 140f;
        public const float WreckHp = 230f;

        // --- pickups ---
        public const int MaxPickups = 80;
        public const float PickupMagnet = 3.2f;      // gems start seeking inside this range
        public const float PickupDrift = 1.6f;       // m/s creep toward the train outside the magnet
        public const float PickupTake = 1.4f;
        public const float PickupLifetime = 18f;
        public const float HealthPackChance = 0.14f;
        public const float HealthPackHeal = 0.15f;   // fraction of max HP

        // --- run / stages ---
        public const float StageBaseLength = 380f;
        public const float StageLengthPerStage = 40f;
        public const float EnemyHpScalePerStage = 0.15f;    // ×1.15 ^ (stage-1)
        public const float XpBase = 6f;
        public const float XpPerLevel = 3f;
    }

    /// <summary>Deterministic RNG: same seed + same inputs = same run, on every device.</summary>
    public struct Mulberry32
    {
        uint _state;

        public Mulberry32(uint seed) { _state = seed == 0 ? 0x9E3779B9u : seed; }

        public float Next01()
        {
            _state += 0x6D2B79F5u;
            uint t = _state;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return ((t ^ (t >> 14)) & 0xFFFFFF) / (float)0x1000000;
        }

        public float Range(float min, float max) => min + (max - min) * Next01();

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Next01() * (maxExclusive - minInclusive));
        }
    }
}

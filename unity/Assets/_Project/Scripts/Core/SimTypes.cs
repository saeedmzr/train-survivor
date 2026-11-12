namespace TrainSurvivor.Core
{
    public enum TrainMode { Stopped, Moving, Blocked, Cleared, Dead }

    /// <summary>
    /// Species in the roster. Each has stats in SimConfig.EnemyStats and a painted sprite
    /// in Art/atlas_enemies (names en_<lower-case kind>).
    /// </summary>
    public enum EnemyKind
    {
        Walker, Sprinter, Tank,
        Crawler, Bloater, Spitter, Shrieker, Bulwark, Glider, Drillmaw, Lantern, Rimeback
    }

    public static class EnemySpecies
    {
        public static string Id(EnemyKind kind) => kind.ToString().ToLowerInvariant();
        public static string Sprite(EnemyKind kind) => "en_" + Id(kind);
    }

    public enum PickupKind { XpGem, HealthPack }

    public enum BlockadeKind { Barrier, Barrels, Wreck }

    public enum OfferReason { LevelUp, StageClear }

    public enum SimEventKind
    {
        Shot, Kill, ContactHit, Ram, Brake, Cleared, Died,
        BlockadeHit, BlockadeBroken, PickupTaken, LevelUp, CardChosen, Blast,
        /// <summary>A player round actually struck an enemy. ContactHit is the train being
        /// bitten and stays a separate event: this one drives hit audio/VFX and carries the
        /// projectile that landed.</summary>
        EnemyHit
    }

    /// <summary>Player intent for one tick. Touch release = Stopped = guns live.</summary>
    public struct Intent
    {
        public bool Held;
        public float SteerX;   // -1..1, unused until forks

        public static Intent Move => new Intent { Held = true };
        public static Intent Stop => new Intent { Held = false };
    }

    public struct SimEvent
    {
        public SimEventKind Kind;

        /// <summary>Species of the enemy this event is about. Carried on the event because
        /// the enemy is already dead by the time views drain the queue.</summary>
        public EnemyKind EnemyKind;
        public int EnemyId;
        public int BlockadeId;
        public float FromS, FromX;   // where the shot left
        public float ToS, ToX;       // where it landed
        public float Damage;
        public int GunIndex;
        /// <summary>Unique id of the projectile this shot/impact refers to (0 when none).</summary>
        public int ProjectileId;
    }

    /// <summary>The nine cards on the art sheet. Index order matches the sprite names.</summary>
    public enum CardId
    {
        Multishot = 0,
        FireTrail = 1,
        SpikeBumper = 2,
        PiercingRounds = 3,
        ReinforcedBoiler = 4,
        RapidFireEngine = 5,
        IncendiaryShells = 6,
        ExtraGun = 7,          // legacy (retired from offers): SPARE BARRELS fire rate
        ExtraWagon = 8,        // legacy (retired from offers): FIELD REPAIR heal

        // Approved Stage 1 card set (new offers draw only from these)
        HeavyHitter = 9,
        FastFire = 10,
        LongReach = 11,
        QuickAim = 12,
        ToughTrain = 13,
        StrongArmor = 14,
        FixItUp = 15,
        FullSteam = 16,
        FastLearner = 17
    }

    /// <summary>What an offer slot is for, so the three picks always cover combat, survival
    /// and a flex pick instead of three copies of the same flavour.</summary>
    public enum CardRole { Combat, Survival, Flex }

    public static class Cards
    {
        public const int Count = 18;

        public static readonly string[] Names =
        {
            "MULTISHOT", "FIRE TRAIL", "SPIKE BUMPER", "PIERCING ROUNDS", "REINFORCED BOILER",
            "RAPID FIRE ENGINE", "INCENDIARY SHELLS", "SPARE BARRELS", "FIELD REPAIR",
            // approved set: short, plain titles a non-gamer reads at a glance
            "HEAVY HITTER", "FAST FIRE", "LONG REACH", "QUICK AIM", "TOUGH TRAIN",
            "STRONG ARMOR", "FIX IT UP", "FULL STEAM", "FAST LEARNER"
        };

        public static readonly string[] Blurbs =
        {
            "Each gun hits +1 more zombie.",
            "Burning track: the train scorches what it passes.",
            "Cowcatcher deals +25 ram damage.",
            "Bullets carry through +1 more target.",
            "+20% max HP and repair the extra plating.",
            "+15% fire rate on every gun.",
            "+20% chance to set zombies alight.",
            "Cooler barrels: +10% fire rate on every gun.",
            "Patch the boiler: repair 40% of max HP.",
            // approved set: one short line, no numbers, no jargon
            "Your guns hit harder.",
            "Your guns shoot faster.",
            "Your guns shoot farther.",
            "Your guns turn toward enemies faster.",
            "Your train can take more damage.",
            "Enemy attacks hurt your train less.",
            "Restore some of your train's health.",
            "Your train moves faster while you hold.",
            "Your train levels up faster."
        };

        /// <summary>Max ranks per approved card; 0 means "no cap, gated by eligibility".</summary>
        public static int Cap(CardId id)
        {
            switch (id)
            {
                case CardId.HeavyHitter: return 3;
                case CardId.FastFire: return 3;
                case CardId.LongReach: return 2;
                case CardId.QuickAim: return 2;
                case CardId.ToughTrain: return 3;
                case CardId.StrongArmor: return 3;
                case CardId.FixItUp: return 0;
                case CardId.FullSteam: return 2;
                case CardId.FastLearner: return 2;
                default: return 0;
            }
        }

        public static CardRole RoleOf(CardId id)
        {
            switch (id)
            {
                case CardId.HeavyHitter:
                case CardId.FastFire:
                case CardId.LongReach:
                case CardId.QuickAim: return CardRole.Combat;
                case CardId.ToughTrain:
                case CardId.StrongArmor: return CardRole.Survival;
                default: return CardRole.Flex;
            }
        }

        /// <summary>The nine approved cards, in offer order. Legacy ids stay in the enum for
        /// persisted stacks but are never drawn into a new offer.</summary>
        public static readonly CardId[] Approved =
        {
            CardId.HeavyHitter, CardId.FastFire, CardId.LongReach, CardId.QuickAim,
            CardId.ToughTrain, CardId.StrongArmor, CardId.FixItUp, CardId.FullSteam,
            CardId.FastLearner
        };

        public static readonly string[] Sprites =
        {
            "up_multishot", "up_firetrail", "up_spikebumper", "up_piercing", "up_boiler",
            "up_rapidfire", "up_incendiary", "up_extragun", "up_extrawagon",
            "up_heavyhitter", "up_fastfire", "up_longreach", "up_quickaim", "up_toughtrain",
            "up_strongarmor", "up_fixitup", "up_fullsteam", "up_fastlearner"
        };
    }

    public sealed class TrainState
    {
        public TrainMode Mode = TrainMode.Stopped;
        public float S;                 // distance along track
        public float Speed;             // m/s
        public float Charge;            // 0..1, fills while moving
        public float ChargeSpent;       // 0..1, locked in on release
        public float ChargeWindow;      // seconds of bonus fire remaining
        public float Hp = SimConfig.BaseHp;
        public float MaxHp = SimConfig.BaseHp;
        public int Guns = SimConfig.StartingGuns;
        public int Wagons = SimConfig.StartingWagons;
        public int Kills;

        // run progression
        public int Stage = 1;
        public int Level = 1;
        public float Xp;
        public float XpToNext;
        public int XpGems;
        public int BlockadesBroken;

        /// <summary>Stacks per card, indexed by CardId.</summary>
        public readonly int[] CardStacks = new int[Cards.Count];

        /// <summary>Last tick's input. Guns go live the instant the finger lifts — no windup.</summary>
        public bool InputHeld;

        public bool Firing => !InputHeld && Mode != TrainMode.Dead && Mode != TrainMode.Blocked;

        public int CardStacksOf(CardId id) => CardStacks[(int)id];

        public float FireRateMultiplier =>
            (ChargeWindow > 0f ? 1f + SimConfig.ChargeBonus * ChargeSpent : 1f)
            * (1f + 0.15f * CardStacks[(int)CardId.RapidFireEngine])
            * (1f + 0.10f * CardStacks[(int)CardId.ExtraGun])    // legacy "SPARE BARRELS"
            * (1f + 0.08f * CardStacks[(int)CardId.FastFire]);   // FAST FIRE

        /// <summary>Global shot damage: every current and future gun reads this.</summary>
        public float GunDamageMultiplier => 1f + 0.12f * CardStacks[(int)CardId.HeavyHitter];

        /// <summary>Global acquisition/firing range.</summary>
        public float GunRangeMultiplier => 1f + 0.10f * CardStacks[(int)CardId.LongReach];

        /// <summary>Turret turn rate: aim solves stay the same, only the slew is faster.</summary>
        public float AimSpeedMultiplier => 1f + 0.20f * CardStacks[(int)CardId.QuickAim];

        /// <summary>Convoy HP from the base hull + wagons, boosted by TOUGH TRAIN and the
        /// legacy boiler. Everything that adds a wagon recomputes through this.</summary>
        public float MaxHpFor(int wagons) =>
            (SimConfig.BaseHp + SimConfig.WagonHp * wagons)
            * (1f + 0.12f * CardStacks[(int)CardId.ToughTrain])
            * (float)System.Math.Pow(1.2, CardStacks[(int)CardId.ReinforcedBoiler]);

        public float DamageTakenMultiplier => 1f - 0.08f * CardStacks[(int)CardId.StrongArmor];

        public float MoveSpeedMultiplier => 1f + 0.08f * CardStacks[(int)CardId.FullSteam];

        public float XpGainMultiplier => 1f + 0.10f * CardStacks[(int)CardId.FastLearner];

        public float RamDamageNow =>
            (SimConfig.RamDamage + 25f * CardStacks[(int)CardId.SpikeBumper]) * StageDamageScale;

        /// <summary>Player output scales 6% per stage so TTK stays sharp (design §8.3).</summary>
        public float StageDamageScale => 1f + 0.06f * (Stage - 1);
    }

    public struct Enemy
    {
        public int Id;
        public EnemyKind Kind;
        public float S, X;
        public float Hp, MaxHp;
        public float Speed;
        public float Damage;
        public float AttackInterval;
        public float AttackCd;
        public float RamCd;
        public float Flash;      // view hint, decays in sim so it's deterministic
        public float Burn;       // seconds of burn left
        public float BurnDps;
        public bool Alive;
    }

    public struct Pickup
    {
        public int Id;
        public PickupKind Kind;
        public float S, X;
        public float Value;
        public float Spin;       // view hint, advanced in sim so it is deterministic
        public float Life;
        public bool Alive;
    }

    public struct Blockade
    {
        public int Id;
        public BlockadeKind Kind;
        public float S, X;
        public float Hp, MaxHp;
        public float Flash;
        public bool Alive;
    }
}

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
        ExtraGun = 7,
        ExtraWagon = 8
    }

    public static class Cards
    {
        public const int Count = 9;

        public static readonly string[] Names =
        {
            "MULTISHOT", "FIRE TRAIL", "SPIKE BUMPER", "PIERCING ROUNDS", "REINFORCED BOILER",
            "RAPID FIRE ENGINE", "INCENDIARY SHELLS", "EXTRA GUN", "EXTRA WAGON"
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
            "Bolt another gun onto the roof.",
            "Hitch another wagon: +35 HP."
        };

        public static readonly string[] Sprites =
        {
            "up_multishot", "up_firetrail", "up_spikebumper", "up_piercing", "up_boiler",
            "up_rapidfire", "up_incendiary", "up_extragun", "up_extrawagon"
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
            * (1f + 0.15f * CardStacks[(int)CardId.RapidFireEngine]);

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

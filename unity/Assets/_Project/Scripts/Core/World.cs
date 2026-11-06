using System;
using System.Collections.Generic;

namespace TrainSurvivor.Core
{
    /// <summary>
    /// The whole game, deterministic and engine-free. Fixed 1/60 step.
    /// Views read state; they never write to it.
    ///
    /// A stage is a stretch of track: zombie groups, destructible blockades that force a
    /// stop, and a finish line at the end. Clearing it offers three cards; levelling up
    /// from XP gems offers three cards too. Both go through the same offer.
    /// </summary>
    public sealed class World
    {
        public readonly TrainState Train = new TrainState();
        public readonly Enemy[] Enemies = new Enemy[SimConfig.MaxEnemies];
        public readonly Pickup[] Pickups = new Pickup[SimConfig.MaxPickups];
        public readonly Blockade[] Blockades = new Blockade[16];
        public readonly List<SimEvent> Events = new List<SimEvent>(256);

        public int EnemyCount { get; private set; }
        public int PickupCount { get; private set; }
        public int BlockadeCount { get; private set; }

        public float StageLength { get; private set; } = SimConfig.StageBaseLength;
        public float Progress01 => Math.Min(1f, Train.S / Math.Max(1f, StageLength));

        readonly CardId[] _offer = new CardId[3];
        public CardId OfferAt(int i) => _offer[i];
        public bool OfferPending { get; private set; }
        public OfferReason OfferKind { get; private set; }
        public int PendingOffers { get; private set; }

        readonly float[] _gunCd = new float[SimConfig.MaxGuns];
        readonly float[] _gunOffsetX = new float[SimConfig.MaxGuns];
        readonly int[] _hitIds = new int[8];

        Mulberry32 _rng;
        int _nextId = 1;

        /// <summary>Dev switch: zombies can close in without being shot, for testing damage.</summary>
        public bool GunsEnabled = true;

        public World() : this(12345u) { }

        public World(uint seed) : this(seed, true) { }

        /// <summary>No stage content: unit tests and sandboxes spawn exactly what they need.</summary>
        public World(uint seed, bool populateStage)
        {
            _rng = new Mulberry32(seed);

            for (int i = 0; i < _gunCd.Length; i++)
            {
                // Guns alternate left/right of the roof line like a real AA battery.
                _gunOffsetX[i] = (i % 2 == 0 ? -0.55f : 0.55f) + (i / 2) * 0.22f;
            }

            ResetRun(populateStage);
        }

        public static World Empty(uint seed = 1u) => new World(seed, false);

        // ---------------------------------------------------------------- run / stage

        public void ResetRun()
        {
            ResetRun(true);
        }

        public void ResetRun(bool populateStage)
        {
            Train.Mode = TrainMode.Stopped;
            Train.S = 0f;
            Train.Speed = 0f;
            Train.Charge = 0f;
            Train.ChargeSpent = 0f;
            Train.ChargeWindow = 0f;
            Train.Stage = 1;
            Train.Level = 1;
            Train.Xp = 0f;
            Train.XpToNext = SimConfig.XpBase;
            Train.XpGems = 0;
            Train.BlockadesBroken = 0;
            Train.Kills = 0;
            Train.Guns = SimConfig.StartingGuns;
            Train.Wagons = SimConfig.StartingWagons;
            Train.MaxHp = SimConfig.BaseHp + SimConfig.WagonHp * (Train.Wagons - 1);
            Train.Hp = Train.MaxHp;
            Array.Clear(Train.CardStacks, 0, Train.CardStacks.Length);

            OfferPending = false;
            PendingOffers = 0;

            BuildStage(populateStage);
        }

        /// <summary>Called when the player picks a card. Returns false if no offer is open.</summary>
        public bool ChooseCard(int index)
        {
            if (!OfferPending || index < 0 || index >= 3) return false;

            CardId id = _offer[index];
            GrantCard(id);

            Events.Add(new SimEvent { Kind = SimEventKind.CardChosen, Damage = (int)id });

            OfferPending = false;

            if (OfferKind == OfferReason.StageClear)
            {
                if (PendingOffers > 0) PendingOffers--;
                NextStage();
            }
            else if (PendingOffers > 0)
            {
                PendingOffers--;
                BeginOffer(OfferReason.LevelUp);
            }
            return true;
        }

        /// <summary>Grants a card: increments its stack and applies the immediate effects.
        /// Public because meta-progression and tests also grant cards directly.</summary>
        public void GrantCard(CardId id)
        {
            Train.CardStacks[(int)id]++;
            ApplyCard(id);
        }

        /// <summary>Adds XP and resolves any level-ups. Public for dev cheats and tests.</summary>
        public void GrantXp(float amount)
        {
            Train.Xp += amount;
            TryLevelUp();
        }

        void ApplyCard(CardId id)
        {
            switch (id)
            {
                case CardId.ReinforcedBoiler:
                    Train.MaxHp *= 1.2f;
                    Train.Hp = Math.Min(Train.MaxHp, Train.Hp + Train.MaxHp * 0.2f);
                    break;
                case CardId.ExtraWagon:
                    if (Train.Wagons < SimConfig.MaxWagons)
                    {
                        Train.Wagons++;
                        Train.MaxHp += SimConfig.WagonHp;
                        Train.Hp += SimConfig.WagonHp;
                    }
                    break;
                case CardId.ExtraGun:
                    if (Train.Guns < SimConfig.MaxGuns) Train.Guns++;
                    break;
            }
        }

        void BeginOffer(OfferReason reason)
        {
            OfferPending = true;
            OfferKind = reason;

            // Three distinct cards, drawn from the same seeded stream as everything else.
            int n = 0;
            while (n < 3)
            {
                var card = (CardId)_rng.Range(0, Cards.Count);
                bool dup = false;
                for (int i = 0; i < n; i++) if (_offer[i] == card) dup = true;
                if (!dup) _offer[n++] = card;
            }
        }

        void NextStage()
        {
            Train.Stage++;
            Train.S = 0f;
            Train.Speed = 0f;
            Train.Charge = 0f;
            Train.ChargeSpent = 0f;
            Train.ChargeWindow = 0f;
            Train.Mode = TrainMode.Stopped;

            // Milestones from the design doc: a gun every 3 stages, a wagon every 5.
            if (Train.Stage % 3 == 0 && Train.Guns < SimConfig.MaxGuns) Train.Guns++;
            if (Train.Stage % 5 == 0 && Train.Wagons < SimConfig.MaxWagons)
            {
                Train.Wagons++;
                Train.MaxHp += SimConfig.WagonHp;
            }
            Train.Hp = Math.Min(Train.MaxHp, Train.Hp + Train.MaxHp * 0.2f);

            BuildStage();
        }

        /// <summary>Hand-built-for-now stage: groups, blockades, finish line.</summary>
        void BuildStage()
        {
            BuildStage(true);
        }

        void BuildStage(bool populate)
        {
            EnemyCount = 0;
            PickupCount = 0;
            BlockadeCount = 0;

            StageLength = Math.Min(TrackPath.Length - 40f,
                                   SimConfig.StageBaseLength + SimConfig.StageLengthPerStage * (Train.Stage - 1));

            if (!populate) return;

            float s = 26f;
            int slot = 0;
            while (s < StageLength - 25f)
            {
                bool blockade = slot > 0 && slot % 4 == 3;
                if (blockade)
                {
                    SpawnBlockade(s);
                    // A tank guards the blockade: you stop, you clear the guard, you break the wall.
                    if (Train.Stage >= 2 || slot > 3)
                        Spawn(EnemyKind.Tank, s + 3f, _rng.Range(-3.5f, 3.5f));
                    SpawnGroup(s - 8f, 2);
                }
                else
                {
                    SpawnGroup(s, 2 + _rng.Range(0, 2 + Math.Min(3, Train.Stage / 2)));
                }

                s += _rng.Range(24f, 36f);
                slot++;
            }

            // The finale always has a wreck to smash.
            SpawnBlockade(StageLength - 18f, BlockadeKind.Wreck);
        }

        void SpawnGroup(float s, int count)
        {
            for (int i = 0; i < count; i++)
            {
                EnemyKind kind = SimConfig.PickKind(Train.Stage, _rng.Next01());
                Spawn(kind, s + _rng.Range(-4f, 6f), _rng.Range(-5f, 5f));
            }
        }

        // ---------------------------------------------------------------- stepping

        public void Advance(float dt, Intent intent)
        {
            Events.Clear();
            if (Train.Mode == TrainMode.Dead || OfferPending) return;

            StepMovement(dt, intent);
            StepEnemies(dt);
            StepPickups(dt);
            StepGuns(dt);
            StepProgress();
        }

        void StepMovement(float dt, Intent intent)
        {
            Train.InputHeld = intent.Held;
            bool wantsMove = intent.Held && Train.Mode != TrainMode.Cleared;

            float wall = BlockadeAheadS();
            float wallStopS = wall < float.MaxValue ? wall - SimConfig.BlockStopDistance : float.MaxValue;

            if (wantsMove)
            {
                if (Train.Mode != TrainMode.Moving) Emit(SimEventKind.Brake, 0f); // leaving combat
                Train.Mode = TrainMode.Moving;
                Train.Speed = Math.Min(SimConfig.MoveSpeed, Train.Speed + SimConfig.MoveSpeed / SimConfig.AccelTime * dt);
                Train.Charge = Math.Min(1f, Train.Charge + dt / SimConfig.ChargeFillTime);

                // A blockade is a hard stop: the cowcatcher cannot punch through it.
                if (Train.S + Train.Speed * dt >= wallStopS)
                {
                    Train.S = Math.Max(Train.S, wallStopS);
                    Train.Speed = 0f;
                    Train.Mode = TrainMode.Blocked;
                }
            }
            else
            {
                bool wasMoving = Train.Mode == TrainMode.Moving || Train.Mode == TrainMode.Blocked;
                Train.Speed = Math.Max(0f, Train.Speed - SimConfig.MoveSpeed / SimConfig.BrakeTime * dt);
                if (Train.Speed <= 0f && Train.Mode != TrainMode.Dead)
                {
                    if (wasMoving)
                    {
                        // Spend the charge: the brake pays out as a burst of fire rate.
                        Train.ChargeSpent = Train.Charge;
                        Train.ChargeWindow = Train.ChargeSpent > 0.05f ? SimConfig.ChargeWindow : 0f;
                        Train.Charge = 0f;
                        Emit(SimEventKind.Brake, 0f);
                    }
                    if (Train.Mode != TrainMode.Cleared) Train.Mode = TrainMode.Stopped;
                }
            }

            if (Train.ChargeWindow > 0f) Train.ChargeWindow = Math.Max(0f, Train.ChargeWindow - dt);

            Train.S += Train.Speed * dt;

            // Fire trail: the track itself burns while the train is rolling with the card.
            if (Train.CardStacksOf(CardId.FireTrail) > 0 && Train.Speed >= SimConfig.FireTrailMinSpeed)
            {
                float dps = SimConfig.FireTrailDps * Train.CardStacksOf(CardId.FireTrail) * dt;
                for (int i = 0; i < EnemyCount; i++)
                {
                    ref Enemy e = ref Enemies[i];
                    if (!e.Alive) continue;
                    float gap = e.S - Train.S;
                    if (gap < -SimConfig.FireTrailBack || gap > SimConfig.FireTrailAhead) continue;

                    e.Hp -= dps;
                    e.Flash = Math.Max(e.Flash, 0.35f);
                    if (e.Hp <= 0f) Kill(ref e);
                }
            }
        }

        void StepEnemies(float dt)
        {
            for (int i = 0; i < EnemyCount; i++)
            {
                ref Enemy e = ref Enemies[i];
                if (!e.Alive) continue;

                e.Flash = Math.Max(0f, e.Flash - dt * 6f);

                if (e.Burn > 0f)
                {
                    e.Burn -= dt;
                    e.Hp -= e.BurnDps * dt;
                    e.Flash = Math.Max(e.Flash, 0.5f);
                    if (e.Hp <= 0f) { Kill(ref e); continue; }
                }

                float gap = e.S - Train.S;

                // Walk toward the train, converging on the centerline.
                if (gap > SimConfig.ContactRange)
                {
                    float speed = e.Speed * (e.Burn > 0f ? 0.7f : 1f);
                    e.S -= speed * dt;
                    e.X = e.X * (1f - Math.Min(1f, dt * 0.8f));
                }
                else
                {
                    // Bite the lead wagon.
                    e.AttackCd -= dt;
                    if (e.AttackCd <= 0f)
                    {
                        e.AttackCd = e.AttackInterval;
                        DamageTrain(e.Damage, e.Id);
                    }
                }

                // Cowcatcher: only at speed, only in front, never enough to kill a tank.
                e.RamCd = Math.Max(0f, e.RamCd - dt);
                if (Train.Speed >= SimConfig.RamMinSpeed && gap > -1.5f && gap <= SimConfig.RamRange &&
                    e.RamCd <= 0f && !SimConfig.Stat(e.Kind).RamImmune)
                {
                    e.RamCd = SimConfig.RamCooldown;
                    e.Hp -= Train.RamDamageNow;
                    e.Flash = 1f;
                    Emit(SimEventKind.Ram, Train.RamDamageNow, e.Id);
                    if (e.Hp <= 0f) { Kill(ref e); continue; }
                }

                if (Train.Mode == TrainMode.Dead) return;
            }
        }

        void StepPickups(float dt)
        {
            for (int i = 0; i < PickupCount; i++)
            {
                ref Pickup p = ref Pickups[i];
                if (!p.Alive) continue;

                p.Life -= dt;
                p.Spin += dt * 3.4f;
                if (p.Life <= 0f) { p.Alive = false; continue; }

                float gap = p.S - Train.S;
                float dist = (float)Math.Sqrt(gap * gap + p.X * p.X);

                if (dist < SimConfig.PickupMagnet)
                {
                    float k = Math.Min(1f, dt * 9f);
                    p.S += (Train.S - p.S) * k;
                    p.X += (0f - p.X) * k;
                    // Pull slightly ahead of the nose so gems never sit under the train.
                    p.S = Math.Max(p.S, Train.S + 0.1f);
                    dist = (float)Math.Sqrt((p.S - Train.S) * (p.S - Train.S) + p.X * p.X);
                }
                else
                {
                    // Outside the magnet they still creep toward the train: loot dropped
                    // in front of a stopped train must never be stranded out of reach.
                    float pull = SimConfig.PickupDrift * dt;
                    if (gap > 0f)
                    {
                        p.S -= Math.Min(pull, gap);
                        p.X -= Math.Sign(p.X) * Math.Min(pull * 0.5f, Math.Abs(p.X));
                    }
                }

                dist = (float)Math.Sqrt((p.S - Train.S) * (p.S - Train.S) + p.X * p.X);

                if (dist < SimConfig.PickupTake)
                {
                    p.Alive = false;

                    if (p.Kind == PickupKind.HealthPack)
                    {
                        Train.Hp = Math.Min(Train.MaxHp, Train.Hp + p.Value);
                    }
                    else
                    {
                        Train.Xp += p.Value;
                        Train.XpGems++;
                        TryLevelUp();
                    }

                    Events.Add(new SimEvent { Kind = SimEventKind.PickupTaken, EnemyId = p.Id, ToS = p.S, ToX = p.X });
                }
            }
        }

        void TryLevelUp()
        {
            while (Train.Xp >= Train.XpToNext)
            {
                Train.Xp -= Train.XpToNext;
                Train.Level++;
                Train.XpToNext = SimConfig.XpBase + SimConfig.XpPerLevel * Train.Level;
                PendingOffers++;
                Emit(SimEventKind.LevelUp, 0f);
            }

            if (!OfferPending && PendingOffers > 0)
            {
                PendingOffers--;
                BeginOffer(OfferReason.LevelUp);
            }
        }

        void StepGuns(float dt)
        {
            if (!GunsEnabled) return;
            if (!Train.Firing) return;

            float rate = Train.FireRateMultiplier;
            int volley = 1 + Train.CardStacksOf(CardId.Multishot);
            int pierce = Train.CardStacksOf(CardId.PiercingRounds);

            for (int g = 0; g < Train.Guns && g < _gunCd.Length; g++)
            {
                _gunCd[g] -= dt * rate;
                if (_gunCd[g] > 0f) continue;

                int hits = 0;
                Array.Clear(_hitIds, 0, _hitIds.Length);

                for (int n = 0; n < volley + pierce && hits < _hitIds.Length; n++)
                {
                    int enemy;
                    int blockade;
                    if (!FindNearestTarget(out enemy, out blockade)) break;

                    float dmg = SimConfig.GunDamage * Train.StageDamageScale
                                * (n == 0 ? 1f : SimConfig.PierceDamageMul);

                    if (blockade >= 0)
                    {
                        DamageBlockade(blockade, dmg, g);
                    }
                    else
                    {
                        ref Enemy e = ref Enemies[enemy];
                        e.Hp -= dmg;
                        e.Flash = 1f;
                        _hitIds[hits] = e.Id;

                        float igniteChance = 0.2f * Train.CardStacksOf(CardId.IncendiaryShells);
                        if (igniteChance > 0f && _rng.Next01() < igniteChance && e.Burn <= 0f)
                        {
                            e.Burn = 3f;
                            e.BurnDps = 8f;
                        }

                        Events.Add(new SimEvent
                        {
                            Kind = SimEventKind.Shot,
                            EnemyId = e.Id,
                            FromS = Train.S,
                            FromX = _gunOffsetX[g],
                            ToS = e.S,
                            ToX = e.X,
                            Damage = dmg,
                            GunIndex = g
                        });

                        if (e.Hp <= 0f) Kill(ref e);
                    }

                    hits++;
                }

                if (hits > 0) _gunCd[g] = SimConfig.GunInterval;
            }
        }

        /// <summary>Nearest live target ahead: an enemy or a blockade, whichever is closer.</summary>
        bool FindNearestTarget(out int enemyIndex, out int blockadeIndex)
        {
            enemyIndex = -1;
            blockadeIndex = -1;
            float best = float.MaxValue;

            for (int i = 0; i < EnemyCount; i++)
            {
                ref Enemy e = ref Enemies[i];
                if (!e.Alive) continue;
                if (IsAlreadyHit(e.Id)) continue;
                float gap = e.S - Train.S;
                if (gap < -SimConfig.RearTargetRange || gap > SimConfig.GunRange) continue;
                if (gap < best) { best = gap; enemyIndex = i; }
            }

            for (int i = 0; i < BlockadeCount; i++)
            {
                ref Blockade b = ref Blockades[i];
                if (!b.Alive) continue;
                float gap = b.S - Train.S;
                if (gap < -1f || gap > SimConfig.GunRange) continue;
                if (gap < best) { best = gap; blockadeIndex = i; enemyIndex = -1; }
            }

            return enemyIndex >= 0 || blockadeIndex >= 0;
        }

        bool IsAlreadyHit(int id)
        {
            for (int i = 0; i < _hitIds.Length; i++) if (_hitIds[i] == id) return true;
            return false;
        }

        void StepProgress()
        {
            if (Train.Mode == TrainMode.Dead || Train.Mode == TrainMode.Cleared) return;
            if (Train.S < StageLength) return;

            Train.S = StageLength;
            Train.Speed = 0f;
            Train.Mode = TrainMode.Cleared;
            Emit(SimEventKind.Cleared, 0f);
            BeginOffer(OfferReason.StageClear);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Distance of the nearest live blockade still ahead of the nose.</summary>
        float BlockadeAheadS()
        {
            float best = float.MaxValue;
            for (int i = 0; i < BlockadeCount; i++)
            {
                ref Blockade b = ref Blockades[i];
                if (!b.Alive) continue;
                if (b.S <= Train.S + 0.5f) continue;
                if (b.S < best) best = b.S;
            }
            return best;
        }

        public bool AnyBlockadeAhead()
        {
            return BlockadeAheadS() < float.MaxValue;
        }

        /// <summary>Auto-targeting priority: strictly the nearest live enemy ahead.</summary>
        public int NearestAhead()
        {
            int best = -1;
            float bestGap = float.MaxValue;

            for (int i = 0; i < EnemyCount; i++)
            {
                if (!Enemies[i].Alive) continue;
                float gap = Enemies[i].S - Train.S;
                if (gap < -SimConfig.RearTargetRange) continue;
                if (gap < bestGap) { bestGap = gap; best = i; }
            }
            return best;
        }

        void DamageTrain(float amount, int sourceId)
        {
            Train.Hp -= amount;
            Events.Add(new SimEvent { Kind = SimEventKind.ContactHit, EnemyId = sourceId, Damage = amount });

            if (Train.Hp <= 0f)
            {
                Train.Hp = 0f;
                Train.Mode = TrainMode.Dead;
                Train.Speed = 0f;
                Emit(SimEventKind.Died, 0f);
            }
        }

        void DamageBlockade(int index, float amount, int gunIndex)
        {
            ref Blockade b = ref Blockades[index];
            b.Hp -= amount;
            b.Flash = 1f;

            Events.Add(new SimEvent
            {
                Kind = SimEventKind.BlockadeHit,
                BlockadeId = b.Id,
                FromS = Train.S,
                ToS = b.S,
                ToX = b.X,
                Damage = amount,
                GunIndex = gunIndex
            });

            if (b.Hp <= 0f)
            {
                b.Alive = false;
                b.Hp = 0f;
                Train.BlockadesBroken++;
                Events.Add(new SimEvent
                {
                    Kind = SimEventKind.BlockadeBroken,
                    BlockadeId = b.Id,
                    ToS = b.S,
                    ToX = b.X
                });
            }
        }

        void Kill(ref Enemy e)
        {
            e.Alive = false;
            e.Hp = 0f;
            Train.Kills++;

            Events.Add(new SimEvent
            {
                Kind = SimEventKind.Kill, EnemyId = e.Id, EnemyKind = e.Kind, ToS = e.S, ToX = e.X
            });

            // Bloaters take the neighbours with them (and chip the train if it is close).
            var stat = SimConfig.Stat(e.Kind);
            if (stat.BlastRadius > 0f)
            {
                Events.Add(new SimEvent
                {
                    Kind = SimEventKind.Blast, EnemyId = e.Id, EnemyKind = e.Kind, ToS = e.S, ToX = e.X,
                    Damage = stat.BlastDamage
                });

                for (int i = 0; i < EnemyCount; i++)
                {
                    if (!Enemies[i].Alive || Enemies[i].Id == e.Id) continue;
                    float ds = Enemies[i].S - e.S;
                    float dx = Enemies[i].X - e.X;
                    if (ds * ds + dx * dx > stat.BlastRadius * stat.BlastRadius) continue;
                    var other = Enemies[i];
                    other.Hp -= stat.BlastDamage;
                    other.Flash = 1f;
                    Enemies[i] = other;   // the update loop buries anything left at <= 0
                }

                if (Math.Abs(e.S - Train.S) <= stat.BlastRadius)
                    DamageTrain(stat.BlastDamage * 0.5f, e.Id);
            }

            // XP gem, and occasionally a health pack instead of the gem drop.
            if (_rng.Next01() < SimConfig.HealthPackChance)
                DropPickup(PickupKind.HealthPack, e.S, e.X, Train.MaxHp * SimConfig.HealthPackHeal);
            else
                DropPickup(PickupKind.XpGem, e.S, e.X, XpForKind(e.Kind));
        }

        static float XpForKind(EnemyKind kind) => SimConfig.Stat(kind).Xp;

        void DropPickup(PickupKind kind, float s, float x, float value)
        {
            int slot = -1;
            for (int i = 0; i < PickupCount; i++) if (!Pickups[i].Alive) { slot = i; break; }
            if (slot < 0)
            {
                if (PickupCount >= SimConfig.MaxPickups) return;
                slot = PickupCount++;
            }

            Pickups[slot] = new Pickup
            {
                Id = _nextId++,
                Kind = kind,
                S = s,
                X = x,
                Value = value,
                Life = SimConfig.PickupLifetime,
                Alive = true
            };
        }

        void Emit(SimEventKind kind, float damage, int enemyId = 0)
        {
            Events.Add(new SimEvent { Kind = kind, Damage = damage, EnemyId = enemyId });
        }

        // ---------------------------------------------------------------- content

        public int Spawn(EnemyKind kind, float s, float x)
        {
            if (EnemyCount >= SimConfig.MaxEnemies) return -1;

            float hpScale = 1f + SimConfig.EnemyHpScalePerStage * (Train.Stage - 1);

            Enemy e = new Enemy
            {
                Id = _nextId++,
                Kind = kind,
                S = s,
                X = x,
                Alive = true,
                AttackCd = 0.6f
            };

            var stat = SimConfig.Stat(kind);
            e.MaxHp = e.Hp = stat.Hp * hpScale;
            e.Speed = stat.Speed;
            e.Damage = stat.Damage;
            e.AttackInterval = stat.AttackInterval;

            Enemies[EnemyCount] = e;
            return EnemyCount++;
        }

        public int SpawnBlockade(float s, BlockadeKind? forced = null)
        {
            if (BlockadeCount >= Blockades.Length) return -1;

            BlockadeKind kind = forced ?? (BlockadeKind)_rng.Range(0, 3);
            float hp = kind == BlockadeKind.Barrier ? SimConfig.BarrierHp
                     : kind == BlockadeKind.Barrels ? SimConfig.BarrelsHp
                     : SimConfig.WreckHp;
            hp *= 1f + SimConfig.EnemyHpScalePerStage * (Train.Stage - 1);

            Blockades[BlockadeCount] = new Blockade
            {
                Id = _nextId++,
                Kind = kind,
                S = s,
                X = 0f,
                Hp = hp,
                MaxHp = hp,
                Alive = true
            };
            return BlockadeCount++;
        }

        public int AliveCount()
        {
            int n = 0;
            for (int i = 0; i < EnemyCount; i++) if (Enemies[i].Alive) n++;
            return n;
        }
    }
}

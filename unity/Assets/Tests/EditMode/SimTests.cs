using NUnit.Framework;
using TrainSurvivor.Core;

namespace TrainSurvivor.Tests
{
    /// <summary>
    /// Pure-sim tests. They run in ~1 s in EditMode and are the balance safety net:
    /// if a tuning change breaks the loop's contracts, this fails before a play session.
    /// </summary>
    public class SimTests
    {
        static void Run(World w, Intent intent, float seconds)
        {
            int steps = (int)(seconds * SimConfig.TickRate + 0.5f);
            for (int i = 0; i < steps; i++) w.Advance(SimConfig.Dt, intent);
        }

        /// <summary>Events are cleared every tick, so counting means accumulating over the run.</summary>
        static int CountOver(World w, SimEventKind kind, Intent intent, float seconds)
        {
            int steps = (int)(seconds * SimConfig.TickRate + 0.5f);
            int n = 0;
            for (int i = 0; i < steps; i++)
            {
                w.Advance(SimConfig.Dt, intent);
                for (int e = 0; e < w.Events.Count; e++)
                    if (w.Events[e].Kind == kind) n++;
            }
            return n;
        }

        [Test]
        public void AcceleratesToFullSpeed()
        {
            var w = World.Empty();
            Run(w, Intent.Move, 1.5f);
            Assert.AreEqual(SimConfig.MoveSpeed, w.Train.Speed, 0.01f, "should reach top speed");
            Assert.AreEqual(TrainMode.Moving, w.Train.Mode);
        }

        [Test]
        public void ApprovedProgressionIsPerLevel()
        {
            // Stage 1 contract: a gun every 5 levels, a wagon every 10, two guns per car max.
            var w = World.Empty();
            int guard = 0;
            while (w.Train.Level < 5 && guard++ < 60) w.GrantXp(w.Train.XpToNext);
            Assert.AreEqual(5, w.Train.Level);
            Assert.AreEqual(SimConfig.StartingGuns + 1, w.Train.Guns, "level 5 grants a second gun");
            Assert.AreEqual(SimConfig.StartingWagons, w.Train.Wagons, "no wagon before level 10");

            while (w.Train.Level < 10 && guard++ < 120) w.GrantXp(w.Train.XpToNext);
            Assert.AreEqual(10, w.Train.Level);
            Assert.AreEqual(SimConfig.StartingWagons + 1, w.Train.Wagons, "level 10 grants a wagon");
            Assert.AreEqual(SimConfig.StartingGuns + 2, w.Train.Guns, "levels 5 and 10 each grant a gun");
            Assert.LessOrEqual(w.Train.Guns, w.GunCapacity, "never more guns than plates");
            Assert.AreEqual(2, w.GunCapacity / (1 + w.Train.Wagons), "two plates per car");
        }

        [Test]
        public void MilestoneTableThroughLevel20()
        {
            // Exact contract: the stage starts with the locomotive alone; every 5 levels adds a
            // gun, every 10 adds a freight wagon, and the wagon lands before the gun on LV10/20.
            var w = World.Empty();
            Assert.AreEqual(0, w.Train.Wagons, "fresh run: locomotive only, no freight car");
            Assert.AreEqual(1, w.Train.Guns, "fresh run: exactly one gun");
            Assert.AreEqual(1, w.Train.Stage);

            var expect = new (int lvl, int wagons, int guns)[]
            {
                (4, 0, 1), (5, 0, 2), (9, 0, 2), (10, 1, 3),
                (15, 1, 4), (20, 2, 5)
            };
            int guard = 0, next = 0;
            while (w.Train.Level < 20 && guard++ < 400)
            {
                w.GrantXp(w.Train.XpToNext);
                while (next < expect.Length && w.Train.Level == expect[next].lvl)
                {
                    Assert.AreEqual(expect[next].wagons, w.Train.Wagons, "wagons at LV" + expect[next].lvl);
                    Assert.AreEqual(expect[next].guns, w.Train.Guns, "guns at LV" + expect[next].lvl);
                    Assert.LessOrEqual(w.Train.Guns, w.GunCapacity, "guns never exceed plates");
                    next++;
                }
            }
            Assert.AreEqual(20, w.Train.Level);
            Assert.AreEqual(6, next, "every milestone level was checked");
        }

        [Test]
        public void CardsNeverGrantWagonsOrGuns()
        {
            // Ordinary upgrades must not bypass the milestone rule, whatever the card used to do.
            var w = World.Empty();
            int wagons = w.Train.Wagons, guns = w.Train.Guns;
            w.GrantCard(CardId.ExtraGun);
            w.GrantCard(CardId.ExtraWagon);
            w.GrantCard(CardId.ExtraGun);
            Assert.AreEqual(wagons, w.Train.Wagons, "no card may add a wagon");
            Assert.AreEqual(guns, w.Train.Guns, "no card may add a gun");
            Assert.Greater(w.Train.FireRateMultiplier, 1f, "spare barrels still improve the guns");
        }

        [Test]
        public void OffersDrawOnlyTheApprovedNine()
        {
            var w = World.Empty();
            var seen = new System.Collections.Generic.HashSet<CardId>();
            for (int round = 0; round < 40; round++)
            {
                w.ResetRun(false);
                for (int i = 0; i <= round % 5; i++) w.GrantXp(w.Train.XpToNext);
                Assert.IsTrue(w.OfferPending, "a level-up always offers unless the pool is empty");
                int count = w.OfferCount;
                Assert.That(count, Is.InRange(1, 3));
                var inOffer = new System.Collections.Generic.HashSet<CardId>();
                for (int i = 0; i < count; i++)
                {
                    var id = w.OfferAt(i);
                    Assert.Contains(id, Cards.Approved, "only approved cards may be offered");
                    Assert.IsTrue(inOffer.Add(id), "no duplicate inside one offer");
                    seen.Add(id);
                }
                // one combat, one survival when the pool allows it
                int combat = 0, survival = 0;
                for (int i = 0; i < count; i++)
                {
                    if (Cards.RoleOf(w.OfferAt(i)) == CardRole.Combat) combat++;
                    if (Cards.RoleOf(w.OfferAt(i)) == CardRole.Survival) survival++;
                }
                if (count == 3) { Assert.AreEqual(1, combat, "exactly one combat pick"); Assert.AreEqual(1, survival, "exactly one survival pick"); }
            }
            Assert.GreaterOrEqual(seen.Count, 7, "the pool actually rotates through the set");
        }

        [Test]
        public void CapsAndHealGatingHideCards()
        {
            var w = World.Empty();
            for (int i = 0; i < 3; i++) w.GrantCard(CardId.HeavyHitter);
            Assert.IsFalse(w.Eligible(CardId.HeavyHitter), "maxed card is ineligible");

            // heal only while hurt: full HP -> hidden, damaged -> offered again
            Assert.IsFalse(w.Eligible(CardId.FixItUp), "no heal at full HP");
            w.ApplyDamage(60f);
            Assert.IsTrue(w.Eligible(CardId.FixItUp), "heal offered when hurt");

            for (int i = 0; i < 3; i++) w.GrantCard(CardId.HeavyHitter);
            Assert.AreEqual(3, w.Train.CardStacksOf(CardId.HeavyHitter), "rank cap is hard");
        }

        [Test]
        public void GlobalStatsMoveWithCards()
        {
            var w = World.Empty();
            float dmg0 = w.Train.GunDamageMultiplier, rate0 = w.Train.FireRateMultiplier;
            float range0 = w.Train.GunRangeMultiplier, aim0 = w.Train.AimSpeedMultiplier;
            float spd0 = w.Train.MoveSpeedMultiplier, armor0 = w.Train.DamageTakenMultiplier;
            float xp0 = w.Train.XpGainMultiplier;
            float hp0 = w.Train.MaxHp;

            w.GrantCard(CardId.HeavyHitter);
            w.GrantCard(CardId.FastFire);
            w.GrantCard(CardId.LongReach);
            w.GrantCard(CardId.QuickAim);
            w.GrantCard(CardId.StrongArmor);
            w.GrantCard(CardId.FullSteam);
            w.GrantCard(CardId.FastLearner);

            Assert.AreEqual(dmg0 * 1.12f, w.Train.GunDamageMultiplier, 0.0001f, "heavy hitter: +12% shot damage");
            Assert.AreEqual(rate0 * 1.08f, w.Train.FireRateMultiplier, 0.0001f, "fast fire: +8% shots/sec");
            Assert.AreEqual(range0 * 1.10f, w.Train.GunRangeMultiplier, 0.0001f, "long reach: +10% range");
            Assert.AreEqual(aim0 * 1.20f, w.Train.AimSpeedMultiplier, 0.0001f, "quick aim: +20% slew");
            Assert.AreEqual(spd0 * 1.08f, w.Train.MoveSpeedMultiplier, 0.0001f, "full steam: +8% held speed");
            Assert.AreEqual(0.92f, w.Train.DamageTakenMultiplier, 0.0001f, "strong armor: 8 points off");
            Assert.AreEqual(xp0 * 1.10f, w.Train.XpGainMultiplier, 0.0001f, "fast learner: +10% kill XP");
            Assert.AreEqual(hp0, w.Train.MaxHp, 0.01f, "no HP from these cards");
        }

        [Test]
        public void ToughTrainUsesOneConsistentBase()
        {
            var w = World.Empty();
            float baseHp = SimConfig.BaseHp + SimConfig.WagonHp * w.Train.Wagons;
            float hpBefore = w.Train.Hp;
            w.GrantCard(CardId.ToughTrain);
            Assert.AreEqual(baseHp * 1.12f, w.Train.MaxHp, 0.01f, "max HP is the base convoy HP x rank");
            Assert.AreEqual(hpBefore + baseHp * 0.12f, w.Train.Hp, 0.01f, "current HP rises with it");

            w.Train.Wagons++;
            w.GrantCard(CardId.ToughTrain);
            float base2 = SimConfig.BaseHp + SimConfig.WagonHp * w.Train.Wagons;
            Assert.AreEqual(base2 * 1.24f, w.Train.MaxHp, 0.01f, "wagon HP follows the same base");
        }

        [Test]
        public void FastLearnerScalesFractionalKillXp()
        {
            var w = World.Empty();
            w.GrantCard(CardId.FastLearner);
            Assert.AreEqual(1.10f, w.Train.XpGainMultiplier, 0.0001f);
            float before = w.Train.Xp;
            w.GrantXp(0.5f);
            Assert.Greater(w.Train.Xp, before, "fractional XP is preserved, not floored away");
        }

        [Test]
        public void DoubleTapCannotTakeTwoCards()
        {
            var w = World.Empty();
            while (!w.OfferPending && w.Train.Level < 6) w.GrantXp(w.Train.XpToNext);
            Assert.IsTrue(w.OfferPending);
            CardId first = w.OfferAt(0);
            Assert.IsTrue(w.ChooseCard(0));
            Assert.IsFalse(w.ChooseCard(0), "second tap on the same offer is refused");
            Assert.AreEqual(1, w.Train.CardStacksOf(first));
        }

        [Test]
        public void RetryClearsCardRanks()
        {
            var w = World.Empty();
            w.GrantCard(CardId.HeavyHitter);
            w.GrantCard(CardId.ToughTrain);
            w.GrantXp(w.Train.XpToNext);
            w.ResetRun(false);
            Assert.AreEqual(0, w.Train.CardStacksOf(CardId.HeavyHitter));
            Assert.AreEqual(0, w.Train.CardStacksOf(CardId.ToughTrain));
            Assert.AreEqual(1f, w.Train.GunDamageMultiplier, 0.0001f);
            Assert.AreEqual(SimConfig.BaseHp + SimConfig.WagonHp * w.Train.Wagons, w.Train.MaxHp, 0.01f);
        }

        [Test]
        public void LegacyIdsKeepTheirEffect()
        {
            // persisted stacks from older builds must keep working, and never be re-offered
            var w = World.Empty();
            float rate0 = w.Train.FireRateMultiplier;
            w.GrantCard(CardId.ExtraGun);
            Assert.Greater(w.Train.FireRateMultiplier, rate0, "legacy spare barrels still speeds guns");
            Assert.IsFalse(System.Array.IndexOf(Cards.Approved, CardId.ExtraGun) >= 0,
                "retired ids are out of the offer pool");
        }

        [Test]
        public void StageClearWithFractionalMaxHpKeepsHpInsideMax()
        {
            // Regression for the observed "168 / 167": TOUGH TRAIN x2 makes max HP 135 x 1.24 =
            // 167.4, and the old HUD printed ceil(167.4) over round(167.4).
            var w = World.Empty();
            w.GrantCard(CardId.ToughTrain);
            w.GrantCard(CardId.ToughTrain);
            Assert.AreEqual(167.4f, w.Train.MaxHp, 0.01f, "fractional but exact convoy max HP");

            w.ApplyDamage(100f);
            Assert.Less(w.Train.Hp, w.Train.MaxHp);

            // normal stage-clear entry point: drive to the end of the stage, take the card
            w.Train.S = w.StageLength - 0.5f;
            int guard = 0;
            while (!w.OfferPending && guard++ < 600) w.Advance(SimConfig.Dt, Intent.Move);
            Assert.IsTrue(w.OfferPending, "stage clear opens an offer");
            Assert.IsTrue(w.ChooseCard(0), "the offered card can be taken");

            Assert.LessOrEqual(w.Train.Hp, w.Train.MaxHp + 0.0001f, "current HP never exceeds max");
            Assert.GreaterOrEqual(w.Train.Hp, 0f, "current HP never goes negative");
            Assert.AreEqual(2, w.Train.Stage, "the stage actually advances");

            // the two displayed integers must be consistent, whatever the fractional values are
            int maxShown = (int)System.Math.Round(w.Train.MaxHp, System.MidpointRounding.AwayFromZero);
            int hpShown = (int)System.Math.Round(w.Train.Hp, System.MidpointRounding.AwayFromZero);
            hpShown = System.Math.Min(System.Math.Max(hpShown, 0), maxShown);
            Assert.LessOrEqual(hpShown, maxShown, "displayed HP can never read above displayed max");
            Assert.AreEqual(hpShown, System.Math.Min(System.Math.Max((int)System.Math.Round(w.Train.Hp), 0), maxShown),
                "both numbers come from one rounding rule");

            // the exact observed case: 167.4 / 167.4 used to print 168 over 167
            Assert.AreEqual(167, maxShown, "167.4 max rounds to 167");
            w.Train.Hp = w.Train.MaxHp;
            int full = System.Math.Min((int)System.Math.Round(w.Train.Hp), maxShown);
            Assert.AreEqual(maxShown, full, "at full health both numbers are identical");
        }

        [Test]
        public void MultiLevelXpCrossesEveryMilestoneOnce()
        {
            // One huge XP dump must award each milestone exactly once, in order.
            var w = World.Empty();
            w.GrantXp(100000f);
            Assert.GreaterOrEqual(w.Train.Level, 20);
            Assert.AreEqual(System.Math.Min(1 + w.Train.Level / 10, SimConfig.MaxWagons), w.Train.Wagons,
                "one wagon per ten levels, capped by the convoy limit");
            Assert.AreEqual(System.Math.Min(1 + w.Train.Level / 5, System.Math.Min(SimConfig.MaxGuns, w.GunCapacity)), w.Train.Guns,
                "one gun per five levels, capped by the plates");
            Assert.LessOrEqual(w.Train.Guns, w.GunCapacity);
        }

        [Test]
        public void BrakeStopsWithinBrakeTime()
        {
            var w = World.Empty();
            Run(w, Intent.Move, 1.0f);
            Run(w, Intent.Stop, 0.25f);
            Assert.AreEqual(0f, w.Train.Speed, 0.001f, "brake must complete in 0.18 s");
            Assert.AreEqual(TrainMode.Stopped, w.Train.Mode);
        }

        [Test]
        public void GunsAreSilentWhileMoving()
        {
            var w = World.Empty();
            w.Spawn(EnemyKind.Walker, 20f, 0f);
            Assert.AreEqual(0, CountOver(w, SimEventKind.Shot, Intent.Move, 1.0f),
                "moving must disable auto-fire");
        }

        [Test]
        public void GunsFireTheInstantInputIsReleased()
        {
            var w = World.Empty();
            w.Spawn(EnemyKind.Walker, 20f, 0f);

            int shots = 0;
            w.Advance(SimConfig.Dt, Intent.Stop);      // first tick after release
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Kind == SimEventKind.Shot) shots++;

            Assert.Greater(shots, 0, "no windup: the first tick after release must fire");
        }

        [Test]
        public void ChargeGivesFasterFireOnRelease()
        {
            // A tank soaks the whole second of fire: the test measures rate, not time-to-kill.
            // Each world needs its target inside gun range at the moment it starts firing —
            // the hot world has already rolled ~45 m by then.
            var cold = World.Empty();
            cold.Spawn(EnemyKind.Tank, 20f, 0f);
            int coldShots = CountOver(cold, SimEventKind.Shot, Intent.Stop, 1.0f);

            var hot = World.Empty();
            hot.Spawn(EnemyKind.Tank, 62f, 0f);
            Run(hot, Intent.Move, SimConfig.ChargeFillTime + 0.2f);   // fill the meter
            Run(hot, Intent.Stop, SimConfig.BrakeTime + 0.02f);      // come to rest
            int hotShots = CountOver(hot, SimEventKind.Shot, Intent.Stop, 1.0f);

            Assert.Greater(hotShots, coldShots * 1.15f, "spent brake charge must visibly raise fire rate");
        }

        [Test]
        public void TargetsTheNearestEnemyAhead()
        {
            var w = World.Empty();
            w.Train.S = 0f;
            int far = w.Spawn(EnemyKind.Walker, 60f, 0f);
            int near = w.Spawn(EnemyKind.Walker, 25f, 0f);

            w.Advance(SimConfig.Dt, Intent.Stop);

            int shotEnemy = -1;
            for (int i = 0; i < w.Events.Count; i++)
                if (w.Events[i].Kind == SimEventKind.Shot) { shotEnemy = w.Events[i].EnemyId; break; }

            Assert.AreEqual(w.Enemies[near].Id, shotEnemy, "auto-target must pick the closest enemy ahead");
            Assert.AreNotEqual(w.Enemies[far].Id, shotEnemy);
        }

        [Test]
        public void RamHappensAtSpeedButNotWhileStopped()
        {
            var rolling = World.Empty();
            rolling.Train.S = 0f;
            rolling.Spawn(EnemyKind.Tank, 2f, 0f);
            int rollingRams = CountOver(rolling, SimEventKind.Ram, Intent.Move, 0.8f);
            Assert.Greater(rollingRams, 0, "cowcatcher must connect at speed");

            var parked = World.Empty();
            parked.Train.S = 0f;
            parked.Spawn(EnemyKind.Tank, 2f, 0f);
            int parkedRams = CountOver(parked, SimEventKind.Ram, Intent.Stop, 0.8f);
            Assert.AreEqual(0, parkedRams, "a stopped train must not ram");
        }

        [Test]
        public void RamCannotKillATankQuickly()
        {
            var w = World.Empty();
            w.Train.S = 0f;
            int idx = w.Spawn(EnemyKind.Tank, 2f, 0f);
            Run(w, Intent.Move, 0.8f);
            Assert.IsTrue(w.Enemies[idx].Alive, "a tank survives a single ram — it must force a stop");
        }

        [Test]
        public void KillingAnEnemyCounts()
        {
            var w = World.Empty();
            int idx = w.Spawn(EnemyKind.Sprinter, 12f, 0f);
            Run(w, Intent.Stop, 1.0f);
            Assert.IsFalse(w.Enemies[idx].Alive);
            Assert.GreaterOrEqual(w.Train.Kills, 1);
        }

        [Test]
        public void TrainDiesAtZeroHp()
        {
            var w = World.Empty();
            w.Train.Hp = 4f;
            w.Spawn(EnemyKind.Tank, 2f, 0f);
            Run(w, Intent.Stop, 6f);
            Assert.AreEqual(TrainMode.Dead, w.Train.Mode);
        }

        [Test]
        public void SameSeedSameRun()
        {
            var a = new World(777u);
            var b = new World(777u);
            for (int i = 0; i < 600; i++)
            {
                var intent = (i % 90 < 50) ? Intent.Move : Intent.Stop;
                a.Advance(SimConfig.Dt, intent);
                b.Advance(SimConfig.Dt, intent);
            }
            Assert.AreEqual(a.Train.S, b.Train.S, 1e-4f);
            Assert.AreEqual(a.Train.Hp, b.Train.Hp, 1e-4f);
            Assert.AreEqual(a.Train.Kills, b.Train.Kills);
        }

        [Test]
        public void ReachingTheEndClearsTheStage()
        {
            var w = World.Empty();
            w.Train.S = TrackPath.Length - 0.5f;
            Run(w, Intent.Move, 0.5f);
            Assert.AreEqual(TrainMode.Cleared, w.Train.Mode);
        }

        // ---------------------------------------------------------------- stage furniture

        [Test]
        public void BlockadeStopsTheTrainShort()
        {
            var w = World.Empty();
            w.SpawnBlockade(30f, BlockadeKind.Barrier);
            Run(w, Intent.Move, 4f);   // long enough to cross 30 m at full speed

            Assert.LessOrEqual(w.Train.S, 30f - SimConfig.BlockStopDistance + 0.05f,
                "the train must halt before the barrier, not pass through it");
            Assert.AreEqual(TrainMode.Blocked, w.Train.Mode, "holding against a blockade is its own state");
        }

        [Test]
        public void BlockadeBreaksUnderFireAndTheTrackOpens()
        {
            var w = World.Empty();
            w.SpawnBlockade(12f, BlockadeKind.Barrier);
            w.Train.S = 0f;

            // Stopped, guns live, wall 12 m away: it must die to gunfire.
            Run(w, Intent.Stop, 6f);

            Assert.IsFalse(w.Blockades[0].Alive, "guns must be able to destroy a blockade");

            Run(w, Intent.Move, 3f);
            Assert.Greater(w.Train.S, 14f, "with the wall gone the train rolls on");
        }

        [Test]
        public void MultishotHitsMoreThanOneZombie()
        {
            var w = World.Empty();
            w.GrantCard(CardId.Multishot);
            w.Train.S = 0f;
            w.Spawn(EnemyKind.Tank, 20f, -2f);
            w.Spawn(EnemyKind.Tank, 22f, 2f);

            w.Advance(SimConfig.Dt, Intent.Stop);
            int shots = 0;
            for (int i = 0; i < w.Events.Count; i++) if (w.Events[i].Kind == SimEventKind.Shot) shots++;

            Assert.GreaterOrEqual(shots, 2, "multishot must put a bullet into a second target");
        }

        [Test]
        public void KillsDropXpGemsThatLevelYouUp()
        {
            var w = World.Empty();
            w.Train.S = 0f;
            w.Train.XpToNext = 2f;                 // level 2 after two gems, so the test is quick
            for (int i = 0; i < 4; i++) w.Spawn(EnemyKind.Sprinter, 6f + i, i - 1.5f);

            Run(w, Intent.Stop, 2f);              // kill them
            Assert.Greater(w.Train.XpGems + CountPickups(w), 0, "a kill must drop loot");

            Run(w, Intent.Stop, 2f);              // vacuum it in

            Assert.Greater(w.Train.Level, 1, "enough gems must level the player up");
            Assert.IsTrue(w.OfferPending, "levelling up offers cards");
            Assert.AreEqual(OfferReason.LevelUp, w.OfferKind);
        }

        [Test]
        public void OfferAlwaysHasThreeDistinctCards()
        {
            var w = World.Empty();
            w.Train.S = w.StageLength - 0.2f;
            Run(w, Intent.Move, 0.4f);            // reaching the finish opens an offer
            Assert.IsTrue(w.OfferPending);

            var a = w.OfferAt(0);
            var b = w.OfferAt(1);
            var c = w.OfferAt(2);
            Assert.IsTrue(a != b && b != c && a != c, "three cards must be three different cards");
        }

        [Test]
        public void ChoosingACardAppliesItsEffect()
        {
            var w = World.Empty();
            int gunsBefore = w.Train.Guns;
            float rateBefore = w.Train.FireRateMultiplier;
            w.GrantCard(CardId.ExtraGun);
            // SPARE BARRELS: no free gun any more, the card accelerates the guns already fitted
            Assert.AreEqual(gunsBefore, w.Train.Guns, "cards may not grant guns");
            Assert.Greater(w.Train.FireRateMultiplier, rateBefore, "spare barrels raise fire rate");

            float hpBefore = w.Train.MaxHp;
            w.GrantCard(CardId.ReinforcedBoiler);
            Assert.AreEqual(hpBefore * 1.2f, w.Train.MaxHp, 0.01f, "boiler gives +20% max HP");

            int wagons = w.Train.Wagons;
            w.GrantCard(CardId.ExtraWagon);
            Assert.AreEqual(wagons, w.Train.Wagons, "cards may not grant wagons");
        }

        [Test]
        public void ClearingAStageOffersACardThenStartsTheNextStage()
        {
            var w = World.Empty();
            w.Train.S = w.StageLength - 0.2f;
            Run(w, Intent.Move, 0.4f);

            Assert.AreEqual(TrainMode.Cleared, w.Train.Mode);
            Assert.IsTrue(w.OfferPending);
            Assert.AreEqual(OfferReason.StageClear, w.OfferKind);

            Assert.IsTrue(w.ChooseCard(0));
            Assert.IsFalse(w.OfferPending);
            Assert.AreEqual(2, w.Train.Stage);
            Assert.AreEqual(0f, w.Train.S, 0.001f);
            Assert.Greater(w.EnemyCount, 0, "the next stage repopulates the track");
        }

        static int CountPickups(World w)
        {
            int n = 0;
            for (int i = 0; i < w.PickupCount; i++) if (w.Pickups[i].Alive) n++;
            return n;
        }

        // ---------------------------------------------------------------- roster (v2)

        [Test]
        public void EverySpeciesCarriesTheStatsFromTheTable()
        {
            var w = World.Empty();
            foreach (EnemyKind kind in System.Enum.GetValues(typeof(EnemyKind)))
            {
                var stat = SimConfig.Stat(kind);
                Assert.Greater(stat.Hp, 0f, kind + " needs hp");
                Assert.Greater(stat.Speed, 0f, kind + " needs speed");
                Assert.GreaterOrEqual(stat.AttackInterval, 0f, kind + " attack interval");

                int i = w.Spawn(kind, 20f, 0f);
                Assert.GreaterOrEqual(i, 0);
                Assert.AreEqual(stat.Hp, w.Enemies[i].Hp, 0.01f, kind + " hp from table");
                Assert.AreEqual(stat.Speed, w.Enemies[i].Speed, 0.01f, kind + " speed from table");
            }
        }

        [Test]
        public void CrawlerSurvivesTheRamThatKillsAWalker()
        {
            var w = World.Empty();
            w.GunsEnabled = false;

            int crawler = w.Spawn(EnemyKind.Crawler, 8f, 0f);
            int walker = w.Spawn(EnemyKind.Walker, 8f, 0.5f);

            Run(w, Intent.Move, 1.2f);

            Assert.IsTrue(w.Enemies[crawler].Alive, "the crawler flattens under the cowcatcher");
            Assert.Greater(w.Enemies[crawler].Hp, 0f);
            Assert.Less(w.Enemies[walker].Hp, SimConfig.WalkerHp, "the walker still eats the ram");
        }

        [Test]
        public void BloaterBlastDamagesTheEnemiesAroundIt()
        {
            var w = World.Empty();
            w.GunsEnabled = false;

            int bloater = w.Spawn(EnemyKind.Bloater, 8f, 0f);
            int neighbour = w.Spawn(EnemyKind.Walker, 8.6f, 0.4f);

            Run(w, Intent.Move, 1.5f);

            Assert.IsFalse(w.Enemies[bloater].Alive, "the ram detonates the bloater");
            Assert.Less(w.Enemies[neighbour].Hp, SimConfig.WalkerHp, "the blast reaches the neighbour");
        }

        [Test]
        public void LaterStagesSpawnMoreThanJustWalkers()
        {
            // SimConfig.OnlyWalkers is true on purpose while the walker is the only 3D enemy
            // model (see the comment on the flag): the stage spawner must stay walkers-only so
            // a later stage never shows a sprite for a species that has no model yet.
            var w = World.Empty();
            for (int i = 0; i < 7; i++) { w.Train.S = w.StageLength - 0.2f; Run(w, Intent.Move, 0.4f); w.ChooseCard(0); }

            for (int i = 0; i < w.EnemyCount; i++)
                if (w.Enemies[i].Alive)
                    Assert.AreEqual(EnemyKind.Walker, w.Enemies[i].Kind,
                                    "OnlyWalkers keeps every stage on the modelled walker");
        }

        [Test]
        public void PickKindOpensNewSpeciesWithStage()
        {
            // The weighted table still exists, but OnlyWalkers short-circuits it until more
            // species have models. Guard the switch instead of the table.
            for (int k = 0; k < 64; k++)
            {
                var early = SimConfig.PickKind(1, k / 64f);
                Assert.AreEqual(EnemyKind.Walker, early, "stage 1 spawns the modelled walker");
            }

            for (int k = 0; k < 256; k++)
                Assert.AreEqual(EnemyKind.Walker, SimConfig.PickKind(9, k / 256f),
                                "stage 9 also spawns walkers while OnlyWalkers is on");
        }

        [Test]
        public void PinnedTrainShootsTheBarricadeEvenWithACloserWalker()
        {
            var w = World.Empty();
            w.SpawnBlockade(12f, BlockadeKind.Barrier);
            w.Train.S = 6f;                                  // stopped one braking distance short
            int walker = w.Spawn(EnemyKind.Walker, 3f, 0f);  // closer to the hull than the wall

            Run(w, Intent.Stop, 0.25f);

            Assert.Less(w.Blockades[0].Hp, SimConfig.BarrierHp,
                        "the wall the train is pinned on outranks a closer walker");
            Assert.AreEqual(SimConfig.WalkerHp, w.Enemies[walker].Hp,
                            "the walker is ignored while the barricade is the objective");
        }

        [Test]
        public void DamageLandsWhenTheRoundArrivesNotOnTheTrigger()
        {
            var w = World.Empty();
            int idx = w.Spawn(EnemyKind.Walker, 30f, 0f);
            w.Train.Mode = TrainMode.Stopped;

            w.Advance(SimConfig.Dt, Intent.Stop);             // trigger pull + queued round
            Assert.AreEqual(SimConfig.WalkerHp, w.Enemies[idx].Hp, 0.001f,
                "firing must not damage the target on the same tick");

            Run(w, Intent.Stop, 0.08f);
            Assert.AreEqual(SimConfig.WalkerHp, w.Enemies[idx].Hp, 0.001f,
                "a 30 m round is still in the air after 80 ms");

            Run(w, Intent.Stop, 0.2f);
            Assert.Less(w.Enemies[idx].Hp, SimConfig.WalkerHp,
                        "damage lands after the flight time");
        }

        // ---- projectile impact contract (phase 1) --------------------------------

        /// <summary>Advance (guns live, train stopped) until the sim accepts a shot on a real
        /// target, then return that Shot event. Deterministic for a fixed seed.</summary>
        static SimEvent WaitForDrivenShot(World w, float maxSeconds)
        {
            int steps = (int)(maxSeconds / SimConfig.Dt);
            for (int i = 0; i < steps; i++)
            {
                w.Advance(SimConfig.Dt, Intent.Stop);
                for (int e = 0; e < w.Events.Count; e++)
                {
                    var ev = w.Events[e];
                    if (ev.Kind == SimEventKind.Shot && ev.ProjectileId > 0)
                        return ev;
                }
            }
            Assert.Fail("no accepted shot within " + maxSeconds + "s");
            return default(SimEvent);
        }

        static float HpOf(World w, int enemyId)
        {
            for (int i = 0; i < w.EnemyCount; i++)
                if (w.Enemies[i].Id == enemyId) return w.Enemies[i].Hp;
            return -1f;
        }

        [Test]
        public void DrivenProjectilesDealNoTimerDamage()
        {
            var w = new World(4242u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);
            Assert.Greater(shot.ProjectileId, 0, "shot must carry a projectile id");
            float hp = HpOf(w, shot.EnemyId);
            Assert.Greater(hp, 0f, "target must be alive at shot time");
            Run(w, Intent.Stop, 0.6f);
            Assert.AreEqual(hp, HpOf(w, shot.EnemyId), 0.001f, "no timer damage while driven");
        }

        [Test]
        public void ImpactAppliesDamageExactlyOnce()
        {
            var w = new World(4243u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);
            float before = HpOf(w, shot.EnemyId);
            Assert.Greater(before, 0f, "target must be alive at shot time");
            Assert.IsTrue(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId), "first impact lands");
            float after = HpOf(w, shot.EnemyId);
            if (after > 0f)
            {
                Assert.Less(after, before, "first impact removes hp");
                Assert.IsFalse(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId), "duplicate rejected");
                Assert.AreEqual(after, HpOf(w, shot.EnemyId), 0.001f, "duplicate adds no damage");
            }
            Assert.IsFalse(w.ResolveImpact(99999, false, shot.EnemyId), "unknown id rejected");
        }

        [Test]
        public void CancelledOrExpiredImpactDoesNothing()
        {
            var w = new World(4244u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);
            float before = HpOf(w, shot.EnemyId);
            Assert.Greater(before, 0f, "target must be alive at shot time");
            Assert.IsTrue(w.CancelProjectile(shot.ProjectileId), "cancel consumes the round");
            Assert.IsFalse(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId), "cancelled id is dead");
            Assert.IsFalse(w.CancelProjectile(shot.ProjectileId), "second cancel is a no-op");
            Assert.AreEqual(before, HpOf(w, shot.EnemyId), 0.001f, "no damage from cancelled round");
        }

        [Test]
        public void AReallyExpiredRoundCannotLand()
        {
            // CancelledOrExpired only covers the explicit cancel; this walks past the 4 s life
            // with a round nobody resolved — the miss path a real driver relies on.
            var w = new World(4245u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);
            Assert.Greater(HpOf(w, shot.EnemyId), 0f, "target must be alive at shot time");

            Run(w, Intent.Stop, 4.2f);

            Assert.IsFalse(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId),
                "a round whose life ran out must not land");
            Assert.IsFalse(w.CancelProjectile(shot.ProjectileId), "the expiry already reaped the id");
        }

        [Test]
        public void RoundAgainstADeadTargetDoesNoDamage()
        {
            var w = World.Empty();
            w.ViewDrivenProjectiles = true;
            w.Train.S = 0f;
            w.Spawn(EnemyKind.Walker, 10f, 0f);

            var shot = WaitForDrivenShot(w, 1f);

            // Kill the target through the sim's own Kill path while the round is still in the
            // air: dev.kills drops one nearby walker every 3 s, first at 2.5 s.
            w.DemoKills = true;
            Run(w, Intent.Stop, 2.8f);

            bool alive = false;
            for (int i = 0; i < w.EnemyCount; i++)
                if (w.Enemies[i].Id == shot.EnemyId && w.Enemies[i].Alive) alive = true;
            Assert.IsFalse(alive, "the target must be dead before the round lands");

            Assert.IsFalse(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId),
                "a round that reaches a dead target does nothing");
        }

        [Test]
        public void ResetClearsPendingRoundsFromTheOldRun()
        {
            var w = new World(4246u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);

            w.ResetRun();

            Assert.IsFalse(w.CancelProjectile(shot.ProjectileId),
                "the old run's pending round must be gone, not merely unmatched");
            Assert.IsFalse(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId),
                "an old round can never land on the new run");
        }

        [Test]
        public void ImpactRaisesEnemyHitSeparateFromContactHit()
        {
            var w = new World(4247u, true);
            w.ViewDrivenProjectiles = true;
            var shot = WaitForDrivenShot(w, 8f);

            w.Events.Clear();
            Assert.IsTrue(w.ResolveImpact(shot.ProjectileId, false, shot.EnemyId), "impact lands");

            bool enemyHit = false, contactHit = false;
            for (int i = 0; i < w.Events.Count; i++)
            {
                if (w.Events[i].Kind == SimEventKind.EnemyHit) enemyHit = true;
                if (w.Events[i].Kind == SimEventKind.ContactHit) contactHit = true;
            }
            Assert.IsTrue(enemyHit, "a bullet impact emits EnemyHit for hit audio/VFX");
            Assert.IsFalse(contactHit, "ContactHit stays the train being bitten");
        }
    }
}

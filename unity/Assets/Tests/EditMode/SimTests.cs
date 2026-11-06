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
            w.GrantCard(CardId.ExtraGun);
            Assert.AreEqual(gunsBefore + 1, w.Train.Guns, "extra gun must bolt on immediately");

            float hpBefore = w.Train.MaxHp;
            w.GrantCard(CardId.ReinforcedBoiler);
            Assert.AreEqual(hpBefore * 1.2f, w.Train.MaxHp, 0.01f, "boiler gives +20% max HP");

            int wagons = w.Train.Wagons;
            w.GrantCard(CardId.ExtraWagon);
            Assert.AreEqual(wagons + 1, w.Train.Wagons);
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
            var w = World.Empty();
            for (int i = 0; i < 7; i++) { w.Train.S = w.StageLength - 0.2f; Run(w, Intent.Move, 0.4f); w.ChooseCard(0); }

            var kinds = new System.Collections.Generic.HashSet<EnemyKind>();
            for (int i = 0; i < w.EnemyCount; i++) if (w.Enemies[i].Alive) kinds.Add(w.Enemies[i].Kind);

            Assert.Greater(kinds.Count, 1, "stage 8 mixes species, not just walkers");
        }

        [Test]
        public void PickKindOpensNewSpeciesWithStage()
        {
            for (int k = 0; k < 64; k++)
            {
                var early = SimConfig.PickKind(1, k / 64f);
                Assert.IsTrue(early == EnemyKind.Walker || early == EnemyKind.Crawler,
                              "stage 1 is walkers and crawlers only, saw " + early);
            }

            bool sawRimeback = false;
            for (int k = 0; k < 256; k++)
                if (SimConfig.PickKind(9, k / 256f) == EnemyKind.Rimeback) sawRimeback = true;
            Assert.IsTrue(sawRimeback, "stage 9 can roll a rimeback");
        }
    }
}

using NUnit.Framework;
using TrainSurvival.Core;

namespace TrainSurvival.Core.Tests
{
    /// <summary>
    /// SEED で動く通勤モデル（乗車＋降車）のテスト。Unity を一切介さず純粋な C# として走る。
    /// </summary>
    public class CommuteWorldTests
    {
        [Test]
        public void RosterSizeMatchesConfig()
        {
            var world = new CommuteWorld(seed: 1, CommuteConfig.Default());
            Assert.AreEqual(CommuteConfig.Default().PassengerCount, world.Passengers.Count);
        }

        [Test]
        public void EveryDestinationIsAheadOnTheLine()
        {
            var config = CommuteConfig.Default();
            var world = new CommuteWorld(seed: 42, config);

            foreach (Passenger p in world.Passengers)
            {
                Assert.Greater(p.DestinationStation, 0);
                Assert.Less(p.DestinationStation, config.StationCount);
            }
        }

        [Test]
        public void AlightersLeaveHereAndBoardersAreHeadedAhead()
        {
            var world = new CommuteWorld(seed: 7, CommuteConfig.Default());
            int before = world.Passengers.Count;

            StationChange change = world.AdvanceToNextStation();

            foreach (Passenger p in change.Alighting)
            {
                Assert.AreEqual(world.CurrentStation, p.DestinationStation);
            }
            foreach (Passenger p in change.Boarding)
            {
                Assert.Greater(p.DestinationStation, world.CurrentStation);
            }
            Assert.AreEqual(before - change.Alighting.Count + change.Boarding.Count, world.Passengers.Count);
        }

        [Test]
        public void AboardNeverExceedsCapacity()
        {
            var config = CommuteConfig.Default();
            var world = new CommuteWorld(seed: 3, config);

            while (!world.IsEndOfLine)
            {
                world.AdvanceToNextStation();
                Assert.LessOrEqual(world.Passengers.Count, config.PassengerCount);
            }
        }

        [Test]
        public void NobodyRidesPastTheirDestination()
        {
            var world = new CommuteWorld(seed: 3, CommuteConfig.Default());

            while (!world.IsEndOfLine)
            {
                world.AdvanceToNextStation();
                foreach (Passenger p in world.Passengers)
                {
                    Assert.Greater(p.DestinationStation, world.CurrentStation,
                        "降りる駅を過ぎても乗ったままの乗客がいる。");
                }
            }
        }

        [Test]
        public void SameSeedProducesIdenticalCommute()
        {
            var a = new CommuteWorld(seed: 12345, CommuteConfig.Default());
            var b = new CommuteWorld(seed: 12345, CommuteConfig.Default());

            for (int step = 0; step < 8; step++)
            {
                StationChange ca = a.AdvanceToNextStation();
                StationChange cb = b.AdvanceToNextStation();

                Assert.AreEqual(ca.Alighting.Count, cb.Alighting.Count, $"step {step} で降車数がズレた。");
                Assert.AreEqual(ca.Boarding.Count, cb.Boarding.Count, $"step {step} で乗車数がズレた。");
                Assert.AreEqual(a.Passengers.Count, b.Passengers.Count, $"step {step} で乗客数がズレた。");
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentRosters()
        {
            var a = new CommuteWorld(seed: 1, CommuteConfig.Default());
            var b = new CommuteWorld(seed: 2, CommuteConfig.Default());

            bool anyDifference = false;
            for (int i = 0; i < a.Passengers.Count; i++)
            {
                if (a.Passengers[i].DestinationStation != b.Passengers[i].DestinationStation)
                {
                    anyDifference = true;
                    break;
                }
            }

            Assert.IsTrue(anyDifference, "異なる SEED が同じ名簿を生成してはいけない。");
        }
    }
}

using System;
using System.Collections.Generic;

namespace TrainSurvival.Core
{
    /// <summary>
    /// SEED で動く通勤の「脳」。Unity に依存しないのでユニットテストできる。
    /// 誰が乗っていて各乗客がどの駅で降りるかを知っていて、電車を駅ごとに進める：各駅で何人かが降り
    /// （席が空き）、何人かが乗る（<see cref="CommuteConfig.PassengerCount"/> まで埋める）。物理的な車内・
    /// 移動・席の取り合いは Unity 層が持ち、このモデルには「誰が乗り降りしたか」を尋ねるだけ。同じ SEED
    /// なら必ず同じ通勤になり、これが「今日の通勤」の共有を可能にする。
    /// </summary>
    public sealed class CommuteWorld
    {
        private readonly CommuteConfig _config;
        private readonly DeterministicRng _rng;
        private readonly List<Passenger> _passengers = new List<Passenger>();
        private int _nextPassengerId;

        public int CurrentStation { get; private set; }

        public IReadOnlyList<Passenger> Passengers => _passengers;

        public bool IsEndOfLine => CurrentStation >= _config.StationCount - 1;

        public CommuteWorld(int seed, CommuteConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _rng = new DeterministicRng(seed);

            CurrentStation = 0;
            for (int i = 0; i < _config.PassengerCount; i++)
            {
                _passengers.Add(SpawnPassenger());
            }
        }

        /// <summary>
        /// 電車を次の駅へ進める：ここで降りる客を降ろし、その後に空きぶんだけ新しい客を乗せる。
        /// 両方のリストを返す。終点では何も変化しない。
        /// </summary>
        public StationChange AdvanceToNextStation()
        {
            if (IsEndOfLine)
            {
                return StationChange.Empty;
            }

            CurrentStation++;

            var alighting = new List<Passenger>();
            for (int i = _passengers.Count - 1; i >= 0; i--)
            {
                if (_passengers[i].DestinationStation == CurrentStation)
                {
                    alighting.Add(_passengers[i]);
                    _passengers.RemoveAt(i);
                }
            }

            var boarding = new List<Passenger>();
            if (CurrentStation < _config.StationCount - 1)
            {
                int capacityLeft = Math.Max(0, _config.PassengerCount - _passengers.Count);
                int want = _rng.NextInt(0, alighting.Count + 3);
                int boardCount = Math.Min(want, capacityLeft);
                for (int i = 0; i < boardCount; i++)
                {
                    Passenger p = SpawnPassenger();
                    _passengers.Add(p);
                    boarding.Add(p);
                }
            }

            return new StationChange(alighting, boarding);
        }

        private Passenger SpawnPassenger()
        {
            int destination = _rng.NextInt(CurrentStation + 1, _config.StationCount);
            return new Passenger(_nextPassengerId++, destination);
        }
    }
}

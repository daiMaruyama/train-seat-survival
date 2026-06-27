using System;
using System.Collections.Generic;

namespace TrainSurvival.Core
{
    /// <summary>駅に着いたとき名簿に起きたこと：誰が降りて、誰が乗ったか。</summary>
    public sealed class StationChange
    {
        public IReadOnlyList<Passenger> Alighting { get; }
        public IReadOnlyList<Passenger> Boarding { get; }

        public StationChange(IReadOnlyList<Passenger> alighting, IReadOnlyList<Passenger> boarding)
        {
            Alighting = alighting;
            Boarding = boarding;
        }

        public static StationChange Empty =>
            new StationChange(Array.Empty<Passenger>(), Array.Empty<Passenger>());
    }
}

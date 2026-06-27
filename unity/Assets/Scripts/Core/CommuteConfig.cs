namespace TrainSurvival.Core
{
    /// <summary>
    /// 1回の通勤ぶんの調整値。単なるデータにしておくことで、ユニットテストでき、後でルールを
    /// 触らずに Unity 側で ScriptableObject 化もできる。
    /// </summary>
    public sealed class CommuteConfig
    {
        /// <summary>路線の駅数。0..StationCount-1 で index 付け。駅0 が乗車駅。</summary>
        public int StationCount = 10;

        /// <summary>開始時に乗っている乗客の数。</summary>
        public int PassengerCount = 12;

        public static CommuteConfig Default()
        {
            return new CommuteConfig();
        }
    }
}

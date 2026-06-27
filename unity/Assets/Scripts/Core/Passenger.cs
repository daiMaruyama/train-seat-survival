namespace TrainSurvival.Core
{
    /// <summary>
    /// 電車に乗っている乗客ひとり。ゲーム全体が回る隠し情報が <see cref="DestinationStation"/>：
    /// これを読む（メガネ／観察）と、次にどの席が空くかを先読みできる。
    /// これは単なるデータで、Unity 層が乗客を車内の物理的な座席へ対応づける。
    /// </summary>
    public sealed class Passenger
    {
        public int Id { get; }

        /// <summary>この乗客が降りる駅の index。</summary>
        public int DestinationStation { get; }

        /// <summary>プレイヤーが情報を使って降車を確定させたら true。</summary>
        public bool IsRevealed { get; internal set; }

        public Passenger(int id, int destinationStation)
        {
            Id = id;
            DestinationStation = destinationStation;
            IsRevealed = false;
        }
    }
}

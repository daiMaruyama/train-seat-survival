using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1つの座席が車内のどこにあり、座ったときどちら（通路側）を向くか。これがグレーボックス／美術
    /// （車両 prefab がこれを公開できる）と、乗客配置・着席のロジックをつなぐ接点。後で美術を差し替えても
    /// ゲーム側を触らずに済む。<see cref="IsBenchEnd"/> はベンチ区画の端の席か（端は浅め・中は深めに
    /// 前へずらして座ると肩が互い違いになり自然に収まる）。
    /// </summary>
    public readonly struct SeatAnchor
    {
        public readonly int Index;
        public readonly Vector3 Position;
        public readonly Quaternion Facing;
        public readonly bool IsBenchEnd;

        public SeatAnchor(int index, Vector3 position, Quaternion facing, bool isBenchEnd)
        {
            Index = index;
            Position = position;
            Facing = facing;
            IsBenchEnd = isBenchEnd;
        }
    }
}

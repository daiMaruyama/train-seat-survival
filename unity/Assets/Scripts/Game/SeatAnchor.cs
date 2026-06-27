using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1つの座席が車内のどこにあり、座ったときどちら（通路側）を向くか。これがグレーボックス／美術
    /// （車両 prefab がこれを公開できる）と、乗客配置・着席のロジックをつなぐ接点。後で美術を差し替えても
    /// ゲーム側を触らずに済む。
    /// </summary>
    public readonly struct SeatAnchor
    {
        public readonly int Index;
        public readonly Vector3 Position;
        public readonly Quaternion Facing;

        public SeatAnchor(int index, Vector3 position, Quaternion facing)
        {
            Index = index;
            Position = position;
            Facing = facing;
        }
    }
}

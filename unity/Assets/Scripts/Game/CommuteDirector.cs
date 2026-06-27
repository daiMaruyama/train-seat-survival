using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TrainSurvival.Core;

namespace TrainSurvival.Game
{
    /// <summary>
    /// SEED で動く <see cref="CommuteWorld"/>（脳）と、物理的な車内（身体）をつなぐ司令塔。
    /// 名簿を実際の座席へ（空席が固まらないようバラして）割り当て、Space で次の駅に着くと、降りる客は
    /// 最寄りのドアへ歩いて退場し、乗る客はドアから空席へ歩いて着席する。プレイヤーの「この席は空いてる？」
    /// という問い合わせ（<see cref="PlayerSit"/> 用）にも答える。
    /// </summary>
    [RequireComponent(typeof(CarBuilder))]
    public sealed class CommuteDirector : MonoBehaviour
    {
        private const float RideY = 0.55f;

        [SerializeField] private int _seed = 12345;
        [SerializeField] private int _stationCount = 10;
        [SerializeField] private int _emptySeatsAtStart = 10;

        private CarBuilder _car;
        private CommuteWorld _world;
        private PassengerPool _pool;

        private Passenger[] _occupant;       // 座席ごとの乗客。空席（またはプレイヤー席）は null
        private PassengerActor[] _view;      // 座席ごとの乗客の体
        private readonly Dictionary<int, int> _passengerSeat = new Dictionary<int, int>();
        private int _playerSeat = -1;

        private void Awake()
        {
            _car = GetComponent<CarBuilder>();
        }

        private void Start()
        {
            int seatCount = _car.Seats.Count;
            _occupant = new Passenger[seatCount];
            _view = new PassengerActor[seatCount];
            _pool = new PassengerPool(transform);

            int passengerCount = Mathf.Clamp(seatCount - _emptySeatsAtStart, 0, seatCount);
            var config = new CommuteConfig { StationCount = _stationCount, PassengerCount = passengerCount };
            _world = new CommuteWorld(_seed, config);

            // 名簿をシャッフルした席に座らせることで、最初の空席をバラけさせる。
            List<int> order = ShuffledSeatIndices(seatCount);
            IReadOnlyList<Passenger> roster = _world.Passengers;
            for (int k = 0; k < roster.Count; k++)
            {
                int seat = order[k];
                Passenger p = roster[k];
                _occupant[seat] = p;
                _passengerSeat[p.Id] = seat;

                PassengerActor actor = _pool.Get();
                actor.Snap(SeatViewPosition(seat), _car.Seats[seat].Facing);
                _view[seat] = actor;
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
            {
                AdvanceStation();
            }
        }

        /// <summary>次の駅へ：降りる客は席を空けてドアへ歩いて退場、乗る客は空席を確保してドアから歩いて着席。</summary>
        private void AdvanceStation()
        {
            StationChange change = _world.AdvanceToNextStation();

            foreach (Passenger p in change.Alighting)
            {
                if (!_passengerSeat.TryGetValue(p.Id, out int seat))
                {
                    continue;
                }
                _passengerSeat.Remove(p.Id);
                _occupant[seat] = null;            // 立ち上がった時点で席は空く

                PassengerActor actor = _view[seat];
                _view[seat] = null;
                if (actor != null)
                {
                    actor.Travel(WalkOutPath(seat), () => _pool.Return(actor));
                }
            }

            foreach (Passenger p in change.Boarding)
            {
                int seat = FindRandomFreeSeat();
                if (seat < 0)
                {
                    break;
                }
                _occupant[seat] = p;              // 歩いている間も席は確保（取られないよう予約）
                _passengerSeat[p.Id] = seat;

                Vector3 door = NearestDoor(_car.Seats[seat].Position.z);
                PassengerActor actor = _pool.Get();
                actor.Snap(door, _car.Seats[seat].Facing);
                _view[seat] = actor;

                Quaternion facing = _car.Seats[seat].Facing;
                actor.Travel(WalkInPath(seat, door), () => actor.transform.rotation = facing);
            }
        }

        /// <summary>その席は今プレイヤーが座れる空席か。</summary>
        public bool IsSeatGrabbable(int index)
        {
            return index >= 0 && index < _occupant.Length && _occupant[index] == null && _playerSeat != index;
        }

        public SeatAnchor GetSeat(int index) => _car.Seats[index];

        public void SetPlayerSeat(int index) => _playerSeat = index;
        public void ClearPlayerSeat() => _playerSeat = -1;

        private Vector3 SeatViewPosition(int seat)
        {
            Vector3 p = _car.Seats[seat].Position;
            return new Vector3(p.x, RideY, p.z);
        }

        // 降車：席 → 通路中央 → 最寄りドア。
        private IReadOnlyList<Vector3> WalkOutPath(int seat)
        {
            float z = _car.Seats[seat].Position.z;
            return new List<Vector3>
            {
                new Vector3(0f, RideY, z),
                NearestDoor(z),
            };
        }

        // 乗車：ドア → 通路中央 → 席。
        private IReadOnlyList<Vector3> WalkInPath(int seat, Vector3 door)
        {
            float z = _car.Seats[seat].Position.z;
            return new List<Vector3>
            {
                new Vector3(0f, RideY, z),
                SeatViewPosition(seat),
            };
        }

        private Vector3 NearestDoor(float z)
        {
            IReadOnlyList<Vector3> doors = _car.Doors;
            if (doors.Count == 0)
            {
                return new Vector3(0f, RideY, z);
            }
            Vector3 best = doors[0];
            float bestDist = Mathf.Abs(doors[0].z - z);
            for (int i = 1; i < doors.Count; i++)
            {
                float d = Mathf.Abs(doors[i].z - z);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = doors[i];
                }
            }
            return new Vector3(0f, RideY, best.z);
        }

        private int FindRandomFreeSeat()
        {
            var free = new List<int>();
            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] == null && _playerSeat != i)
                {
                    free.Add(i);
                }
            }
            return free.Count == 0 ? -1 : free[Random.Range(0, free.Count)];
        }

        private static List<int> ShuffledSeatIndices(int count)
        {
            var order = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                order.Add(i);
            }
            for (int i = count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        private int CountFreeSeats()
        {
            int free = 0;
            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] == null && _playerSeat != i)
                {
                    free++;
                }
            }
            return free;
        }

        private void OnGUI()
        {
            if (_world == null)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            GUILayout.BeginArea(new Rect(20, 20, 360, 130), GUI.skin.box);
            GUILayout.Label($"駅  {_world.CurrentStation} / {_stationCount - 1}", style);
            GUILayout.Label($"乗客  {_world.Passengers.Count}    空席  <b>{CountFreeSeats()}</b>", style);
            GUILayout.Label(_world.IsEndOfLine ? "<b>終点</b>" : "Space  次の駅へ", style);
            GUILayout.EndArea();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TrainSurvival.Core;

namespace TrainSurvival.Game
{
    /// <summary>
    /// SEED で動く <see cref="CommuteWorld"/>（脳）と、物理的な車内（身体）をつなぐ司令塔。
    /// 満員電車を再現する：開始時は全席が埋まり、通路には立ち客の群れがいて、各立ち客はどれかの席の
    /// 前に陣取って空くのを待つ。着席客が降りると、その席を陣取っていた立ち客が座る（陣取りがいなければ
    /// 近くの立ち客が座りに行く）＝空席はすぐ埋まる。乗客の「誰がいつ乗り降りするか」は Core が持ち、
    /// ここは席・立ち客・着席といった物理的な振り分けだけを担う。
    /// （※プレイヤーの割り込み＝ハイブリッドのダッシュ勝負は次の段。今は土台。）
    /// </summary>
    [RequireComponent(typeof(CarBuilder))]
    public sealed class CommuteDirector : MonoBehaviour
    {
        private const float SeatedY = 0.55f;
        private const float StandY = 0.9f;
        private const float TakeSeatRadius = 3.5f;
        private static readonly Vector3 SeatedScale = new Vector3(0.42f, 0.6f, 0.42f);
        private static readonly Vector3 StandScale = new Vector3(0.42f, 1.0f, 0.42f);

        /// <summary>通路に立って、ある席の前で空くのを待っている乗客。</summary>
        private sealed class Standee
        {
            public Passenger Passenger;
            public PassengerActor Actor;
            public int CampedSeat = -1; // 陣取っている席。-1 はうろつき
        }

        [SerializeField] private int _seed = 12345;
        [SerializeField] private int _stationCount = 10;
        [SerializeField] private int _standeeCount = 28;

        private CarBuilder _car;
        private CommuteWorld _world;
        private PassengerPool _pool;

        private Passenger[] _seatOccupant;   // 着席客。空席は null
        private PassengerActor[] _seatView;
        private Standee[] _camperOfSeat;     // 各席の前に陣取っている立ち客（いなければ null）
        private readonly Dictionary<int, int> _seatOfPassenger = new Dictionary<int, int>();
        private readonly List<Standee> _standees = new List<Standee>();
        private readonly Dictionary<int, Standee> _standeeOf = new Dictionary<int, Standee>();
        private PlayerSit _player;
        private int _playerSeat = -1;
        private int _playerClaim = -1;   // プレイヤーが予約している席（-1 は無し）

        private void Awake()
        {
            _car = GetComponent<CarBuilder>();
        }

        private void Start()
        {
            int seatCount = _car.Seats.Count;
            _seatOccupant = new Passenger[seatCount];
            _seatView = new PassengerActor[seatCount];
            _camperOfSeat = new Standee[seatCount];
            _pool = new PassengerPool(transform);
            _player = FindFirstObjectByType<PlayerSit>();

            int aboard = seatCount + _standeeCount;
            var config = new CommuteConfig { StationCount = _stationCount, PassengerCount = aboard };
            _world = new CommuteWorld(_seed, config);

            IReadOnlyList<Passenger> roster = _world.Passengers;
            List<int> seatOrder = ShuffledSeatIndices(seatCount);
            List<int> campOrder = ShuffledSeatIndices(seatCount);
            int campPtr = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                Passenger p = roster[i];
                if (i < seatCount)
                {
                    SeatPassengerInstant(seatOrder[i], p);
                }
                else
                {
                    int camp = campPtr < seatCount ? campOrder[campPtr++] : -1;
                    SpawnStandee(p, camp, SeatFrontSpot(camp));
                }
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

        /// <summary>次の駅へ：降りる客を処理し（席が空けば立ち客が座る）、新しい客は立ち客として乗ってくる。</summary>
        private void AdvanceStation()
        {
            StationChange change = _world.AdvanceToNextStation();

            foreach (Passenger p in change.Alighting)
            {
                if (_seatOfPassenger.TryGetValue(p.Id, out int seat))
                {
                    LeaveSeat(seat);
                    ResolveOpening(seat);
                }
                else if (_standeeOf.TryGetValue(p.Id, out Standee s))
                {
                    RemoveStandee(s, walkOut: true);
                }
            }

            foreach (Passenger p in change.Boarding)
            {
                int camp = FindUncampedSeat();
                Vector3 door = NearestDoor(camp >= 0 ? _car.Seats[camp].Position.z : 0f);
                Standee s = SpawnStandee(p, camp, door);
                Vector3 spot = camp >= 0 ? SeatFrontSpot(camp) : new Vector3(0f, StandY, door.z);
                s.Actor.Travel(new List<Vector3> { new Vector3(0f, StandY, spot.z), spot }, null);
            }
        }

        // --- 着席まわり -------------------------------------------------

        private void SeatPassengerInstant(int seat, Passenger p)
        {
            _seatOccupant[seat] = p;
            _seatOfPassenger[p.Id] = seat;

            PassengerActor actor = _pool.Get();
            actor.transform.localScale = SeatedScale;
            actor.Snap(SeatViewPosition(seat), _car.Seats[seat].Facing);
            _seatView[seat] = actor;
        }

        private void LeaveSeat(int seat)
        {
            if (_seatOccupant[seat] != null)
            {
                _seatOfPassenger.Remove(_seatOccupant[seat].Id);
            }
            _seatOccupant[seat] = null;

            PassengerActor actor = _seatView[seat];
            _seatView[seat] = null;
            if (actor != null)
            {
                actor.transform.localScale = StandScale;
                actor.Travel(WalkOutPath(seat), () => _pool.Return(actor));
            }
        }

        /// <summary>空いた席を誰かが取る：陣取っていた立ち客、いなければ近くの立ち客。誰もいなければ空いたまま。</summary>
        private void ResolveOpening(int seat)
        {
            // 予約していたプレイヤーが最優先で滑り込む。
            if (_playerClaim == seat && _player != null)
            {
                _playerClaim = -1;
                _playerSeat = seat;
                _player.SlideInto(_car.Seats[seat]);
                return;
            }

            Standee taker = _camperOfSeat[seat] ?? NearestStandee(SeatPosition(seat), TakeSeatRadius);
            if (taker == null)
            {
                return;
            }

            DetachStandee(taker);

            _seatOccupant[seat] = taker.Passenger;
            _seatOfPassenger[taker.Passenger.Id] = seat;
            PassengerActor actor = taker.Actor;
            _seatView[seat] = actor;

            actor.Travel(new List<Vector3> { SeatViewPosition(seat) }, () =>
            {
                actor.transform.localScale = SeatedScale;
                actor.transform.SetPositionAndRotation(SeatViewPosition(seat), _car.Seats[seat].Facing);
            });
        }

        // --- 立ち客まわり -----------------------------------------------

        private Standee SpawnStandee(Passenger p, int camp, Vector3 startPos)
        {
            PassengerActor actor = _pool.Get();
            actor.transform.localScale = StandScale;
            actor.Snap(startPos, FacingTowardSeat(camp));

            var s = new Standee { Passenger = p, Actor = actor, CampedSeat = camp };
            _standees.Add(s);
            _standeeOf[p.Id] = s;
            if (camp >= 0)
            {
                _camperOfSeat[camp] = s;
            }
            return s;
        }

        private void RemoveStandee(Standee s, bool walkOut)
        {
            DetachStandee(s);
            PassengerActor actor = s.Actor;
            if (walkOut)
            {
                actor.Travel(new List<Vector3>
                {
                    new Vector3(0f, StandY, actor.transform.position.z),
                    NearestDoor(actor.transform.position.z),
                }, () => _pool.Return(actor));
            }
            else
            {
                _pool.Return(actor);
            }
        }

        /// <summary>立ち客リスト・陣取り表からだけ外す（体はそのまま）。</summary>
        private void DetachStandee(Standee s)
        {
            _standees.Remove(s);
            _standeeOf.Remove(s.Passenger.Id);
            if (s.CampedSeat >= 0 && _camperOfSeat[s.CampedSeat] == s)
            {
                _camperOfSeat[s.CampedSeat] = null;
            }
        }

        private Standee NearestStandee(Vector3 pos, float radius)
        {
            Standee best = null;
            float bestSqr = radius * radius;
            foreach (Standee s in _standees)
            {
                float sqr = (s.Actor.transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }
            return best;
        }

        private int FindUncampedSeat()
        {
            var candidates = new List<int>();
            for (int i = 0; i < _camperOfSeat.Length; i++)
            {
                if (_camperOfSeat[i] == null && _playerSeat != i && _playerClaim != i)
                {
                    candidates.Add(i);
                }
            }
            return candidates.Count == 0 ? -1 : candidates[Random.Range(0, candidates.Count)];
        }

        // --- 位置の計算 -------------------------------------------------

        private Vector3 SeatPosition(int seat) => _car.Seats[seat].Position;

        private Vector3 SeatViewPosition(int seat)
        {
            Vector3 p = _car.Seats[seat].Position;
            return new Vector3(p.x, SeatedY, p.z);
        }

        // 席の前（通路寄り）の立ち位置。
        private Vector3 SeatFrontSpot(int seat)
        {
            if (seat < 0)
            {
                return new Vector3(0f, StandY, 0f);
            }
            Vector3 p = _car.Seats[seat].Position;
            float sign = Mathf.Sign(p.x);
            return new Vector3(sign * 0.7f, StandY, p.z);
        }

        private Quaternion FacingTowardSeat(int seat)
        {
            if (seat < 0)
            {
                return Quaternion.identity;
            }
            float sign = Mathf.Sign(_car.Seats[seat].Position.x);
            return Quaternion.LookRotation(new Vector3(sign, 0f, 0f));
        }

        private IReadOnlyList<Vector3> WalkOutPath(int seat)
        {
            float z = _car.Seats[seat].Position.z;
            return new List<Vector3>
            {
                new Vector3(0f, StandY, z),
                NearestDoor(z),
            };
        }

        private Vector3 NearestDoor(float z)
        {
            IReadOnlyList<Vector3> doors = _car.Doors;
            if (doors.Count == 0)
            {
                return new Vector3(0f, StandY, z);
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
            return new Vector3(0f, StandY, best.z);
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

        // --- プレイヤー / HUD 向けの読み取り ------------------------------

        public bool IsSeatGrabbable(int index)
        {
            return index >= 0 && index < _seatOccupant.Length && _seatOccupant[index] == null && _playerSeat != index;
        }

        public SeatAnchor GetSeat(int index) => _car.Seats[index];

        public void SetPlayerSeat(int index) => _playerSeat = index;
        public void ClearPlayerSeat() => _playerSeat = -1;

        // プレイヤーの「予約（位置取り）」。埋まっていて、誰も陣取っていない席だけ予約できる。
        public bool CanClaim(int seat)
        {
            return seat >= 0 && seat < _seatOccupant.Length
                && _seatOccupant[seat] != null
                && _camperOfSeat[seat] == null
                && _playerSeat != seat;
        }

        public void SetPlayerClaim(int seat) => _playerClaim = seat;
        public void ClearPlayerClaim() => _playerClaim = -1;

        public int CurrentStation => _world?.CurrentStation ?? 0;
        public int StationCount => _stationCount;
        public int Aboard => _world?.Passengers.Count ?? 0;
        public int FreeSeats => _seatOccupant == null ? 0 : CountFreeSeats();
        public bool IsEndOfLine => _world?.IsEndOfLine ?? false;

        private int CountFreeSeats()
        {
            int free = 0;
            for (int i = 0; i < _seatOccupant.Length; i++)
            {
                if (_seatOccupant[i] == null && _playerSeat != i)
                {
                    free++;
                }
            }
            return free;
        }
    }
}

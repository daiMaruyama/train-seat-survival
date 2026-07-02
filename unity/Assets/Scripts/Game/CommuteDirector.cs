using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TrainSurvival.Core;

namespace TrainSurvival.Game
{
    /// <summary>
    /// SEED で動く <see cref="CommuteWorld"/>（脳）と、物理的な車内（身体）をつなぐ司令塔。
    /// 満員電車の椅子取りゲーム：開始時は全席が埋まり、通路の立ち客はどれかの席の前に陣取っている。
    /// 着席客が降りて席が空くと、狙っていた立ち客が「一瞬迷ってから」歩いて座りに行く——その間は
    /// 誰でも座れるので、プレイヤーが先に E で座れば勝ち（先に座った者勝ち）。空席は放っておけば
    /// すぐ埋まる。乗客の「誰がいつ乗り降りするか」は Core が持ち、ここは席の取り合いの物理だけを担う。
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
            public int CampedSeat = -1;   // 陣取っている席。-1 はうろつき
            public int IncomingSeat = -1; // いま座りに向かっている空席。-1 は待機中
        }

        [SerializeField] private int _seed = 12345;
        [SerializeField] private int _stationCount = 10;
        [SerializeField] private int _standeeCount = 28;
        [SerializeField] private float _secondsPerStation = 8f;   // サクサク進行。1駅の長さ
        [SerializeField] private float _drainRampPerLeg = 0.25f;  // 乗り換えごとの消耗倍率の伸び
        [SerializeField] private float _takerHesitation = 0.45f; // 立ち客が空席に気づいてから動くまでの迷い＝プレイヤーの勝機（ギリ反応できる長さ）
        [SerializeField] private float _sitReward = 40f;         // 座れた日のご褒美回復。消耗倍率の伸びに徐々に食われ、ランは必ず終わる

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
        private Standee[] _incoming;     // 各空席へ座りに向かっている立ち客（いなければ null）
        private int _playerSeat = -1;
        private float _stationTimer;
        private bool _transitioning;     // 日替わり演出中は駅を進めない

        /// <summary>日替わり演出の黒フェード濃度（0..1）。HudView が読んで描く。</summary>
        public float TransitionAlpha { get; private set; }

        /// <summary>日替わり演出中に出すラベル（「2日目」など）。HudView が読んで描く。</summary>
        public string TransitionLabel { get; private set; } = "";
        private int _leg;                // 何本目の電車か（乗り換え回数）

        /// <summary>ラン全体で生き延びた駅数（スコア）。</summary>
        public int TotalStationsSurvived { get; private set; }

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
            _incoming = new Standee[seatCount];
            _pool = new PassengerPool(transform);
            _player = FindFirstObjectByType<PlayerSit>();

            SetupLeg(_seed);
            _stationTimer = _secondsPerStation;
        }

        /// <summary>1本ぶんの電車（レグ）を満員状態で組む。乗り換えのたびに新しい seed で呼び直す。</summary>
        private void SetupLeg(int seed)
        {
            int seatCount = _car.Seats.Count;
            int aboard = seatCount + _standeeCount;
            var config = new CommuteConfig { StationCount = _stationCount, PassengerCount = aboard };
            _world = new CommuteWorld(seed, config);

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
            if (Time.timeScale <= 0f || _transitioning)
            {
                return; // 倒れて停止中／日替わり演出中は駅を進めない
            }

            // 駅は自動で進む（サクサク）。Space はデバッグ用の早送り。
            _stationTimer -= Time.deltaTime;
            Keyboard kb = Keyboard.current;
            bool skip = kb != null && kb.spaceKey.wasPressedThisFrame;
            if (_stationTimer <= 0f || skip)
            {
                AdvanceStation();
                _stationTimer = _secondsPerStation;
            }
        }

        /// <summary>次の駅へ：降りる客を処理し（席が空けば立ち客が座る）、新しい客は立ち客として乗ってくる。終点なら乗り換え。</summary>
        private void AdvanceStation()
        {
            if (_world.IsEndOfLine)
            {
                Transfer();
                return;
            }

            TotalStationsSurvived++;
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

        /// <summary>
        /// 乗り換え：終点で全員降ろして次の満員電車に乗り直す。プレイヤーは強制的に立たされ、予約も消える
        /// （「座れば安泰」を崩す仕様の柱）。日が進むほど立ちの消耗が重くなり、ランは必ず終わる。
        /// </summary>
        private void Transfer()
        {
            _leg++;
            ClearCar();

            _playerSeat = -1;
            if (_player != null)
            {
                _player.ResetForTransfer();
                var stamina = _player.GetComponent<StaminaSystem>();
                if (stamina != null)
                {
                    stamina.DrainMultiplier = 1f + _drainRampPerLeg * _leg;
                }
            }

            SetupLeg(_seed + _leg);
        }

        /// <summary>車内の乗客を全撤去してプールへ返す（車両ジオメトリはそのまま）。</summary>
        private void ClearCar()
        {
            // 座りに向かう途中の立ち客は _incoming を消せば各コルーチンのガードで自然に止まる
            // （StopAllCoroutines は日替わり演出のコルーチン自身まで殺すので使わない）。
            for (int i = 0; i < _seatView.Length; i++)
            {
                _incoming[i] = null;
                if (_seatView[i] != null)
                {
                    _pool.Return(_seatView[i]);
                    _seatView[i] = null;
                }
                _seatOccupant[i] = null;
                _camperOfSeat[i] = null;
            }
            foreach (Standee s in _standees)
            {
                _pool.Return(s.Actor);
            }
            _standees.Clear();
            _standeeOf.Clear();
            _seatOfPassenger.Clear();
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

        /// <summary>
        /// 席が空いた：狙っていた立ち客（いなければ近くの立ち客）が座りに向かう。ただし迷い＋歩きの間は
        /// 席は空いたままなので、プレイヤーが先に座れば横取りできる（椅子取りゲーム）。
        /// </summary>
        private void ResolveOpening(int seat)
        {
            Standee taker = _camperOfSeat[seat];
            if (taker == null || taker.IncomingSeat >= 0)
            {
                taker = NearestFreeStandee(SeatPosition(seat), TakeSeatRadius);
            }
            if (taker == null)
            {
                return;
            }

            taker.IncomingSeat = seat;
            _incoming[seat] = taker;
            StartCoroutine(TakeSeatRoutine(seat, taker));
        }

        /// <summary>立ち客が空席に気づき、迷ってから歩いて座る。各段階でプレイヤーに取られたら中断。</summary>
        private IEnumerator TakeSeatRoutine(int seat, Standee taker)
        {
            yield return new WaitForSeconds(_takerHesitation);
            if (_incoming[seat] != taker || _seatOccupant[seat] != null || _playerSeat == seat)
            {
                yield break;
            }

            bool arrived = false;
            taker.Actor.Travel(new List<Vector3> { SeatViewPosition(seat) }, () => arrived = true);
            while (!arrived)
            {
                if (_incoming[seat] != taker || _playerSeat == seat)
                {
                    yield break;
                }
                yield return null;
            }
            if (_incoming[seat] != taker || _playerSeat == seat)
            {
                yield break;
            }

            _incoming[seat] = null;
            taker.IncomingSeat = -1;
            DetachStandee(taker);

            _seatOccupant[seat] = taker.Passenger;
            _seatOfPassenger[taker.Passenger.Id] = seat;
            _seatView[seat] = taker.Actor;
            taker.Actor.transform.localScale = SeatedScale;
            taker.Actor.Snap(SeatViewPosition(seat), _car.Seats[seat].Facing);
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

        /// <summary>まだどの空席にも向かっていない立ち客のうち、一番近い者。</summary>
        private Standee NearestFreeStandee(Vector3 pos, float radius)
        {
            Standee best = null;
            float bestSqr = radius * radius;
            foreach (Standee s in _standees)
            {
                if (s.IncomingSeat >= 0)
                {
                    continue;
                }
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
                if (_camperOfSeat[i] == null && _playerSeat != i)
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

        /// <summary>プレイヤーが空席に座ろうとした。先に座れたら true（向かっていた立ち客は諦めて陣取り直す）。</summary>
        public bool TryPlayerSit(int seat)
        {
            if (!IsSeatGrabbable(seat))
            {
                return false;
            }

            Standee loser = _incoming[seat];
            if (loser != null)
            {
                _incoming[seat] = null;
                loser.IncomingSeat = -1;
                ReCamp(loser);
            }
            _playerSeat = seat;

            // 座れた＝この日は勝ち。余韻→日替わり演出→次の電車へ。
            StartCoroutine(SeatedDayRoutine());
            return true;
        }

        /// <summary>
        /// 座れた日のクリア演出（仮）。ひと呼吸おいて黒フェード→「N日目」→次の満員電車。
        /// ペルソナ風の日付転換はあとでブラッシュアップ前提のプレースホルダ。
        /// </summary>
        private IEnumerator SeatedDayRoutine()
        {
            _transitioning = true;
            yield return new WaitForSeconds(1.0f); // 座れた余韻

            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                TransitionAlpha = Mathf.Clamp01(t);
                yield return null;
            }
            TransitionAlpha = 1f;

            Transfer(); // 日を進める：全員入れ替え・強制起立・消耗倍率アップ
            var stamina = _player != null ? _player.GetComponent<StaminaSystem>() : null;
            if (stamina != null)
            {
                stamina.Restore(_sitReward);
            }
            TransitionLabel = $"{_leg + 1}日目";
            _stationTimer = _secondsPerStation;
            yield return new WaitForSeconds(0.9f);

            TransitionLabel = "";
            for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.35f)
            {
                TransitionAlpha = Mathf.Clamp01(t);
                yield return null;
            }
            TransitionAlpha = 0f;
            _transitioning = false;
        }

        /// <summary>プレイヤーが席を立った。空いた席はすぐ立ち客に狙われる。</summary>
        public void PlayerVacated(int seat)
        {
            _playerSeat = -1;
            if (seat >= 0)
            {
                ResolveOpening(seat);
            }
        }

        /// <summary>席を取り損ねた（or 取られた）立ち客が、別の席の前へ陣取り直す。</summary>
        private void ReCamp(Standee s)
        {
            if (s.CampedSeat >= 0 && _camperOfSeat[s.CampedSeat] == s)
            {
                _camperOfSeat[s.CampedSeat] = null;
            }
            int camp = FindUncampedSeat();
            s.CampedSeat = camp;
            if (camp >= 0)
            {
                _camperOfSeat[camp] = s;
                Vector3 spot = SeatFrontSpot(camp);
                s.Actor.Travel(new List<Vector3>
                {
                    new Vector3(0f, StandY, s.Actor.transform.position.z),
                    new Vector3(0f, StandY, spot.z),
                    spot,
                }, null);
            }
        }

        public int CurrentStation => _world?.CurrentStation ?? 0;
        public int StationCount => _stationCount;
        public int Aboard => _world?.Passengers.Count ?? 0;
        public int FreeSeats => _seatOccupant == null ? 0 : CountFreeSeats();
        public bool IsEndOfLine => _world?.IsEndOfLine ?? false;
        public int Leg => _leg;
        public float SecondsToNextStation => Mathf.Max(0f, _stationTimer);

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

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
        private const float SeatedY = 0f; // 座り客のルートも床基準（座り姿勢はモーション側が腰高を持つ）
        private const float StandY = 0f;  // 床
        private const float TakeSeatRadius = 3.5f;

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

        [Header("停車（駅サイクル）")]
        [SerializeField] private float _stationDwell = 3.5f; // 停車時間（乗降が起きる）
        [SerializeField] private float _brakeTime = 1.1f;    // 減速にかける時間
        [SerializeField] private float _accelTime = 1.6f;    // 加速にかける時間

        [Header("音素材（未設定なら GameAudio の合成音）")]
        [SerializeField] private AudioClip _arriveClip;
        [SerializeField] private AudioClip _bellClip;
        [SerializeField] private AudioClip _trainDepartureClip;
        [SerializeField] private AudioClip _trainStopClip;
        [SerializeField] private AudioClip _hornClip;
        [SerializeField] private AudioClip _heartClip;
        [SerializeField] private Vector2 _hornIntervalRange = new Vector2(8f, 18f);

        [Header("乗客チューニング（次の乗降・座り直しから反映）")]
        [SerializeField] private float _passengerScale = 0.72f;   // 身長スケール（素モデル約2.5m→0.72で約1.8m）
        [SerializeField] private float _passengerStandY = 0f;     // 立ちの上下微調整（＋で浮く）
        [SerializeField] private float _passengerSitY = 0f;       // 座りの上下微調整（＋で浮く）
        [SerializeField] private float _passengerSeatForward = 0.11f; // 座面中心→通路側の距離（−で深く＝背もたれ寄り）
        [SerializeField] private float _sitReward = 40f;         // 座れた日のご褒美回復。消耗倍率の伸びに徐々に食われ、ランは必ず終わる
        [SerializeField] private float _playerBoardingY = 0.2f;  // CharacterController の足元が床へ自然に乗る高さ

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
        private float _trainSpeed = 1f;  // 電車の疑似速度(0..1)
        private bool _atStation;         // 停車中（減速開始〜発車まで）
        private bool _transitioning;     // 日替わり演出中は駅を進めない
        private float _hornTimer;

        /// <summary>電車の疑似速度(0..1)。車窓スクロール（Scenery）が読む。</summary>
        public float TrainSpeed01 => _trainSpeed;

        /// <summary>停車中か（HUD 表示用）。</summary>
        public bool IsAtStation => _atStation;

        private CutInView _cutIn; // 日替わりカットイン演出（描画は向こう、タイミングはこちら）
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
            _cutIn = FindFirstObjectByType<CutInView>();
            RegisterAudioClips();

            SetupLeg(_seed);
            MovePlayerToDoorFront();
            _stationTimer = _secondsPerStation;
            ResetHornTimer();
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

            // 走行→減速→停車（ここで乗降）→加速、のサイクル。Space はデバッグ用の早送り。
            Keyboard kb = Keyboard.current;
            bool skip = kb != null && kb.spaceKey.wasPressedThisFrame;
            if (!_atStation)
            {
                _trainSpeed = Mathf.MoveTowards(_trainSpeed, 1f, Time.deltaTime / _accelTime); // 発車加速
                MaybePlayHorn();
                _stationTimer -= Time.deltaTime;
                if (_stationTimer <= 0f || skip)
                {
                    StartCoroutine(StationStopRoutine());
                }
            }

            RefreshSeatHighlights();
        }

        /// <summary>駅到着：減速して停車→停車中に乗降（席の取り合いはここで起きる）→発車。</summary>
        private IEnumerator StationStopRoutine()
        {
            _atStation = true;
            GameAudio.Instance.Play(GameAudio.Sfx.TrainStop, Random.Range(0.96f, 1.04f));
            while (_trainSpeed > 0.001f)
            {
                _trainSpeed = Mathf.MoveTowards(_trainSpeed, 0f, Time.deltaTime / _brakeTime);
                yield return null;
            }
            _trainSpeed = 0f;
            float stopTail = ClipTail(_trainStopClip, _brakeTime, 0.25f, 1.2f);
            if (stopTail > 0f)
            {
                yield return new WaitForSeconds(stopTail);
            }
            GameAudio.Instance.Play(GameAudio.Sfx.Arrive); // 到着音＋環境音
            _car.SetDoorsOpen(true);

            AdvanceStation(); // 降りる→席が空く→乗ってくる（すべて停車中）
            yield return new WaitForSeconds(_stationDwell);

            GameAudio.Instance.Play(GameAudio.Sfx.Bell); // 発車ベル＋ドア
            float bellWait = ClipWait(_bellClip, 0.9f, 3.2f);
            float closeDelay = Mathf.Min(0.7f, bellWait * 0.35f);
            yield return new WaitForSeconds(closeDelay);
            _car.SetDoorsOpen(false);
            yield return new WaitForSeconds(Mathf.Max(0f, bellWait - closeDelay));
            GameAudio.Instance.Play(GameAudio.Sfx.TrainDeparture, Random.Range(0.98f, 1.03f)); // 電車発車
            _stationTimer = _secondsPerStation;
            _atStation = false; // Update 側で加速していく
            ResetHornTimer();
        }

        private void RegisterAudioClips()
        {
            GameAudio audio = GameAudio.Instance;
            audio.RegisterClip(GameAudio.Sfx.Arrive, _arriveClip);
            audio.RegisterClip(GameAudio.Sfx.Bell, _bellClip);
            audio.RegisterClip(GameAudio.Sfx.TrainDeparture, _trainDepartureClip);
            audio.RegisterClip(GameAudio.Sfx.TrainStop, _trainStopClip);
            audio.RegisterClip(GameAudio.Sfx.Horn, _hornClip);
            audio.RegisterClip(GameAudio.Sfx.Heart, _heartClip);
        }

        private void MaybePlayHorn()
        {
            if (_trainSpeed < 0.35f)
            {
                return;
            }

            _hornTimer -= Time.deltaTime;
            if (_hornTimer > 0f)
            {
                return;
            }

            GameAudio.Instance.Play(GameAudio.Sfx.Horn, Random.Range(0.92f, 1.08f));
            ResetHornTimer();
        }

        private void ResetHornTimer()
        {
            float min = Mathf.Max(1f, Mathf.Min(_hornIntervalRange.x, _hornIntervalRange.y));
            float max = Mathf.Max(min, Mathf.Max(_hornIntervalRange.x, _hornIntervalRange.y));
            _hornTimer = Random.Range(min, max);
        }

        /// <summary>空席（今すぐ座れる席）を常時light-upさせる。狙い色は PlayerSit がマーカーに直接付ける。</summary>
        private void RefreshSeatHighlights()
        {
            IReadOnlyList<SeatMarker> markers = _car.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                markers[i].SetAvailable(IsSeatGrabbable(i));
            }
        }

        /// <summary>次の駅へ：降りる客を処理し（席が空けば立ち客が座る）、新しい客は立ち客として乗ってくる。終点なら乗り換え。</summary>
        private void AdvanceStation()
        {
            if (!EnsureWorld())
            {
                return;
            }

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

        private bool EnsureWorld()
        {
            if (_world != null)
            {
                return true;
            }

            if (_car == null || _car.Seats.Count == 0 || _seatOccupant == null || _pool == null)
            {
                return false;
            }

            ClearCar();
            SetupLeg(_seed + _leg);
            return _world != null;
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
                MovePlayerToDoorFront();
            }

            SetupLeg(_seed + _leg);
        }

        /// <summary>開始時・乗り換え時のプレイヤー初期位置。前日座った席に残さず、ドア前の通路へ戻す。</summary>
        private void MovePlayerToDoorFront()
        {
            if (_player == null)
            {
                return;
            }

            IReadOnlyList<Vector3> doors = _car.Doors;
            float z = doors.Count > 0 ? doors[Mathf.Abs(_leg) % doors.Count].z : 0f;
            Vector3 position = new Vector3(0f, _playerBoardingY, z);
            Quaternion rotation = Quaternion.identity;

            var controller = _player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }
            _player.transform.SetPositionAndRotation(position, rotation);
            var fpc = _player.GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.ResetLook();
            }
            if (controller != null)
            {
                controller.enabled = wasEnabled;
            }
        }

        private static float ClipWait(AudioClip clip, float fallback, float max)
        {
            return clip != null ? Mathf.Min(max, Mathf.Max(fallback, clip.length)) : fallback;
        }

        private static float ClipTail(AudioClip clip, float alreadyPlayed, float overlap, float max)
        {
            if (clip == null)
            {
                return 0f;
            }
            return Mathf.Min(max, Mathf.Max(0f, clip.length - alreadyPlayed - overlap));
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

            PassengerActor actor = GetConfiguredActor();
            actor.SetSeated(true);
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
                actor.SetSeated(false);
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
            taker.Actor.SeatForward = GetSeatForward(seat);
            taker.Actor.Snap(SeatViewPosition(seat), _car.Seats[seat].Facing);
            taker.Actor.SetSeated(true, instant: false); // 歩いて来た流れから腰を下ろすモーションへ
        }

        // --- 立ち客まわり -----------------------------------------------

        /// <summary>プールから取り出し、Inspector のチューニング値を注入してから着せ替える。</summary>
        private PassengerActor GetConfiguredActor()
        {
            PassengerActor actor = _pool.Get();
            actor.BaseScale = _passengerScale;
            actor.StandYOffset = _passengerStandY;
            actor.SitYOffset = _passengerSitY;
            actor.SeatForward = _passengerSeatForward;
            actor.RandomizeLook();
            return actor;
        }

        private Standee SpawnStandee(Passenger p, int camp, Vector3 startPos)
        {
            PassengerActor actor = GetConfiguredActor();
            actor.SetSeated(false);
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
            return new Vector3(sign * 0.55f, StandY, p.z); // 吊り革ラインに立つ（座り客の膝ともぶつからない）
        }

        /// <summary>座るときの前方オフセット（座面中心→通路側）。全員一律＝横一列。小さいほど深く座る。</summary>
        public float GetSeatForward(int seat)
        {
            return _passengerSeatForward;
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
            GameAudio.Instance.Play(GameAudio.Sfx.Sit); // 座れた！
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

            // ペルソナ風カットイン（CutInView）で日替わり。画面が覆われた瞬間に世界を入れ替える
            string label = $"{_leg + 2}日目";
            bool covered = _cutIn == null; // 演出が無ければ即時
            bool done = _cutIn == null;
            if (_cutIn != null)
            {
                _cutIn.PlayDayTransition(label, () => covered = true, () => done = true);
            }
            while (!covered)
            {
                yield return null;
            }

            Transfer(); // 日を進める：全員入れ替え・強制起立・消耗倍率アップ
            var stamina = _player != null ? _player.GetComponent<StaminaSystem>() : null;
            if (stamina != null)
            {
                stamina.Restore(_sitReward);
            }
            _stationTimer = _secondsPerStation;

            while (!done)
            {
                yield return null;
            }
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

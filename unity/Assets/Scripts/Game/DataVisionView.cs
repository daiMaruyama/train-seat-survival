using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// データメガネの視界。拾って0.2秒後から、発光の間に1秒空けて0.5秒のスキャンを2回行う。
    /// 発光中は、次の駅で降りる乗客の席だけが元色を保ったまま光る。
    /// 発光は <see cref="SeatMarker"/> のインテル層に委譲し、ここはスキャン列の時間と駆動だけを持つ。
    /// 隠し情報 <see cref="Core.Passenger.DestinationStation"/> は
    /// <see cref="CommuteDirector.TryReadSeatIntel"/> 経由でのみ覗く（描画専用・ルールは持たない）。
    /// </summary>
    public sealed class DataVisionView : MonoBehaviour
    {
        public static DataVisionView Instance { get; private set; }

        [SerializeField, Min(0f)] private float _initialDelay = 0.2f;
        [SerializeField, Min(0.1f)] private float _pulseDuration = 0.5f;
        [SerializeField, Min(0f)] private float _pulseInterval = 1f;
        [SerializeField, Min(1)] private int _pulseCount = 2;

        private CommuteDirector _director;
        private CarBuilder _car;
        private float _sequenceStartedAt;
        private float _sequenceEndsAt;
        private bool _wasPulsing; // 発光が切れた瞬間に席を通常表示へ戻すため

        /// <summary>初回待機・発光間隔を含め、2連スキャンが進行中か（HUD表示用）。</summary>
        public bool IsActive => Time.unscaledTime < _sequenceEndsAt;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>メガネを拾った瞬間から2連スキャンを開始する。</summary>
        public void Scan()
        {
            float delay = Mathf.Max(0f, _initialDelay);
            float duration = Mathf.Max(0.1f, _pulseDuration);
            float interval = Mathf.Max(0f, _pulseInterval);
            int count = Mathf.Max(1, _pulseCount);

            ClearIntel();
            _wasPulsing = false;
            _sequenceStartedAt = Time.unscaledTime;
            _sequenceEndsAt = _sequenceStartedAt + delay + duration * count + interval * (count - 1);
        }

        /// <summary>日付変更時は途中の発光を破棄する。</summary>
        public void ResetForNewDay()
        {
            _sequenceEndsAt = 0f;
            ClearIntel();
            _wasPulsing = false;
        }

        private void LateUpdate()
        {
            if (_director == null)
            {
                _director = FindFirstObjectByType<CommuteDirector>();
            }
            if (_car == null)
            {
                _car = FindFirstObjectByType<CarBuilder>();
            }
            if (_director == null || _car == null || _director.SeatCount == 0)
            {
                return;
            }

            if (!IsActive)
            {
                if (_wasPulsing)
                {
                    ClearIntel();
                }
                _wasPulsing = false;
                return;
            }

            if (!TryPulseEnvelope(Time.unscaledTime - _sequenceStartedAt, out float envelope))
            {
                if (_wasPulsing)
                {
                    ClearIntel(); // 各パルス間の1秒は通常の座席表示へ戻す
                }
                _wasPulsing = false;
                return;
            }
            _wasPulsing = true;

            var markers = _car.Markers;
            for (int seat = 0; seat < markers.Count; seat++)
            {
                if (markers[seat] == null)
                {
                    continue;
                }
                if (_director.TryReadSeatIntel(seat, out _, out int stations) && stations <= 1)
                {
                    markers[seat].SetIntel(true, envelope);
                }
                else
                {
                    markers[seat].SetIntel(false, 0f); // 次駅で降りない席・空席・プレイヤー席は通常表示
                }
            }
        }

        private bool TryPulseEnvelope(float elapsed, out float envelope)
        {
            envelope = 0f;
            float duration = Mathf.Max(0.1f, _pulseDuration);
            float cycle = duration + Mathf.Max(0f, _pulseInterval);
            float pulseTime = elapsed - Mathf.Max(0f, _initialDelay);
            if (pulseTime < 0f)
            {
                return false;
            }

            int pulseIndex = Mathf.FloorToInt(pulseTime / cycle);
            if (pulseIndex < 0 || pulseIndex >= Mathf.Max(1, _pulseCount))
            {
                return false;
            }

            float withinPulse = pulseTime - pulseIndex * cycle;
            if (withinPulse < 0f || withinPulse >= duration)
            {
                return false;
            }

            float progress = withinPulse / duration;
            // 0.5秒の大半で読み取れるよう、素早く点いて最後だけ柔らかく消す。
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.1f, progress));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, progress));
            envelope = fadeIn * fadeOut;
            return true;
        }

        private void ClearIntel()
        {
            if (_car == null)
            {
                return;
            }
            var markers = _car.Markers;
            for (int seat = 0; seat < markers.Count; seat++)
            {
                if (markers[seat] != null)
                {
                    markers[seat].SetIntel(false, 0f);
                }
            }
        }

    }
}

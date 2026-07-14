using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// データメガネの視界。メガネを拾うと「かけ直し3回分」のチャージを持ち、右クリックで一瞬だけ装着。
    /// 効いている間は各席"そのもの"が降りそう度の色で光る（緑＝もうすぐ空く 〜 赤＝当分空かない）。
    /// 色付けは <see cref="SeatMarker"/> のインテル層に委譲し、ここは入力・チャージ・駆動だけを持つ。
    /// 隠し情報 <see cref="Core.Passenger.DestinationStation"/> は
    /// <see cref="CommuteDirector.TryReadSeatIntel"/> 経由でのみ覗く（描画専用・ルールは持たない）。
    /// </summary>
    public sealed class DataVisionView : MonoBehaviour
    {
        public static DataVisionView Instance { get; private set; }

        [SerializeField] private float _scanDuration = 6f; // 1回の装着時間（一瞬だけ視える）

        private CommuteDirector _director;
        private CarBuilder _car;
        private float _activeUntil;
        private bool _wasActive; // 切れた瞬間に席の色を戻すため

        /// <summary>いまデータ視界が有効か。</summary>
        public bool IsActive => Time.unscaledTime < _activeUntil;

        /// <summary>残り有効秒数（HUD 表示用）。</summary>
        public float Remaining => Mathf.Max(0f, _activeUntil - Time.unscaledTime);

        /// <summary>残りのかけ直し回数（メガネを拾うと3回に補充）。</summary>
        public int Charges { get; private set; }

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

        /// <summary>メガネを拾った：かけ直し回数を補充する（貯め込みはできない＝上書き）。</summary>
        public void GrantCharges(int count)
        {
            Charges = Mathf.Max(Charges, count);
        }

        /// <summary>効果を即オフにする（翌日への切り替えで持ち越さないため。チャージは残る）。</summary>
        public void Deactivate()
        {
            _activeUntil = 0f;
        }

        private void Update()
        {
            // 右クリック＝メガネをかける（1回消費）。ポーズ・各種パネル・停止中は受け付けない
            if (Charges <= 0 || IsActive || Time.timeScale <= 0f
                || PauseMenuView.IsOpen || TutorialView.IsOpen || RankingView.IsOpen)
            {
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
            {
                return;
            }
            Charges--;
            _activeUntil = Time.unscaledTime + Mathf.Max(0f, _scanDuration);
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee, 1.45f); // かける音（軽いキュッ）
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
                if (_wasActive)
                {
                    ClearIntel(); // 切れた瞬間に一度だけ席の色を戻す
                }
                _wasActive = false;
                return;
            }
            _wasActive = true;

            var markers = _car.Markers;
            for (int seat = 0; seat < markers.Count; seat++)
            {
                if (markers[seat] == null)
                {
                    continue;
                }
                if (_director.TryReadSeatIntel(seat, out _, out int stations))
                {
                    markers[seat].SetIntel(true, UrgencyColor(stations));
                }
                else
                {
                    markers[seat].SetIntel(false, Color.clear); // 空席・プレイヤー席は通常表示のまま
                }
            }
        }

        private void ClearIntel()
        {
            var markers = _car.Markers;
            for (int seat = 0; seat < markers.Count; seat++)
            {
                if (markers[seat] != null)
                {
                    markers[seat].SetIntel(false, Color.clear);
                }
            }
        }

        private static Color UrgencyColor(int delta)
        {
            if (delta <= 0) return new Color(0.35f, 1f, 0.5f);    // 今にも空く（緑）
            if (delta == 1) return new Color(0.55f, 1f, 0.45f);   // 次で降りる
            if (delta == 2) return new Color(0.95f, 0.9f, 0.35f); // 黄
            if (delta == 3) return new Color(1f, 0.62f, 0.25f);   // 橙
            return new Color(1f, 0.4f, 0.34f);                    // 当分乗る（赤）
        }
    }
}

using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内に浮かぶコーヒー。くるくる回転＋上下ボブ＋暖色の光で「取れるもの」だと一目で分かるようにし、
    /// プレイヤーが触れた瞬間に自動で飲んで体力を戻す（E 操作は不要）。当たり判定はトリガー。
    /// 消費してもオブジェクトは破棄せず非表示にするだけ＝<see cref="ItemSpawner"/> がプールとして使い回す。
    /// 見た目（光の柱／ハロー）は <see cref="ItemBeacon"/> に委譲して共有する。
    /// </summary>
    public sealed class CoffeeCupItem : MonoBehaviour
    {
        [SerializeField] private float _staminaRestore = 28f; // 1日1杯なので、取りに行く価値が出る回復量
        [SerializeField] private float _spinSpeed = 100f;   // 回転（度/秒）
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSpeed = 2.2f;
        [SerializeField] private Color _glowColor = new(1f, 0.72f, 0.4f);
        [SerializeField, Range(0f, 5f)] private float _glowIntensity = 1.2f;

        private Vector3 _basePosition;
        private float _phase;
        private bool _consumed;
        private bool _started;

        public bool IsConsumed => _consumed;
        public float StaminaRestore => _staminaRestore;

        private void Start()
        {
            _started = true;
            ItemBeacon.ApplyEmission(gameObject, _glowColor, _glowIntensity);
            gameObject.AddComponent<ItemBeacon>().Configure(_glowColor);
            CaptureBase();
        }

        private void OnEnable()
        {
            // プールから再表示されたら消費フラグと基準位置を取り直す（Start より後の再利用に対応）
            _consumed = false;
            if (_started)
            {
                CaptureBase();
            }
        }

        private void CaptureBase()
        {
            _basePosition = transform.localPosition;
            _phase = 0f; // 全アイテムで上下位置を揃える
        }

        private void Update()
        {
            transform.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
            Vector3 p = _basePosition;
            p.y += Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobAmplitude;
            transform.localPosition = p;
        }

        // プレイヤー（CharacterController）が触れたら自動で飲む
        private void OnTriggerEnter(Collider other)
        {
            var stamina = other.GetComponentInParent<StaminaSystem>();
            if (stamina != null)
            {
                TryDrink(stamina);
            }
        }

        public bool TryDrink(StaminaSystem stamina)
        {
            if (_consumed || stamina == null)
            {
                return false;
            }

            _consumed = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee);
            stamina.Restore(_staminaRestore);
            gameObject.SetActive(false); // 破棄せずプールへ返す
            return true;
        }
    }
}

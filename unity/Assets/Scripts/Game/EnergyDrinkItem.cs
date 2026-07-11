using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// エナジードリンク（金の缶）。コーヒーの上位互換で大きく回復するが、1ランに1本しか出ない。
    /// 挙動は <see cref="CoffeeCupItem"/> と同型：回転＋ボブ＋金色の光、プレイヤー接触で自動使用、
    /// 消費後は非表示（<see cref="ItemSpawner"/> のプールが日替わりで撒き直す）。
    /// </summary>
    public sealed class EnergyDrinkItem : MonoBehaviour
    {
        [SerializeField] private float _staminaRestore = 60f;
        [SerializeField] private float _spinSpeed = 130f;
        [SerializeField] private float _bobAmplitude = 0.055f;
        [SerializeField] private float _bobSpeed = 2.6f;
        [SerializeField] private Color _glowColor = new(1f, 0.84f, 0.25f); // 金
        [SerializeField, Range(0f, 5f)] private float _glowIntensity = 1.5f;

        private Vector3 _basePosition;
        private float _phase;
        private bool _consumed;
        private bool _started;

        private void Start()
        {
            _started = true;
            ItemBeacon.ApplyEmission(gameObject, _glowColor, _glowIntensity);
            gameObject.AddComponent<ItemBeacon>().Configure(_glowColor);
            CaptureBase();
        }

        private void OnEnable()
        {
            _consumed = false;
            if (_started)
            {
                CaptureBase();
            }
        }

        private void CaptureBase()
        {
            _basePosition = transform.localPosition;
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            transform.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
            Vector3 p = _basePosition;
            p.y += Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobAmplitude;
            transform.localPosition = p;
        }

        private void OnTriggerEnter(Collider other)
        {
            var stamina = other.GetComponentInParent<StaminaSystem>();
            if (stamina == null || _consumed)
            {
                return;
            }
            _consumed = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee, 0.85f); // 低めのピッチ＝濃い一本
            stamina.Restore(_staminaRestore);
            gameObject.SetActive(false);
        }
    }
}

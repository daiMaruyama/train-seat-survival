using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 栄養ドリンク「モーレツ」（赤い小瓶）。飲むと"その日だけ"立ち消耗が半分になるドーピング。
    /// 効果は <see cref="StaminaSystem.TemporaryDrainScale"/> に乗り、乗り換え（翌日）でリセットされる。
    /// 挙動はコーヒーと同型（回転＋ボブ＋赤い光、接触で自動使用、消費後はプールへ）。
    /// </summary>
    public sealed class MoretsuDrinkItem : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] private float _drainScale = 0.5f; // その日の消耗倍率
        [SerializeField] private float _spinSpeed = 110f;
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSpeed = 2.4f;
        [SerializeField] private Color _glowColor = new(1f, 0.32f, 0.24f); // 赤
        [SerializeField, Range(0f, 5f)] private float _glowIntensity = 1.4f;

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
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee, 1.2f); // 高めのピッチ＝キュッと一杯
            stamina.TemporaryDrainScale = Mathf.Min(stamina.TemporaryDrainScale, _drainScale);
            gameObject.SetActive(false);
        }
    }
}

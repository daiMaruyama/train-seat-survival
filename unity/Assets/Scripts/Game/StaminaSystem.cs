using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 疲労（体力）のルールだけを持つ。立っていると減り、座っていると回復する。表示は一切持たず、
    /// 現在値を読み取り専用で公開するだけ（<see cref="HudView"/> が読んでゲージにする）。＝単一責任。
    /// 着席しているかは <see cref="PlayerSit"/> から読む。
    /// </summary>
    [RequireComponent(typeof(PlayerSit))]
    public sealed class StaminaSystem : MonoBehaviour
    {
        [SerializeField] private float _max = 100f;
        [SerializeField] private float _drainPerSecond = 4f;
        [SerializeField] private float _recoverPerSecond = 8f;

        private PlayerSit _sit;
        private float _current;

        public float Max => _max;
        public float Current => _current;
        public float Normalized => _max <= 0f ? 0f : Mathf.Clamp01(_current / _max);
        public bool IsEmpty => _current <= 0f;

        private void Awake()
        {
            _sit = GetComponent<PlayerSit>();
            _current = _max;
        }

        private void Update()
        {
            float perSecond = _sit != null && _sit.IsSeated ? _recoverPerSecond : -_drainPerSecond;
            _current = Mathf.Clamp(_current + perSecond * Time.deltaTime, 0f, _max);
        }
    }
}

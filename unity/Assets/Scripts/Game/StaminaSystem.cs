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
        [SerializeField] private float _drainPerSecond = 3f;
        [SerializeField] private float _recoverPerSecond = 1f;

        private PlayerSit _sit;
        private float _current;

        public float Max => _max;
        public float Current => _current;
        public float Normalized => _max <= 0f ? 0f : Mathf.Clamp01(_current / _max);
        public bool IsEmpty => _current <= 0f;

        /// <summary>難度用の消耗倍率。乗り換えのたびに上がる（＝日が進むほどキツく、ランは必ず終わる）。</summary>
        public float DrainMultiplier { get; set; } = 1f;

        /// <summary>回復イベント（座れた日のご褒美など）。上限で頭打ち。</summary>
        public void Restore(float amount)
        {
            _current = Mathf.Clamp(_current + amount, 0f, _max);
        }

        private void Awake()
        {
            _sit = GetComponent<PlayerSit>();
            _current = _max;
        }

        private void Update()
        {
            // 座っても回復は微々たるもの。立ちの消耗が主で、ランは長くは続かない。
            float perSecond = _sit != null && _sit.IsSeated ? _recoverPerSecond : -_drainPerSecond * DrainMultiplier;
            _current = Mathf.Clamp(_current + perSecond * Time.deltaTime, 0f, _max);
        }
    }
}

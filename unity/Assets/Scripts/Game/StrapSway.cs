using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 吊り革の常時ゆらゆら。unscaled 時間駆動なのでポーズ（timeScale=0）中も停車中も揺れ続け、
    /// 「画面のどこかが必ず生きている」を保証する。ゲーム状態には一切書き込まない純粋な見た目レイヤー。
    /// 支点（レール取り付け点）を回すだけの振り子で、揺れ幅・周期は個体ごとに少しずらして機械感を消す。
    /// </summary>
    public sealed class StrapSway : MonoBehaviour
    {
        private float _amplitude;  // 度
        private float _frequency;  // rad/s
        private float _phase;

        private void Start()
        {
            _amplitude = Random.Range(2.6f, 4.2f);
            _frequency = Random.Range(0.9f, 1.3f);
            _phase = Random.value * 10f;
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            // 主揺れ＝進行方向（X軸回り）、ごく薄い横揺れ（Z軸回り）を混ぜて単振り子臭さを消す
            float swing = Mathf.Sin(t * _frequency + _phase) * _amplitude;
            float side = Mathf.Sin(t * _frequency * 0.63f + _phase * 1.7f) * _amplitude * 0.35f;
            transform.localRotation = Quaternion.Euler(swing, 0f, side);
        }
    }
}

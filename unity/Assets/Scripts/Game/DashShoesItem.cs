using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// ダッシュ靴（赤いスニーカー）。履くと"その日だけ"移動速度が上がる＝空いた席へ猛ダッシュできる
    /// （椅子取りの主導権を足で稼ぐ）。効果は <see cref="FirstPersonController.SpeedScale"/> に乗り、
    /// 乗り換え（翌日）でリセット。1ランに1足しか出ない希少アイテム。
    /// 挙動はコーヒーと同型（回転＋ボブ＋赤い光、接触で自動使用）。モデルは外部アセットの
    /// 赤いスニーカー＋公式URPマテリアルをそのまま使い、素材の陰影や配色を保持する。
    /// </summary>
    public sealed class DashShoesItem : MonoBehaviour
    {
        [SerializeField, Range(1f, 2.5f)] private float _speedScale = 1.5f; // その日の移動速度倍率
        [SerializeField] private float _spinSpeed = 110f;
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSpeed = 2.2f;
        [SerializeField] private Color _glowColor = new(1f, 0.32f, 0.24f);  // ビーコンの赤

        private Vector3 _basePosition;
        private float _phase;
        private bool _consumed;
        private bool _started;

        private void Start()
        {
            _started = true;
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
            _phase = 0f; // 全アイテムで上下位置を揃える
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
            var fpc = other.GetComponentInParent<FirstPersonController>();
            if (fpc == null || _consumed)
            {
                return;
            }
            _consumed = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee, 1.2f); // 履き替えのキュッ
            fpc.SpeedScale = Mathf.Max(fpc.SpeedScale, _speedScale); // その日だけ俊足
            gameObject.SetActive(false);
        }
    }
}

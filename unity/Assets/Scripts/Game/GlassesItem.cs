using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内に浮かぶデータメガネ。コーヒーと同じくくるくる回転＋ボブ＋光で拾える所在を示し、触れると
    /// 「かけ直し3回分」を入手（<see cref="DataVisionView.GrantCharges"/>）。右クリックで一瞬だけ装着し、
    /// その間は席そのものが降りそう度の色で光る。光はコーヒー（暖色）と区別するためシアン。消費は非表示（プール）。
    /// </summary>
    public sealed class GlassesItem : MonoBehaviour
    {
        [SerializeField, Min(1)] private int _charges = 3; // 拾ったとき補充されるかけ直し回数
        [SerializeField] private float _spinSpeed = 80f;
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSpeed = 2.2f;
        [SerializeField] private Color _glowColor = new(0.4f, 0.82f, 1f); // シアン＝情報アイテム

        private Vector3 _basePosition;
        private float _phase;
        private bool _consumed;
        private bool _started;

        public bool IsConsumed => _consumed;

        private void Start()
        {
            _started = true;
            // メガネ本体は素材そのままの見た目を保ち、所在はシアンのビーコンだけで示す（色被り回避）
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
            // プレイヤー（体力を持つ側＝プレイヤー）が触れたら装着
            if (other.GetComponentInParent<StaminaSystem>() != null)
            {
                Equip();
            }
        }

        public void Equip()
        {
            if (_consumed)
            {
                return;
            }

            _consumed = true;
            GameAudio.Instance.Play(GameAudio.Sfx.Coffee);

            DataVisionView view = DataVisionView.Instance;
            if (view == null)
            {
                view = new GameObject("DataVisionView").AddComponent<DataVisionView>();
            }
            view.GrantCharges(_charges); // かけ直し回数を補充（使うのは右クリック）

            gameObject.SetActive(false); // 破棄せずプールへ返す
        }
    }
}

using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// カメラに“手触り”を足す軽い減衰バネ。座った瞬間などにコクッと沈んで戻る回転キックを重ねる。
    /// <see cref="FirstPersonController"/> が毎フレーム視線（localRotation）を上書きするので、その後の
    /// LateUpdate でオフセットを掛け直すだけ＝накопление（累積）せず自己補正する。FPC が無効な間
    /// （倒れ込み演出中など）は触らないので、他のカメラ制御と喧嘩しない。
    /// </summary>
    public sealed class CameraJuice : MonoBehaviour
    {
        private const float Stiffness = 170f;
        private const float Damping = 15f;

        /// <summary>今アクティブなカメラのジュース（各システムから手軽に叩く用）。</summary>
        public static CameraJuice Active { get; private set; }

        private Vector3 _rot;
        private Vector3 _rotVel;
        private FirstPersonController _fpc;

        private void OnEnable()
        {
            Active = this;
            _fpc = GetComponentInParent<FirstPersonController>();
        }

        private void OnDisable()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>回転インパルス（度・pitch/yaw/roll）。バネで元へ戻る。</summary>
        public void Kick(Vector3 euler)
        {
            _rot += euler;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // 減衰バネで 0 へ戻す（わずかにオーバーシュートして弾む）
            _rotVel += (-Stiffness * _rot - Damping * _rotVel) * dt;
            _rot += _rotVel * dt;

            if (_fpc == null || !_fpc.enabled || _rot.sqrMagnitude < 0.0001f)
            {
                return;
            }
            transform.localRotation = transform.localRotation * Quaternion.Euler(_rot);
        }
    }
}

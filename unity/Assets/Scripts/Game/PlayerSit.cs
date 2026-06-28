using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 空席に座る処理。画面中央から視線レイを飛ばし、当たった席が空いていれば座面を点灯させる
    /// （＝見ている席だけが対象。後ろを向いていると座れない）。E で着席：その席へスナップし、通路を
    /// 向き、カメラを下げ、歩行を止める。もう一度 E で起立。（回復や席の取り合いは次の段で。）
    /// </summary>
    public sealed class PlayerSit : MonoBehaviour
    {
        [SerializeField] private float _reach = 2.6f;
        [SerializeField] private float _seatedCameraDrop = 0.5f;

        private CommuteDirector _director;
        private FirstPersonController _controller;
        private Transform _camera;
        private float _standEyeHeight;
        private bool _capturedEyeHeight;

        private bool _seated;
        private SeatMarker _highlighted;

        /// <summary>着席中か（StaminaSystem などが読む）。</summary>
        public bool IsSeated => _seated;

        /// <summary>今フレーム、視線の先に座れる空席があるか（HUD のプロンプト用）。</summary>
        public bool CanSitNow { get; private set; }

        private void Start()
        {
            _director = FindFirstObjectByType<CommuteDirector>();
            _controller = GetComponent<FirstPersonController>();
        }

        private void Update()
        {
            if (_director == null)
            {
                return;
            }

            // 着席中は狙い直しをせず、E で立つだけ。
            if (_seated)
            {
                CanSitNow = false;
                if (PressedSit())
                {
                    Stand();
                }
                return;
            }

            int target = AimedSeat();
            CanSitNow = target >= 0;
            if (PressedSit() && target >= 0)
            {
                SitDown(target);
            }
        }

        /// <summary>視線レイで狙っている空席の index を返す（無ければ -1）。ついでに座面を点灯。</summary>
        private int AimedSeat()
        {
            EnsureCamera();

            int index = -1;
            SeatMarker marker = null;
            if (_camera != null &&
                Physics.Raycast(_camera.position, _camera.forward, out RaycastHit hit, _reach) &&
                hit.collider.TryGetComponent(out marker) &&
                _director.IsSeatGrabbable(marker.Index))
            {
                index = marker.Index;
            }

            Highlight(index >= 0 ? marker : null);
            return index;
        }

        private void Highlight(SeatMarker marker)
        {
            if (_highlighted == marker)
            {
                return;
            }
            if (_highlighted != null)
            {
                _highlighted.SetHighlighted(false);
            }
            _highlighted = marker;
            if (_highlighted != null)
            {
                _highlighted.SetHighlighted(true);
            }
        }

        private void SitDown(int seatIndex)
        {
            EnsureCamera();
            SeatAnchor seat = _director.GetSeat(seatIndex);
            transform.SetPositionAndRotation(seat.Position, seat.Facing);

            if (_controller != null)
            {
                _controller.CanMove = false;
            }
            SetCameraHeight(_standEyeHeight - _seatedCameraDrop);

            _director.SetPlayerSeat(seatIndex);
            _seated = true;
            Highlight(null);
        }

        private void Stand()
        {
            if (_controller != null)
            {
                _controller.CanMove = true;
            }
            SetCameraHeight(_standEyeHeight);

            _director.ClearPlayerSeat();
            _seated = false;
        }

        private static bool PressedSit()
        {
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        }

        private void EnsureCamera()
        {
            if (_camera == null && Camera.main != null)
            {
                _camera = Camera.main.transform;
            }
            if (_camera != null && !_capturedEyeHeight)
            {
                _standEyeHeight = _camera.localPosition.y;
                _capturedEyeHeight = true;
            }
        }

        private void SetCameraHeight(float y)
        {
            if (_camera == null)
            {
                return;
            }
            Vector3 local = _camera.localPosition;
            local.y = y;
            _camera.localPosition = local;
        }
    }
}

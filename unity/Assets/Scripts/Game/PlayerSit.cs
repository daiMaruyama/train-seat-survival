using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 満員電車での「位置取り（陣取り）」と滑り込み。視線の先にある"埋まっている席"を E で予約し
    /// （誰も陣取っていない席だけ）、その席が空いた瞬間に <see cref="CommuteDirector"/> から呼ばれて
    /// 自動で滑り込む。歩いて拾える空席は無いので、座る唯一の手段は「降りそうな人を読んで先に予約する」こと。
    /// （"降りそう度"を読むメガネ情報は後の段。今は予約と滑り込みの土台。）
    /// </summary>
    public sealed class PlayerSit : MonoBehaviour
    {
        [SerializeField] private float _reach = 2.8f;
        [SerializeField] private float _seatedCameraDrop = 0.5f;

        private CommuteDirector _director;
        private FirstPersonController _controller;
        private Transform _camera;
        private float _standEyeHeight;
        private bool _capturedEyeHeight;

        private bool _seated;
        private int _claimedSeat = -1;
        private SeatMarker _claimedMarker;
        private bool _aimClaimable;

        public bool IsSeated => _seated;
        public bool HasClaim => _claimedSeat >= 0;
        public bool CanClaimNow => _aimClaimable;

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

            // 着席中は予約せず、E で立つだけ。
            if (_seated)
            {
                _aimClaimable = false;
                if (Pressed())
                {
                    Stand();
                }
                return;
            }

            SeatMarker aim = AimedSeat();
            _aimClaimable = aim != null && _director.CanClaim(aim.Index);

            if (Pressed())
            {
                if (_aimClaimable)
                {
                    Claim(aim);
                }
                else if (aim != null && aim.Index == _claimedSeat)
                {
                    Unclaim();
                }
            }
        }

        private SeatMarker AimedSeat()
        {
            EnsureCamera();
            if (_camera != null
                && Physics.Raycast(_camera.position, _camera.forward, out RaycastHit hit, _reach)
                && hit.collider.TryGetComponent(out SeatMarker marker))
            {
                return marker;
            }
            return null;
        }

        // 予約：その席をハイライトし、Director に「ここを狙ってる」と伝える。
        private void Claim(SeatMarker marker)
        {
            if (_claimedMarker != null)
            {
                _claimedMarker.SetHighlighted(false);
            }
            _claimedMarker = marker;
            _claimedSeat = marker.Index;
            marker.SetHighlighted(true);
            _director.SetPlayerClaim(_claimedSeat);
        }

        private void Unclaim()
        {
            ClearClaimVisual();
            _director.ClearPlayerClaim();
        }

        /// <summary>予約した席が空いたとき、Director から呼ばれて滑り込む（座る唯一の手段）。</summary>
        public void SlideInto(SeatAnchor seat)
        {
            EnsureCamera();
            ClearClaimVisual();

            transform.SetPositionAndRotation(seat.Position, seat.Facing);
            if (_controller != null)
            {
                _controller.CanMove = false;
            }
            SetCameraHeight(_standEyeHeight - _seatedCameraDrop);
            _seated = true;
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

        private void ClearClaimVisual()
        {
            if (_claimedMarker != null)
            {
                _claimedMarker.SetHighlighted(false);
            }
            _claimedMarker = null;
            _claimedSeat = -1;
        }

        private static bool Pressed()
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

using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 椅子取りゲームの「座る」担当。空いた席は放っておくと立ち客がすぐ座りに来るので、
    /// 視線の先の空席（レイキャスト）を E で先に取る＝先に座った者勝ち。降りそうな人の近くに
    /// 立っておいて、空いた瞬間に押すのが立ち回りの核。取り合いの裁定は <see cref="CommuteDirector"/> が持つ。
    /// </summary>
    public sealed class PlayerSit : MonoBehaviour
    {
        [SerializeField] private float _reach = 2.8f;
        [SerializeField] private float _seatedCameraDrop = 0.5f;
        [SerializeField] private float _seatedLookPitch = -9f;

        private CommuteDirector _director;
        private FirstPersonController _controller;
        private Transform _camera;
        private float _standEyeHeight;
        private bool _capturedEyeHeight;

        private bool _seated;
        private int _seatIndex = -1;
        private SeatMarker _highlighted;

        /// <summary>着席中か（StaminaSystem や HUD が読む）。</summary>
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

            if (_seated)
            {
                CanSitNow = false;
                return;
            }

            // 小休止・研修・ランキング表示中は席取り操作を受け付けない
            // （timeScale=0 でも Update は回るので、ここで止めないとポーズ越しに座れてしまう）
            if (PauseMenuView.IsOpen || TutorialView.IsOpen || RankingView.IsOpen)
            {
                CanSitNow = false;
                Highlight(null);
                return;
            }

            SeatMarker aim = AimedSeat();
            bool grabbable = aim != null && _director.IsSeatGrabbable(aim.Index);
            CanSitNow = grabbable;
            Highlight(grabbable ? aim : null);

            if (Pressed() && grabbable && _director.TryPlayerSit(aim.Index))
            {
                SitDown(_director.GetSeat(aim.Index));
            }
        }

        /// <summary>画面中央からのレイが当たっている座席マーカー（無ければ null）。</summary>
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

        private void Highlight(SeatMarker marker)
        {
            if (_highlighted == marker)
            {
                return;
            }
            if (_highlighted != null)
            {
                _highlighted.SetTargeted(false);
            }
            _highlighted = marker;
            if (_highlighted != null)
            {
                _highlighted.SetTargeted(true);
            }
        }

        private void SitDown(SeatAnchor seat)
        {
            EnsureCamera();
            Highlight(null);

            _seatIndex = seat.Index;
            // 乗客と同じ前方オフセット（端0.15/中0.30）で座る＝頭が背もたれに刺さらない自然な位置
            Vector3 pos = seat.Position + seat.Facing * Vector3.forward * _director.GetSeatForward(seat.Index);
            transform.SetPositionAndRotation(pos, seat.Facing);
            if (_controller != null)
            {
                _controller.CanMove = false;
            }
            SetCameraHeight(_standEyeHeight - _seatedCameraDrop);
            if (_controller != null)
            {
                _controller.SetPitch(_seatedLookPitch);
            }
            // 腰を下ろした瞬間、カメラがコクッと沈んで戻る（着席の手応え）
            if (CameraJuice.Active != null)
            {
                CameraJuice.Active.Kick(new Vector3(5f, 0f, 1.5f));
            }
            _seated = true;
        }

        /// <summary>乗り換えで強制的に立たされる。Director から呼ばれる（席の清算は Director 側で済んでいる）。</summary>
        public void ResetForTransfer()
        {
            Highlight(null);
            if (_seated)
            {
                StandUp();
                _seatIndex = -1;
            }
        }

        private void StandUp()
        {
            if (_controller != null)
            {
                _controller.CanMove = true;
                _controller.SetPitch(0f);
            }
            SetCameraHeight(_standEyeHeight);
            _seated = false;
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

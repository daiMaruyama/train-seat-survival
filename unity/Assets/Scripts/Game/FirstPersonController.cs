using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 小さく自己完結した一人称コントローラ（Starter Assets 不使用＝全行が自分たちのもので読める）。
    /// 当たり判定のある歩行を <see cref="CharacterController"/> で動かし、体を左右（yaw）に、カメラを
    /// 上下（pitch）に回す。入力は新 Input System（キーボード／マウス）から直接読み、プロトタイプの間は
    /// InputActions アセットを用意せずに済ませている。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 3.2f;
        [SerializeField] private float _lookSensitivity = 0.08f;
        [SerializeField] private float _eyeHeight = 1.6f;
        [SerializeField] private float _gravity = -12f;
        [SerializeField] private float _pitchLimit = 80f;

        private CharacterController _controller;
        private Transform _camera;
        private float _pitch;
        private float _verticalVelocity;

        /// <summary>false の間は歩行を止める（例：着席中）。見回しはそのまま効く。</summary>
        public bool CanMove { get; set; } = true;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // 立っている乗客の形にカプセルを整える（足元が GameObject の原点）。
            _controller.height = 1.8f;
            _controller.radius = 0.3f;
            _controller.center = new Vector3(0f, 0.9f, 0f);
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;

            Camera cam = Camera.main;
            if (cam != null)
            {
                _camera = cam.transform;
                _camera.SetParent(transform);
                _camera.localPosition = new Vector3(0f, _eyeHeight, 0f);
                _camera.localRotation = Quaternion.identity;
            }
        }

        private void Update()
        {
            HandleLook();
            HandleMove();

            // 使い勝手用：Esc でカーソルを解放し、テスト中に他をクリックできるように。
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void HandleLook()
        {
            if (_camera == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Vector2 delta = Mouse.current.delta.ReadValue() * _lookSensitivity;
            transform.Rotate(Vector3.up, delta.x);

            _pitch = Mathf.Clamp(_pitch - delta.y, -_pitchLimit, _pitchLimit);
            _camera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void HandleMove()
        {
            if (!CanMove)
            {
                return;
            }

            Vector2 input = ReadMoveInput();
            Vector3 move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }
            _verticalVelocity += _gravity * Time.deltaTime;

            Vector3 velocity = move * _moveSpeed + Vector3.up * _verticalVelocity;
            _controller.Move(velocity * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return Vector2.zero;
            }

            Vector2 input = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
            return input;
        }
    }
}

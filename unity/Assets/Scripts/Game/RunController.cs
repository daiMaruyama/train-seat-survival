using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1ラン（連続する通勤の日々）の管理。体力（寿命）が尽きたら「倒れた」＝ラン終了。
    /// 演出は <see cref="CutInView"/> に任せる：まずスローモーションで赤いカットイン、決まった瞬間に
    /// 時間を完全停止し、リザルト（何日目・通算駅数）を出す。R かリトライボタンでやり直し。
    /// 疲労のルールは <see cref="StaminaSystem"/> 側にあり、ここは終了判定とリスタートだけを持つ。
    /// </summary>
    [RequireComponent(typeof(StaminaSystem))]
    public sealed class RunController : MonoBehaviour
    {
        [SerializeField] private float _slowMotionScale = 0.2f; // 倒れた瞬間のスロー再生

        private StaminaSystem _stamina;
        private CommuteDirector _director;
        private CutInView _cutIn;

        public bool IsOver { get; private set; }

        private void Awake()
        {
            _stamina = GetComponent<StaminaSystem>();
        }

        private void Start()
        {
            Time.timeScale = 1f; // 前のランで止めた時間を戻す
            _director = FindFirstObjectByType<CommuteDirector>();
            _cutIn = FindFirstObjectByType<CutInView>();
        }

        private void Update()
        {
            if (!IsOver)
            {
                if (_stamina != null && _stamina.IsEmpty)
                {
                    Die();
                }
                return;
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                Restart();
            }
        }

        /// <summary>倒れた：その場に倒れ込むカメラ＋じわ暗転→（落ちきったら）完全停止→リザルト。</summary>
        private void Die()
        {
            IsOver = true;
            GameAudio.Instance.Play(GameAudio.Sfx.GameOver);
            Cursor.lockState = CursorLockMode.None;

            int days = _director != null ? _director.Leg + 1 : 1;
            int stations = _director != null ? _director.TotalStationsSurvived : 0;

            // 視点操作を止めて「通勤中に倒れる」一人称演出へ
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.enabled = false;
            }
            StartCoroutine(CollapseCamera());

            if (_cutIn != null)
            {
                Time.timeScale = _slowMotionScale; // 背後の世界はスローに
                _cutIn.PlayGameOver(
                    "過労で倒れてしまった",
                    $"{days}日目の朝 / 通算 {stations} 駅",
                    onCovered: () => Time.timeScale = 0f,
                    onRestart: Restart);
            }
            else
            {
                Time.timeScale = 0f;
            }
        }

        /// <summary>カメラが傾きながら床へ崩れ落ちる（unscaled 駆動なのでスロー/停止中も動く）。</summary>
        private IEnumerator CollapseCamera()
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null)
            {
                yield break;
            }

            Vector3 startPos = cam.localPosition;
            Quaternion startRot = cam.localRotation;
            Vector3 endPos = new Vector3(startPos.x + 0.15f, 0.32f, startPos.z);
            Quaternion endRot = startRot * Quaternion.Euler(12f, 0f, 74f); // 横倒れ＋少しうつむく

            float t = 0f;
            const float duration = 1.1f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                float e = t * t * (3f - 2f * t); // smoothstep：始まりゆっくり→加速→着地は緩む
                cam.localPosition = Vector3.Lerp(startPos, endPos, e);
                cam.localRotation = Quaternion.Slerp(startRot, endRot, e);
                yield return null;
            }
        }

        /// <summary>やり直し（R キー／リトライボタン共通）。</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }
    }
}

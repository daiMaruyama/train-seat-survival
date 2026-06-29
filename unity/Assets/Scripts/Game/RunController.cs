using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1ラン（1日の通勤）の管理。体力（寿命）が尽きたら「倒れた」＝ラン終了にして時間を止め、
    /// R でやり直す。疲労のルールは <see cref="StaminaSystem"/> 側にあり、ここは終了判定とリスタート
    /// だけを持つ（単一責任）。
    /// </summary>
    [RequireComponent(typeof(StaminaSystem))]
    public sealed class RunController : MonoBehaviour
    {
        private StaminaSystem _stamina;

        public bool IsOver { get; private set; }

        private void Awake()
        {
            _stamina = GetComponent<StaminaSystem>();
        }

        private void Start()
        {
            Time.timeScale = 1f; // 前のランで止めた時間を戻す。
        }

        private void Update()
        {
            if (!IsOver)
            {
                if (_stamina != null && _stamina.IsEmpty)
                {
                    IsOver = true;
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                }
                return;
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                Time.timeScale = 1f;
                Scene scene = SceneManager.GetActiveScene();
                SceneManager.LoadScene(scene.buildIndex);
            }
        }
    }
}

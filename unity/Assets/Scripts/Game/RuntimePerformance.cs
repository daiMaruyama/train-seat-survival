using UnityEngine;
using UnityEngine.InputSystem;

namespace TrainSurvival.Game
{
    public static class RuntimePerformance
    {
        private const string FrameRateKey = "RuntimeFrameRate";
        private const int DefaultFrameRate = 45;
        private static readonly int[] FrameRateOptions = { 30, 45, 60 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int frameRate = PlayerPrefs.GetInt(FrameRateKey, DefaultFrameRate);
            ApplyFrameRate(frameRate);

            var go = new GameObject("RuntimePerformance");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Driver>();
        }

        private static void ApplyFrameRate(int frameRate)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Mathf.Clamp(frameRate, 15, 120);
        }

        private sealed class Driver : MonoBehaviour
        {
            private void Update()
            {
                Keyboard kb = Keyboard.current;
                if (kb == null || !kb.f10Key.wasPressedThisFrame)
                {
                    return;
                }

                int current = Application.targetFrameRate > 0 ? Application.targetFrameRate : DefaultFrameRate;
                int next = FrameRateOptions[0];
                for (int i = 0; i < FrameRateOptions.Length; i++)
                {
                    if (current <= FrameRateOptions[i])
                    {
                        next = FrameRateOptions[(i + 1) % FrameRateOptions.Length];
                        break;
                    }
                }

                PlayerPrefs.SetInt(FrameRateKey, next);
                ApplyFrameRate(next);
                Debug.Log($"[RuntimePerformance] targetFrameRate={next}");
            }
        }
    }
}

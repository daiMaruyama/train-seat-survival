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
        private bool _restarting;

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

            // 危険度に連動する視界演出（心音とセット）。シーンに無ければここで立てる
            if (FindFirstObjectByType<DangerVisionFx>() == null)
            {
                new GameObject("DangerVisionFx").AddComponent<DangerVisionFx>();
            }
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

            // ランキング表示中／明細への署名タイプ中は R を拾わない（サインの「r」で誤リトライしない）
            if (RankingView.IsOpen || (_cutIn != null && _cutIn.IsAwaitingSignature))
            {
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
            GameAudio.Instance.SetTrainMoving(false);
            GameAudio.Instance.SetHeartbeat(false);
            GameAudio.Instance.Play(GameAudio.Sfx.GameOver);
            Cursor.lockState = CursorLockMode.None;

            int days = _director != null ? _director.Leg + 1 : 1;
            int stations = _director != null ? _director.TotalStationsSurvived : 0;

            // ランキングへ記録（ローカル保存。共有DBを繋いだらここから送信も行う）
            RankingStore.Record(new RankingEntry
            {
                name = PlayerProfile.Name,
                days = days,
                stations = stations,
                yen = Payroll.Annual(days, stations), // スコア＝推定年収
                ticks = System.DateTime.Now.Ticks,
            });

            // 視点操作を止めて「通勤中に倒れる」一人称演出へ
            var fpc = GetComponent<FirstPersonController>();
            if (fpc != null)
            {
                fpc.enabled = false;
            }

            // リザルト中は通常HUD（左上の「N日目」カード等）を消す。
            // ※HudView と CutInView は同じ 'HUD' GameObject に同居しているので、GameObject 自体は
            // 　消さず HUD のキャンバスだけ切る（消すと CutInView まで止まりリザルトが出ない）。
            var hud = FindFirstObjectByType<HudView>();
            if (hud != null)
            {
                hud.SetHudVisible(false);
            }
            StartCoroutine(CollapseCamera());

            if (_cutIn != null)
            {
                Time.timeScale = _slowMotionScale; // 背後の世界はスローに
                _cutIn.PlayGameOver(
                    days,
                    stations,
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
                // 崩れ落ちる途中の細かな揺れ（体の重さ）。着地に近づくほど強くする
                Vector3 fallNoise = Random.insideUnitSphere * (0.6f + 2.2f * Mathf.Max(0f, e - 0.75f) * 4f);
                cam.localPosition = Vector3.Lerp(startPos, endPos, e);
                cam.localRotation = Quaternion.Slerp(startRot, endRot, e) * Quaternion.Euler(fallNoise);
                yield return null;
            }

            // 着地の“ドサッ”という余韻の揺れ（減衰）
            float s = 0f;
            const float settle = 0.42f;
            while (s < settle)
            {
                s += Time.unscaledDeltaTime;
                float k = 1f - s / settle;
                Vector3 n = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * k * 3f;
                cam.localRotation = endRot * Quaternion.Euler(n);
                cam.localPosition = endPos + Random.insideUnitSphere * k * 0.018f;
                yield return null;
            }
            cam.localPosition = endPos;
            cam.localRotation = endRot;
        }

        /// <summary>やり直し（R キー／リトライボタン共通）。</summary>
        public void Restart()
        {
            if (_restarting)
            {
                return;
            }
            _restarting = true;

            if (_cutIn != null)
            {
                _cutIn.PlaySceneFadeOut(LoadRestartScene);
            }
            else
            {
                LoadRestartScene();
            }
        }

        private void LoadRestartScene()
        {
            Time.timeScale = 1f;
            RunStartContext.RequestOpeningDayTransition();
            Scene scene = SceneManager.GetActiveScene();
            SceneManager.LoadSceneAsync(scene.buildIndex);
        }
    }
}

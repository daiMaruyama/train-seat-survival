using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 一時停止メニュー（ESC）。世界観は「トイレ休憩でサボる社畜」——暗い掲示札に
    /// 『小休止中』。マスター／BGM／SE音量とマウス感度のスライダー、再開・遊び方・タイトルの導線。
    /// 開いている間は timeScale=0（演出系は unscaled なので影響なし）。リザルト中・ランキング表示中・
    /// カットイン中・チュートリアル中は開かない。
    /// </summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        private static readonly Color PanelBg = new Color(0.05f, 0.06f, 0.10f, 0.97f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);

        /// <summary>ポーズ中か（他システムの入力ガード用）。</summary>
        public static bool IsOpen { get; private set; }

        private RunController _run;
        private CutInView _cutIn;
        private FirstPersonController _fpc;
        private GameObject _panelRoot;

        private void Start()
        {
            _run = FindFirstObjectByType<RunController>();
            _cutIn = FindFirstObjectByType<CutInView>();
            _fpc = FindFirstObjectByType<FirstPersonController>();
            Build();
        }

        private void OnDestroy()
        {
            IsOpen = false; // シーン遷移で開きっぱなしフラグを残さない
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            if (IsOpen)
            {
                Resume();
            }
            else if (CanOpen())
            {
                Open();
            }
        }

        private bool CanOpen()
        {
            return (_run == null || !_run.IsOver)
                && !RankingView.IsOpen
                && !TutorialView.IsOpen
                && (_cutIn == null || !_cutIn.IsBusy);
        }

        private void Open()
        {
            IsOpen = true;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _panelRoot.SetActive(true);
        }

        private void Resume()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _panelRoot.SetActive(false);
        }

        private void ShowHelp()
        {
            // ポーズは閉じて研修のしおりへ（しおり側が停止を引き継ぎ、閉じると再開する）
            IsOpen = false;
            _panelRoot.SetActive(false);
            TutorialView.Show();
        }

        private void GoTitle()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            RunStartContext.RequestTitleFadeInTransition();
            SceneManager.LoadScene("Title");
        }

        private void Build()
        {
            var canvasGo = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 35; // HUDより前・ランキング(40)より後ろ
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            _panelRoot = new GameObject("PauseRoot", typeof(RectTransform));
            var panelRootRect = _panelRoot.GetComponent<RectTransform>();
            panelRootRect.SetParent(root, false);
            Stretch(panelRootRect);

            Image dim = CreateImage("Dim", panelRootRect, new Color(0f, 0f, 0f, 0.66f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true; // 背後のゲームUIを触らせない

            // 掲示札
            Image panel = CreateImage("Panel", panelRootRect, PanelBg);
            panel.raycastTarget = true;
            RectTransform pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(640f, 700f);
            UiKit.Panelize(panel, 18);
            UiKit.AddShadow(panel, 18, blur: 36, alpha: 0.55f, offset: new Vector2(0f, -12f));
            UiKit.AddFrame(panel, new Color(0.96f, 0.94f, 0.89f, 0.55f)); // 駅サインの額縁

            // 左端の路線カラー帯（HUDカードと同じ言語）
            Image accent = CreateImage("Accent", pr, AccentOrange);
            accent.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            accent.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = new Vector2(10f, 0f);
            accent.rectTransform.sizeDelta = new Vector2(6f, 640f);
            UiKit.Panelize(accent, 3);

            Text title = CreateText("Title", pr, 40, TextAnchor.MiddleLeft);
            title.text = "小休止中";
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, new Vector2(44f, -30f), new Vector2(400f, 48f));

            Text sub = CreateText("Sub", pr, 20, TextAnchor.MiddleLeft);
            sub.text = "（上司に見つかりませんように）";
            sub.color = new Color(1f, 1f, 1f, 0.55f);
            Place(sub.rectTransform, new Vector2(46f, -78f), new Vector2(420f, 26f));

            // スライダー群（共通スタイル）
            GameAudio audio = GameAudio.Instance;
            UiKit.MakeSlider(pr, new Vector2(0f, 96f), 520f, "マスター音量", audio.MasterVolume, 0f, 1f, v => audio.MasterVolume = v);
            UiKit.MakeSlider(pr, new Vector2(0f, 44f), 520f, "BGM音量", audio.BgmVolume, 0f, 1f, v => audio.BgmVolume = v);
            UiKit.MakeSlider(pr, new Vector2(0f, -8f), 520f, "SE音量", audio.SeVolume, 0f, 1f, v => audio.SeVolume = v);
            float sens = _fpc != null ? _fpc.LookSensitivity : 0.08f;
            UiKit.MakeSlider(pr, new Vector2(0f, -60f), 520f, "マウス感度", sens, 0.02f, 0.24f, v => { if (_fpc != null) _fpc.LookSensitivity = v; });

            // 導線（共通ボタン）
            UiKit.MakeButton(pr, new Vector2(0f, -140f), new Vector2(360f, 76f), "仕事に戻る", 26, Resume);
            UiKit.MakeButton(pr, new Vector2(0f, -228f), new Vector2(360f, 76f), "遊び方を見る", 24, ShowHelp);
            UiKit.MakeButton(pr, new Vector2(0f, -316f), new Vector2(360f, 76f), "退職する（タイトルへ）", 22, GoTitle);

            _panelRoot.SetActive(false);
        }

        private static void Place(RectTransform rt, Vector2 topLeftPos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeftPos;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string goName, Transform parent, Color color)
        {
            var go = new GameObject(goName, typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string goName, Transform parent, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(goName, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UiFont.Load();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 通勤テーマのカットイン演出（描画専用・ルールは持たない）。
    /// ・日替わり：暗がりのカバーがかかり、光る窓の列＝電車が通過していく。駅名標スタイルの
    /// 　「つぎは ▶ N日目」がコトンと降りて、電車が行ってしまうと新しい朝
    /// ・ゲームオーバー：暗転の中に「運行終了」の札とリザルト（何日目・通算駅数）＋リトライ
    /// 　（倒れ込むカメラ演出は RunController 側が担当）
    /// アニメーションはすべて unscaled 時間で駆動する（timeScale=0 でも動く）。
    /// </summary>
    public sealed class CutInView : MonoBehaviour
    {
        // 車内と同じ落ち着いたパレット（オレンジは細い差し色だけ）
        private static readonly Color CoverNavy = new Color(0.07f, 0.09f, 0.14f);
        private static readonly Color TrainBody = new Color(0.13f, 0.15f, 0.21f);
        private static readonly Color WindowGlow = new Color(1f, 0.86f, 0.55f, 0.95f);
        private static readonly Color SpeedLine = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color SignPaper = new Color(0.94f, 0.93f, 0.89f);
        private static readonly Color SignInk = new Color(0.13f, 0.14f, 0.17f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);

        private RectTransform _dayRoot;
        private RectTransform _cover;
        private RectTransform _train;
        private RectTransform _sign;
        private Text _signDay;

        private RectTransform _overRoot;
        private Image _overDark;
        private CanvasGroup _resultGroup;
        private Text _resultHeadline;
        private Text _resultSub;
        private Action _onRestart;

        /// <summary>演出再生中か（多重再生ガード）。</summary>
        public bool IsBusy { get; private set; }

        private void Awake()
        {
            Build();
        }

        /// <summary>日替わり演出。画面が覆われた瞬間に onCovered、抜け切ったら onDone。</summary>
        public void PlayDayTransition(string label, Action onCovered, Action onDone)
        {
            if (IsBusy)
            {
                onCovered?.Invoke();
                onDone?.Invoke();
                return;
            }
            StartCoroutine(DayRoutine(label, onCovered, onDone));
        }

        /// <summary>ゲームオーバー演出。暗転しきった瞬間に onCovered（ここで時間を止める想定）。</summary>
        public void PlayGameOver(string headline, string subline, Action onCovered, Action onRestart)
        {
            if (IsBusy)
            {
                return;
            }
            _onRestart = onRestart;
            StartCoroutine(GameOverRoutine(headline, subline, onCovered));
        }

        // ---- 日替わり：電車の通過 ------------------------------------------

        private IEnumerator DayRoutine(string label, Action onCovered, Action onDone)
        {
            IsBusy = true;
            _dayRoot.gameObject.SetActive(true);
            _signDay.text = label;
            _sign.anchoredPosition = new Vector2(0f, 240f);
            _train.anchoredPosition = new Vector2(-2600f, 40f);

            // 夜明け前の暗がりが右からかかる
            yield return Animate(0.4f, t => SetX(_cover, Mathf.Lerp(2000f, 0f, EaseOutCubic(t))));
            onCovered?.Invoke();

            // 電車（光る窓の列）が通過していく＋駅名標がコトンと降りる
            StartCoroutine(Animate(2.1f, t => SetX(_train, Mathf.Lerp(-2600f, 2600f, t))));
            yield return WaitUnscaled(0.25f);
            yield return Animate(0.35f, t =>
                _sign.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(240f, 0f, EaseOutBack(t))));

            yield return WaitUnscaled(1.0f);

            // 駅名標が上へ抜けて、暗がりが左へ晴れる
            yield return Animate(0.25f, t =>
                _sign.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 260f, EaseInCubic(t))));
            yield return Animate(0.4f, t => SetX(_cover, Mathf.Lerp(0f, -2000f, EaseInCubic(t))));

            _dayRoot.gameObject.SetActive(false);
            IsBusy = false;
            onDone?.Invoke();
        }

        // ---- ゲームオーバー：運行終了 --------------------------------------

        private IEnumerator GameOverRoutine(string headline, string subline, Action onCovered)
        {
            IsBusy = true;
            _overRoot.gameObject.SetActive(true);
            _resultHeadline.text = headline;
            _resultSub.text = subline;
            _resultGroup.alpha = 0f;
            _resultGroup.interactable = false;
            _resultGroup.blocksRaycasts = false;

            // 意識が落ちるようにじわっと暗転（この間、カメラは倒れ込んでいる）
            Color dark = _overDark.color;
            yield return Animate(1.15f, t =>
            {
                dark.a = Mathf.Lerp(0f, 1f, EaseInCubic(t));
                _overDark.color = dark;
            });
            onCovered?.Invoke();

            yield return WaitUnscaled(0.4f);

            // 「運行終了」の札とリザルトがせり上がる
            RectTransform resultRect = _resultGroup.GetComponent<RectTransform>();
            yield return Animate(0.4f, t =>
            {
                _resultGroup.alpha = EaseOutCubic(t);
                resultRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-40f, 0f, EaseOutCubic(t)));
            });
            _resultGroup.interactable = true;
            _resultGroup.blocksRaycasts = true;
            // IsBusy は立てたまま（リスタートでシーンごと破棄される）
        }

        // ---- 組み立て ------------------------------------------------------

        private void Build()
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("CutInCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // HUD より前面
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            BuildDay(root);
            BuildGameOver(root);
        }

        private void BuildDay(RectTransform root)
        {
            _dayRoot = CreateStretched("DayRoot", root);

            // 暗がりのカバー（画面より大きめの1枚）
            Image cover = CreateImage("Cover", _dayRoot, CoverNavy);
            _cover = cover.rectTransform;
            _cover.sizeDelta = new Vector2(2400f, 1400f);

            // うっすら流れるスピード線（カバーに焼き付け）
            for (int i = 0; i < 5; i++)
            {
                Image line = CreateImage($"Speed_{i}", _cover, SpeedLine);
                line.rectTransform.sizeDelta = new Vector2(2400f, 3f);
                line.rectTransform.anchoredPosition = new Vector2(0f, -420f + i * 210f);
            }

            // 通過する電車＝車体の帯＋光る窓の列＋オレンジの帯（中央線）
            _train = new GameObject("Train", typeof(RectTransform)).GetComponent<RectTransform>();
            _train.SetParent(_dayRoot, false);
            Image body = CreateImage("Body", _train, TrainBody);
            body.rectTransform.sizeDelta = new Vector2(2400f, 320f);
            Image stripe = CreateImage("Stripe", _train, AccentOrange);
            stripe.rectTransform.sizeDelta = new Vector2(2400f, 22f);
            stripe.rectTransform.anchoredPosition = new Vector2(0f, -96f);
            for (int i = 0; i < 12; i++)
            {
                Image window = CreateImage($"Win_{i}", _train, WindowGlow);
                window.rectTransform.sizeDelta = new Vector2(120f, 110f);
                window.rectTransform.anchoredPosition = new Vector2(-1100f + i * 200f, 26f);
            }

            // 駅名標スタイルの日付（白地に濃紺の文字＋オレンジの下線）
            _sign = new GameObject("Sign", typeof(RectTransform)).GetComponent<RectTransform>();
            _sign.SetParent(_dayRoot, false);
            _sign.anchoredPosition = new Vector2(0f, 240f);
            Image paper = CreateImage("Paper", _sign, SignPaper);
            paper.rectTransform.sizeDelta = new Vector2(560f, 200f);
            Image underline = CreateImage("Underline", _sign, AccentOrange);
            underline.rectTransform.sizeDelta = new Vector2(480f, 10f);
            underline.rectTransform.anchoredPosition = new Vector2(0f, -62f);

            Text next = CreateText("Next", _sign, 26, TextAnchor.MiddleCenter);
            next.text = "つ ぎ は";
            next.color = new Color(SignInk.r, SignInk.g, SignInk.b, 0.75f);
            next.rectTransform.anchoredPosition = new Vector2(0f, 62f);
            next.rectTransform.sizeDelta = new Vector2(480f, 32f);

            _signDay = CreateText("Day", _sign, 76, TextAnchor.MiddleCenter);
            _signDay.color = SignInk;
            _signDay.fontStyle = FontStyle.Bold;
            _signDay.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            _signDay.rectTransform.sizeDelta = new Vector2(520f, 100f);

            _dayRoot.gameObject.SetActive(false);
        }

        private void BuildGameOver(RectTransform root)
        {
            _overRoot = CreateStretched("GameOverRoot", root);

            _overDark = CreateImage("Dark", _overRoot, new Color(0.02f, 0.02f, 0.04f, 0f));
            Stretch(_overDark.rectTransform);

            // リザルト（運行終了の札＋見出し＋通算＋リトライ）
            var resultGo = new GameObject("Result", typeof(RectTransform), typeof(CanvasGroup));
            var resultRect = resultGo.GetComponent<RectTransform>();
            resultRect.SetParent(_overRoot, false);
            resultRect.anchoredPosition = new Vector2(0f, -40f);
            resultRect.sizeDelta = new Vector2(1000f, 420f);
            _resultGroup = resultGo.GetComponent<CanvasGroup>();

            // 「運行終了」の札（行先表示風：濃紺地にオレンジ枠）
            var plate = new GameObject("Plate", typeof(RectTransform)).GetComponent<RectTransform>();
            plate.SetParent(resultRect, false);
            plate.anchoredPosition = new Vector2(0f, 150f);
            Image plateFrame = CreateImage("Frame", plate, AccentOrange);
            plateFrame.rectTransform.sizeDelta = new Vector2(332f, 92f);
            Image plateBody = CreateImage("Body", plate, new Color(0.10f, 0.12f, 0.18f));
            plateBody.rectTransform.sizeDelta = new Vector2(320f, 80f);
            Text plateText = CreateText("Text", plate, 40, TextAnchor.MiddleCenter);
            plateText.text = "運 行 終 了";
            plateText.color = new Color(0.95f, 0.93f, 0.88f);
            plateText.fontStyle = FontStyle.Bold;
            plateText.rectTransform.sizeDelta = new Vector2(320f, 80f);

            _resultHeadline = CreateText("Headline", resultRect, 48, TextAnchor.MiddleCenter);
            _resultHeadline.color = new Color(0.95f, 0.93f, 0.88f);
            _resultHeadline.fontStyle = FontStyle.Bold;
            _resultHeadline.rectTransform.anchoredPosition = new Vector2(0f, 55f);
            _resultHeadline.rectTransform.sizeDelta = new Vector2(1000f, 64f);

            _resultSub = CreateText("Sub", resultRect, 32, TextAnchor.MiddleCenter);
            _resultSub.color = new Color(1f, 1f, 1f, 0.65f);
            _resultSub.rectTransform.anchoredPosition = new Vector2(0f, 0f);
            _resultSub.rectTransform.sizeDelta = new Vector2(1000f, 42f);

            // リトライボタン（落ち着いたオレンジ）
            var buttonGo = new GameObject("Retry", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.SetParent(resultRect, false);
            buttonRect.anchoredPosition = new Vector2(0f, -85f);
            buttonRect.sizeDelta = new Vector2(300f, 62f);
            var buttonImage = buttonGo.GetComponent<Image>();
            buttonImage.color = new Color(0.85f, 0.40f, 0.14f);
            var button = buttonGo.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.58f, 0.30f);
            colors.pressedColor = new Color(0.66f, 0.30f, 0.10f);
            button.colors = colors;
            button.onClick.AddListener(() => _onRestart?.Invoke());
            Text buttonText = CreateText("Text", buttonRect, 28, TextAnchor.MiddleCenter);
            buttonText.text = "明日も出勤する";
            buttonText.color = Color.white;
            buttonText.fontStyle = FontStyle.Bold;
            Stretch(buttonText.rectTransform);

            Text hint = CreateText("Hint", resultRect, 20, TextAnchor.MiddleCenter);
            hint.text = "R でもやり直せます";
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, -140f);
            hint.rectTransform.sizeDelta = new Vector2(600f, 28f);

            _overRoot.gameObject.SetActive(false);
        }

        // ---- 小道具 --------------------------------------------------------

        /// <summary>uGUI ボタンに必要な EventSystem（新 Input System 用）が無ければ作る。</summary>
        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(go);
        }

        private static RectTransform CreateStretched(string goName, RectTransform parent)
        {
            var rect = new GameObject(goName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
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
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void SetX(RectTransform rect, float x)
        {
            Vector2 p = rect.anchoredPosition;
            p.x = x;
            rect.anchoredPosition = p;
        }

        /// <summary>timeScale の影響を受けないアニメ駆動（0..1 の生 t を渡す）。</summary>
        private static IEnumerator Animate(float duration, Action<float> apply)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / duration);
                apply(t);
                yield return null;
            }
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        private static float EaseInCubic(float t) => t * t * t;

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}

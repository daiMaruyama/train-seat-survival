using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 通勤テーマのカットイン演出（描画専用・ルールは持たない）。
    /// ・日替わり：暗がりのカバーがかかり、光る窓の列＝電車が通過していく。駅名標スタイルの
    /// 　「つぎは ▶ N日目」がコトンと降りて、電車が行ってしまうと新しい朝
    /// ・ゲームオーバー：暗転の中に「過労で倒れてしまった」ことを伝える札とリザルト＋導線
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

        private const float Pad = 48f; // 明細の紙の左右余白

        private RectTransform _overRoot;
        private Image _overDark;
        private CanvasGroup _resultGroup;
        private Text _subLine, _vDays, _vStations, _vBase, _vAllow, _vDeduct, _vTotal;
        private Text _rankBig, _rankLabel, _comment;
        private Image _stampCircle;
        private RectTransform _signStrip;
        private InputField _signField;
        private Transform _signSeal;
        private Image _signPanel;
        private Text _rankingEntryText;
        private Text _rankingGuideText;
        private GameObject _actionsRoot;
        private bool _awaitingSignature;
        private bool _signatureDone;
        private Action _onRestart;

        /// <summary>署名待ちか（サインするまでリトライ等の入力をブロックする）。</summary>
        public bool IsAwaitingSignature => _awaitingSignature && !_signatureDone;
        private CanvasGroup _sceneFade;
        private bool _sceneTransitioning;
        [SerializeField] private string _titleSceneName = "Title";

        /// <summary>演出再生中か（多重再生ガード）。</summary>
        public bool IsBusy { get; private set; }
        public bool IsSceneTransitioning => _sceneTransitioning;

        private void Awake()
        {
            Build();
            if (RunStartContext.IsOpeningDayTransitionRequested)
            {
                PrepareOpeningDayTransition("1日目");
            }
        }

        private void Update()
        {
            // リザルト表示中のキーショートカット（R=リトライは RunController 側が担当）
            if (_resultGroup == null || !_resultGroup.interactable || RankingView.IsOpen || IsAwaitingSignature)
            {
                return; // ランキング表示中／署名タイプ中は T/L を拾わない
            }
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }
            if (kb.tKey.wasPressedThisFrame)
            {
                ReturnToTitle();
            }
            else if (kb.lKey.wasPressedThisFrame)
            {
                ShowRanking();
            }
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

        /// <summary>Title→InGame専用。すでに暗幕が被った状態から始め、ゲーム画面のチラ見えを防ぐ。</summary>
        public void PlayOpeningDayTransition(string label, Action onDone)
        {
            if (IsBusy)
            {
                onDone?.Invoke();
                return;
            }
            StartCoroutine(OpeningDayRoutine(label, onDone));
        }

        /// <summary>ゲームオーバー演出：勤務評定（給与明細）を出す。暗転しきった瞬間に onCovered。</summary>
        public void PlayGameOver(int days, int stations, Action onCovered, Action onRestart)
        {
            if (IsBusy)
            {
                return;
            }
            _onRestart = onRestart;
            FillAssessment(days, stations);
            StartCoroutine(GameOverRoutine(onCovered));
        }

        /// <summary>リザルトから別シーンへ移る直前に、最前面を黒で覆う。</summary>
        public void PlaySceneFadeOut(Action onCovered)
        {
            if (_sceneTransitioning)
            {
                return;
            }
            _sceneTransitioning = true;
            StartCoroutine(SceneFadeOutRoutine(onCovered));
        }

        // ---- 日替わり：電車の通過 ------------------------------------------

        private IEnumerator DayRoutine(string label, Action onCovered, Action onDone)
        {
            IsBusy = true;
            PrepareDayTransition(label, 2000f, 240f);

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

        private IEnumerator OpeningDayRoutine(string label, Action onDone)
        {
            IsBusy = true;
            PrepareOpeningDayTransition(label);

            StartCoroutine(Animate(2.1f, t => SetX(_train, Mathf.Lerp(-2600f, 2600f, t))));
            yield return WaitUnscaled(0.85f);

            yield return Animate(0.25f, t =>
                _sign.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 260f, EaseInCubic(t))));
            yield return Animate(0.4f, t => SetX(_cover, Mathf.Lerp(0f, -2000f, EaseInCubic(t))));

            _dayRoot.gameObject.SetActive(false);
            IsBusy = false;
            onDone?.Invoke();
        }

        private void PrepareOpeningDayTransition(string label)
        {
            PrepareDayTransition(label, 0f, 0f);
        }

        private void PrepareDayTransition(string label, float coverX, float signY)
        {
            _signDay.text = label;
            _sign.anchoredPosition = new Vector2(0f, signY);
            _train.anchoredPosition = new Vector2(-2600f, 40f);
            SetX(_cover, coverX);
            _dayRoot.gameObject.SetActive(true);
        }

        // ---- ゲームオーバー：過労で倒れる ----------------------------------

        private IEnumerator GameOverRoutine(Action onCovered)
        {
            IsBusy = true;
            _overRoot.gameObject.SetActive(true);
            _resultGroup.alpha = 0f;
            _resultGroup.interactable = false;
            _resultGroup.blocksRaycasts = false;

            // ランキング入りしていたら署名欄を出し、サインが済むまでボタンを隠す
            _awaitingSignature = RankingStore.LastRecordRank >= 0 && _signField != null;
            _signatureDone = false;
            if (_signStrip != null)
            {
                _signStrip.gameObject.SetActive(_awaitingSignature);
            }
            if (_actionsRoot != null)
            {
                _actionsRoot.SetActive(!_awaitingSignature);
            }
            if (_stampCircle != null)
            {
                _stampCircle.transform.parent.localScale = Vector3.one; // 判子は後で押す
            }

            // 意識が落ちるようにじわっと暗転（この間、カメラは倒れ込んでいる）
            Color dark = _overDark.color;
            yield return Animate(1.15f, t =>
            {
                dark.a = Mathf.Lerp(0f, 1f, EaseInCubic(t));
                _overDark.color = dark;
            });
            onCovered?.Invoke();

            yield return WaitUnscaled(0.4f);

            // 明細が右手から“スッと手渡される”：斜めに傾いた紙が滑り込み、正面へ据えられる
            RectTransform resultRect = _resultGroup.GetComponent<RectTransform>();
            Vector2 fromPos = new Vector2(820f, -150f);
            Vector2 toPos = new Vector2(0f, 40f);
            yield return Animate(0.62f, t =>
            {
                float e = EaseOutBack(t);
                _resultGroup.alpha = EaseOutCubic(Mathf.Clamp01(t * 1.5f));
                resultRect.anchoredPosition = Vector2.LerpUnclamped(fromPos, toPos, e);
                resultRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(-16f, 0f, e));
                resultRect.localScale = Vector3.one * Mathf.LerpUnclamped(0.88f, 1f, e);
            });
            resultRect.anchoredPosition = toPos;
            resultRect.localRotation = Quaternion.identity;
            resultRect.localScale = Vector3.one;
            _resultGroup.interactable = true;
            _resultGroup.blocksRaycasts = true;

            // 判子がドンと押される
            yield return StampPunch();

            // ランキング入りなら明細の署名欄が開き、サインするまで退勤（ボタン）できない
            if (_awaitingSignature && _signField != null)
            {
                yield return WaitUnscaled(0.3f);
                FocusSignature();
            }
            // IsBusy は立てたまま（リスタートでシーンごと破棄される）
        }

        // ---- 署名（明細の紙の上でサイン→認印→退勤解禁） --------------------

        private void FocusSignature()
        {
            // onEndEdit は EventSystem の選択処理中に呼ばれるため、その場で SetSelectedGameObject すると
            // 「Attempting to select while already selecting」になる。必ず1フレーム遅らせる。
            if (isActiveAndEnabled)
            {
                StartCoroutine(FocusSignatureDeferred());
            }
        }

        private IEnumerator FocusSignatureDeferred()
        {
            yield return null;
            if (_signField == null || !_awaitingSignature || _signatureDone || EventSystem.current == null)
            {
                yield break;
            }
            EventSystem.current.SetSelectedGameObject(_signField.gameObject);
            _signField.ActivateInputField();
        }

        private void OnSignedPayslip(string value)
        {
            // シーン破棄時に InputField.OnDisable 経由でも呼ばれるため、死んだ状態では何もしない
            if (this == null || !isActiveAndEnabled || _signatureDone || !_awaitingSignature)
            {
                return;
            }

            string name = (value ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                FocusSignature(); // 空サインは受理しない
                return;
            }

            _signatureDone = true;
            PlayerProfile.Name = name;
            RankingStore.Rename(RankingStore.LastRecordedTicks, name);
            _signField.interactable = false; // 署名済みの書類は書き換えられない
            StartCoroutine(SignatureAccepted());
        }

        private IEnumerator SignatureAccepted()
        {
            if (_rankingEntryText != null)
            {
                _rankingEntryText.text = $"第 {RankingStore.LastRecordRank + 1:00} 位　登録完了";
                _rankingEntryText.color = new Color(0.08f, 0.42f, 0.32f);
            }
            if (_rankingGuideText != null)
            {
                _rankingGuideText.text = "ランキングを更新しました。順位表を表示します…";
                _rankingGuideText.color = new Color(0.08f, 0.42f, 0.32f);
            }
            if (_signPanel != null)
            {
                _signPanel.color = new Color(0.20f, 0.66f, 0.49f, 0.15f);
            }

            // 認印がドンと押される
            if (_signSeal != null)
            {
                _signSeal.gameObject.SetActive(true);
                GameAudio.Instance.Play(GameAudio.Sfx.Ding, 0.95f);
                float t = 0f;
                while (t < 1f)
                {
                    t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.16f);
                    _signSeal.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, EaseOutCubic(t));
                    yield return null;
                }
                _signSeal.localScale = Vector3.one;
            }

            // 更新完了を読める時間を置いてから発車標へ。閉じた後は退勤操作を解禁する
            yield return WaitUnscaled(1.05f);
            if (_actionsRoot != null)
            {
                _actionsRoot.SetActive(true);
            }
            RankingView.Show(RankingStore.LastRecordedTicks);
        }

        private IEnumerator SceneFadeOutRoutine(Action onCovered)
        {
            if (_resultGroup != null)
            {
                _resultGroup.interactable = false;
                _resultGroup.blocksRaycasts = false;
            }
            _sceneFade.blocksRaycasts = true;
            yield return Animate(0.4f, t => _sceneFade.alpha = EaseInCubic(t));
            _sceneFade.alpha = 1f;
            onCovered?.Invoke();
        }

        private IEnumerator StampPunch()
        {
            if (_stampCircle == null)
            {
                yield break;
            }
            Transform s = _stampCircle.transform.parent; // 回転を持つ判子コンテナ
            yield return WaitUnscaled(0.15f);
            GameAudio.Instance.Play(GameAudio.Sfx.GameOver, 0.8f);
            yield return Animate(0.16f, t => s.localScale = Vector3.one * Mathf.Lerp(2.1f, 1f, EaseOutCubic(t)));
            s.localScale = Vector3.one;
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
            BuildSceneFade(root);
        }

        private void BuildSceneFade(RectTransform root)
        {
            Image fade = CreateImage("SceneFade", root, Color.black);
            fade.raycastTarget = true;
            Stretch(fade.rectTransform);
            _sceneFade = fade.gameObject.AddComponent<CanvasGroup>();
            _sceneFade.alpha = 0f;
            _sceneFade.blocksRaycasts = false;
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

            // リザルト中は明細が主役。画面端に余白だけ残す大判サイズにする
            const float W = 1360f, H = 880f;
            var resultGo = new GameObject("Result", typeof(RectTransform), typeof(CanvasGroup));
            var resultRect = resultGo.GetComponent<RectTransform>();
            resultRect.SetParent(_overRoot, false);
            resultRect.anchoredPosition = new Vector2(0f, 34f);
            resultRect.sizeDelta = new Vector2(1500f, 1080f);
            _resultGroup = resultGo.GetComponent<CanvasGroup>();

            // 給与明細の紙（角丸＋落ち影）
            Image paper = CreateImage("Payslip", resultRect, SignPaper);
            RectTransform pr = paper.rectTransform;
            pr.sizeDelta = new Vector2(W, H);
            UiKit.Panelize(paper, 14);
            UiKit.AddShadow(paper, 14, blur: 40, alpha: 0.55f, offset: new Vector2(0f, -14f));

            // ヘッダ帯（紺）：給与明細書 ＋ 会社名
            Image header = CreateImage("Header", pr, TrainBody);
            EdgeTop(header.rectTransform, 8f, 112f, sidePad: 8f);
            UiKit.Panelize(header, 10);
            Text title = FullText("Title", header.rectTransform, 50, TextAnchor.MiddleLeft, new Color(0.96f, 0.94f, 0.89f), 46f);
            title.text = "給 与 明 細 書";
            title.fontStyle = FontStyle.Bold;
            Text company = FullText("Company", header.rectTransform, 26, TextAnchor.MiddleRight, new Color(0.96f, 0.94f, 0.89f, 0.78f), 46f);
            company.text = "株式会社 ホワイト・ハッピー";

            // 日付サブライン
            _subLine = SimpleLine(pr, 130f, 30, TextAnchor.MiddleLeft, new Color(SignInk.r, SignInk.g, SignInk.b, 0.76f));

            Rule(pr, 178f, 3f, new Color(0.13f, 0.14f, 0.17f, 0.5f));

            SectionLabel(pr, 192f, "勤務実績");
            _vDays = PayRow(pr, 230f, "出勤日数", 30, 36, false, SignInk);
            _vStations = PayRow(pr, 282f, "通過駅数", 30, 36, false, SignInk);

            Rule(pr, 340f, 2f, new Color(0.13f, 0.14f, 0.17f, 0.22f));
            SectionLabel(pr, 356f, "支給");
            _vBase = PayRow(pr, 395f, "基本給", 30, 36, false, SignInk);
            _vAllow = PayRow(pr, 448f, "精勤手当", 30, 36, false, SignInk);
            _vDeduct = PayRow(pr, 501f, "過労控除", 30, 36, false, new Color(0.82f, 0.16f, 0.12f));

            Rule(pr, 560f, 3f, new Color(0.13f, 0.14f, 0.17f, 0.5f));
            _vTotal = PayRow(pr, 574f, "差引支給額", 42, 52, true, SignInk); // 赤は控除だけ＝読みやすく

            Rule(pr, 654f, 3f, AccentOrange);
            BuildCommentBox(pr, 670f, 80f);
            BuildSignatureStrip(pr, 762f);
            BuildStamp(pr);

            // 3ボタン＋キーヒントは1コンテナに（署名が済むまで隠せるように）
            var actionsGo = new GameObject("Actions", typeof(RectTransform));
            RectTransform actions = actionsGo.GetComponent<RectTransform>();
            actions.SetParent(resultRect, false);
            Stretch(actions);
            _actionsRoot = actionsGo;

            float by = -(H * 0.5f + 58f);
            var btnSize = new Vector2(360f, 92f);
            UiKit.MakeButton(actions, new Vector2(-380f, by), btnSize, "もう一度出勤する", 27, () => _onRestart?.Invoke());
            UiKit.MakeButton(actions, new Vector2(0f, by), btnSize, "タイトルへ", 26, ReturnToTitle);
            UiKit.MakeButton(actions, new Vector2(380f, by), btnSize, "ランキング", 27, ShowRanking);

            Text hint = CreateText("Hint", actions, 22, TextAnchor.MiddleCenter);
            hint.text = "［R］リトライ　　［T］タイトル　　［L］ランキング";
            hint.color = new Color(1f, 1f, 1f, 0.55f);
            hint.fontStyle = FontStyle.Bold;
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, by - 64f);
            hint.rectTransform.sizeDelta = new Vector2(900f, 30f);

            _overRoot.gameObject.SetActive(false);
        }

        // ---- 明細の中身を実績から埋める ----------------------------------

        private void FillAssessment(int days, int stations)
        {
            // 計算式は Payroll に一元化（ランキングの「推定年収」と同じ式）
            int basePay = Mathf.Max(0, days) * Payroll.BasePerDay;
            int allowance = Mathf.Max(0, stations) * Payroll.PerStation;
            int total = Payroll.Total(days, stations);
            long annual = Payroll.Annual(days, stations);

            _subLine.text = $"第 {days} 期通勤　／　本日、過労にて力尽く　／　推定年収 ¥ {annual:#,0}";
            _vDays.text = $"{days} 日";
            _vStations.text = $"{stations} 駅";
            _vBase.text = $"¥ {basePay:#,0}";
            _vAllow.text = $"¥ {allowance:#,0}";
            _vDeduct.text = $"- ¥ {Payroll.Deduction:#,0}";
            _vTotal.text = $"¥ {total:#,0}";

            (string rank, string rankJa, string comment) = Assess(days);
            _rankBig.text = rank;
            _rankLabel.text = rankJa;
            _comment.text = comment;
            if (_rankingEntryText != null && RankingStore.LastRecordRank >= 0)
            {
                _rankingEntryText.text = $"第 {RankingStore.LastRecordRank + 1:00} 位　登録待ち";
            }
            if (_rankingGuideText != null)
            {
                _rankingGuideText.text = "名前を署名するとランキング登録が確定します";
            }
        }

        /// <summary>日数で決まる評定（ランク／肩書き／上から目線の一言）。</summary>
        private static (string rank, string rankJa, string comment) Assess(int days)
        {
            if (days <= 1) return ("D", "要 再 教 育", "初日で轟沈とはな。話にならん、もう少し働きたまえ。");
            if (days <= 3) return ("C", "平 社 員", "まだ半人前だ。その程度で音を上げるな、気合が足りん。");
            if (days <= 6) return ("B", "中 堅", "まあ及第点はやろう。昇給は…考えておいてやる。");
            if (days <= 9) return ("A", "エ ー ス", "よく粘ったじゃないか。昇進を検討してやろう。");
            return ("S", "伝 説 の 社 畜", "見事だ。役員の椅子をくれてやってもいい。");
        }

        // ---- 明細の小道具（すべて紙の上端からの yTop で配置＝崩れない） ----

        private void BuildCommentBox(RectTransform paper, float yTop, float boxH)
        {
            Image bg = CreateImage("CommentBox", paper, new Color(SignInk.r, SignInk.g, SignInk.b, 0.06f));
            EdgeTop(bg.rectTransform, yTop, boxH, sidePad: Pad);
            UiKit.Panelize(bg, 8);

            Text head = FullText("Head", bg.rectTransform, 20, TextAnchor.MiddleLeft, AccentOrange, 22f);
            head.text = "人事査定：";
            head.fontStyle = FontStyle.Bold;
            head.rectTransform.anchorMin = new Vector2(0f, 0f);
            head.rectTransform.anchorMax = new Vector2(0.15f, 1f);

            _comment = FullText("Comment", bg.rectTransform, 26, TextAnchor.MiddleLeft, SignInk, 0f);
            _comment.rectTransform.anchorMin = new Vector2(0.15f, 0f);
            _comment.rectTransform.anchorMax = new Vector2(1f, 1f);
            _comment.rectTransform.offsetMax = new Vector2(-16f, 0f);
        }

        /// <summary>
        /// 明細の末尾のランキング登録票。入賞順位と「署名で登録確定」を大きく見せる。
        /// 署名後は同じ場所を登録完了表示へ変え、ランキングが更新されたことを伝える。
        /// </summary>
        private void BuildSignatureStrip(RectTransform paper, float yTop)
        {
            var stripGo = new GameObject("Signature", typeof(RectTransform), typeof(Image));
            _signStrip = stripGo.GetComponent<RectTransform>();
            _signStrip.SetParent(paper, false);
            EdgeTop(_signStrip, yTop, 104f, sidePad: Pad);
            _signPanel = stripGo.GetComponent<Image>();
            _signPanel.color = new Color(AccentOrange.r, AccentOrange.g, AccentOrange.b, 0.11f);
            UiKit.Panelize(_signPanel, 10);
            var panelOutline = stripGo.AddComponent<Outline>();
            panelOutline.effectColor = new Color(AccentOrange.r, AccentOrange.g, AccentOrange.b, 0.62f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            Text tag = FullText("Tag", _signStrip, 15, TextAnchor.MiddleLeft, AccentOrange, 22f);
            tag.text = "RANKING ENTRY";
            tag.fontStyle = FontStyle.Bold;
            tag.rectTransform.anchorMin = new Vector2(0f, 0.60f);
            tag.rectTransform.anchorMax = new Vector2(0.31f, 1f);

            _rankingEntryText = FullText("RankStatus", _signStrip, 30, TextAnchor.MiddleLeft, SignInk, 22f);
            _rankingEntryText.text = "ランキング登録待ち";
            _rankingEntryText.fontStyle = FontStyle.Bold;
            _rankingEntryText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _rankingEntryText.rectTransform.anchorMax = new Vector2(0.31f, 0.68f);

            _rankingGuideText = FullText("Guide", _signStrip, 21, TextAnchor.MiddleLeft, SignInk, 0f);
            _rankingGuideText.text = "名前を署名するとランキング登録が確定します";
            _rankingGuideText.fontStyle = FontStyle.Bold;
            _rankingGuideText.rectTransform.anchorMin = new Vector2(0.33f, 0.55f);
            _rankingGuideText.rectTransform.anchorMax = new Vector2(0.96f, 1f);

            // 白い記入枠を大きく取り、最下段でも入力場所を見失わないようにする
            var fieldGo = new GameObject("SignField", typeof(RectTransform), typeof(Image), typeof(InputField));
            fieldGo.transform.SetParent(_signStrip, false);
            RectTransform fieldRect = fieldGo.GetComponent<RectTransform>();
            fieldRect.anchorMin = new Vector2(0.33f, 0.08f);
            fieldRect.anchorMax = new Vector2(0.76f, 0.54f);
            fieldRect.offsetMin = new Vector2(0f, 0f);
            fieldRect.offsetMax = new Vector2(-10f, 0f);
            Image fieldBg = fieldGo.GetComponent<Image>();
            fieldBg.color = new Color(1f, 0.99f, 0.96f, 0.96f);
            UiKit.Panelize(fieldBg, 6);
            var fieldOutline = fieldGo.AddComponent<Outline>();
            fieldOutline.effectColor = new Color(SignInk.r, SignInk.g, SignInk.b, 0.32f);
            fieldOutline.effectDistance = new Vector2(1.5f, -1.5f);

            Text signText = CreateText("Text", fieldRect, 30, TextAnchor.MiddleLeft);
            signText.color = SignInk;
            signText.fontStyle = FontStyle.BoldAndItalic;
            signText.supportRichText = false;
            Stretch(signText.rectTransform);
            signText.rectTransform.offsetMin = new Vector2(14f, 0f);
            signText.rectTransform.offsetMax = new Vector2(-14f, 0f);

            Text placeholder = CreateText("Placeholder", fieldRect, 21, TextAnchor.MiddleLeft);
            placeholder.color = new Color(SignInk.r, SignInk.g, SignInk.b, 0.35f);
            placeholder.fontStyle = FontStyle.Italic;
            placeholder.text = "表示名を署名";
            Stretch(placeholder.rectTransform);
            placeholder.rectTransform.offsetMin = new Vector2(14f, 0f);
            placeholder.rectTransform.offsetMax = new Vector2(-14f, 0f);

            _signField = fieldGo.GetComponent<InputField>();
            _signField.targetGraphic = fieldBg;
            _signField.textComponent = signText;
            _signField.placeholder = placeholder;
            _signField.characterLimit = 10;
            _signField.lineType = InputField.LineType.SingleLine; // Enter で署名
            _signField.onEndEdit.AddListener(OnSignedPayslip);

            Text enter = FullText("Enter", _signStrip, 20, TextAnchor.MiddleCenter, new Color(0.82f, 0.16f, 0.12f), 0f);
            enter.text = "ENTER  登録確定";
            enter.fontStyle = FontStyle.Bold;
            enter.rectTransform.anchorMin = new Vector2(0.77f, 0.08f);
            enter.rectTransform.anchorMax = new Vector2(0.96f, 0.54f);

            // 認印（サイン受理でドンと出る）
            var sealGo = new GameObject("Seal", typeof(RectTransform));
            _signSeal = sealGo.transform;
            RectTransform seal = sealGo.GetComponent<RectTransform>();
            seal.SetParent(paper, false);
            seal.anchorMin = seal.anchorMax = new Vector2(1f, 1f);
            seal.pivot = new Vector2(0.5f, 0.5f);
            seal.anchoredPosition = new Vector2(-92f, -(yTop + 51f));
            seal.sizeDelta = new Vector2(72f, 72f);
            seal.localRotation = Quaternion.Euler(0f, 0f, -9f);
            Image ring = CreateImage("Ring", seal, new Color(0.82f, 0.16f, 0.12f, 0.12f));
            Stretch(ring.rectTransform);
            UiKit.Panelize(ring, 31);
            var ringOutline = ring.gameObject.AddComponent<Outline>();
            ringOutline.effectColor = new Color(0.82f, 0.16f, 0.12f, 0.85f);
            ringOutline.effectDistance = new Vector2(2f, -2f);
            Text sealText = FullText("Text", seal, 21, TextAnchor.MiddleCenter, new Color(0.82f, 0.16f, 0.12f, 0.9f), 0f);
            sealText.text = "登録";
            sealText.fontStyle = FontStyle.Bold;
            sealGo.SetActive(false);

            _signStrip.gameObject.SetActive(false); // ランキング入りしたときだけ出す
        }

        private void BuildStamp(RectTransform paper)
        {
            var go = new GameObject("Stamp", typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(paper, false);
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(-220f, -272f);
            r.sizeDelta = new Vector2(204f, 204f);
            r.localRotation = Quaternion.Euler(0f, 0f, -13f);

            _stampCircle = CreateImage("Ring", r, new Color(0.82f, 0.16f, 0.12f, 0.12f));
            Stretch(_stampCircle.rectTransform);
            UiKit.Panelize(_stampCircle, 96); // 正方形＋半径96＝円
            var ring = _stampCircle.gameObject.AddComponent<Outline>();
            ring.effectColor = new Color(0.82f, 0.16f, 0.12f, 0.85f);
            ring.effectDistance = new Vector2(3.5f, -3.5f);

            _rankBig = FullText("Rank", r, 90, TextAnchor.MiddleCenter, new Color(0.82f, 0.16f, 0.12f, 0.92f), 0f);
            _rankBig.fontStyle = FontStyle.Bold;
            _rankBig.rectTransform.offsetMin = new Vector2(0f, 16f);

            _rankLabel = FullText("RankLabel", r, 18, TextAnchor.MiddleCenter, new Color(0.82f, 0.16f, 0.12f, 0.92f), 0f);
            _rankLabel.fontStyle = FontStyle.Bold;
            _rankLabel.rectTransform.offsetMax = new Vector2(0f, -108f);
        }

        /// <summary>ラベル左／値右の1行（紙の左右端に固定するので、はみ出さない）。値テキストを返す。</summary>
        private Text PayRow(RectTransform paper, float yTop, string label, int labelSize, int valueSize, bool strong, Color valueColor)
        {
            var rowGo = new GameObject(label + "Row", typeof(RectTransform));
            var row = rowGo.GetComponent<RectTransform>();
            row.SetParent(paper, false);
            EdgeTop(row, yTop, valueSize + 22f, sidePad: Pad);

            Text l = FullText("L", row, labelSize, TextAnchor.MiddleLeft,
                strong ? SignInk : new Color(SignInk.r, SignInk.g, SignInk.b, 0.72f), 0f);
            l.text = label;
            l.fontStyle = strong ? FontStyle.Bold : FontStyle.Normal;

            Text v = FullText("V", row, valueSize, TextAnchor.MiddleRight, valueColor, 0f);
            v.fontStyle = strong ? FontStyle.Bold : FontStyle.Normal;
            return v;
        }

        private void Rule(RectTransform paper, float yTop, float h, Color color)
        {
            Image line = CreateImage("Rule", paper, color);
            EdgeTop(line.rectTransform, yTop, h, sidePad: Pad);
        }

        private Text SimpleLine(RectTransform paper, float yTop, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Line", typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(paper, false);
            EdgeTop(r, yTop, size + 12f, sidePad: Pad);
            Text t = FullText("T", r, size, anchor, color, 0f);
            return t;
        }

        private void SectionLabel(RectTransform paper, float yTop, string label)
        {
            Text t = SimpleLine(paper, yTop, 22, TextAnchor.MiddleLeft, AccentOrange);
            t.text = "■ " + label;
            t.fontStyle = FontStyle.Bold;
        }

        /// <summary>親の上端に固定し、指定の yTop（上端からの距離）と高さで幅いっぱいに広げる。</summary>
        private static void EdgeTop(RectTransform rect, float yTop, float height, float sidePad)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePad, -(yTop + height));
            rect.offsetMax = new Vector2(-sidePad, -yTop);
        }

        /// <summary>親いっぱいに広げたテキスト（左右 inset）。</summary>
        private static Text FullText(string goName, Transform parent, int size, TextAnchor anchor, Color color, float sideInset)
        {
            Text t = CreateText(goName, parent, size, anchor);
            t.color = color;
            RectTransform r = t.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(sideInset, 0f);
            r.offsetMax = new Vector2(-sideInset, 0f);
            return t;
        }

        public void ReturnToTitle()
        {
            PlaySceneFadeOut(() =>
            {
                Time.timeScale = 1f;
                RunStartContext.RequestTitleFadeInTransition();
                SceneManager.LoadSceneAsync(_titleSceneName);
            });
        }

        private void ShowRanking()
        {
            GameAudio.Instance.Play(GameAudio.Sfx.Ding, 0.92f);
            RankingView.Show(RankingStore.LastRecordedTicks); // 今回の記録を強調表示
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

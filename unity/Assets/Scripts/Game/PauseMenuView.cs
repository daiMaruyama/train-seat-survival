using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 一時停止メニュー（ESC）＝クリップボードに挟まった「小休止願」。
    /// 紙の申請書（明朝＋紙グレイン＋赤い「許可」印）の上に音量・感度の紙スライダー、
    /// 台座の下に導線ボタン（仕事に戻る／遊び方／はじめから／退職）。
    /// 開いている間は timeScale=0。リザルト・ランキング・カットイン・研修中は開かない。
    /// </summary>
    public sealed class PauseMenuView : MonoBehaviour
    {
        private static readonly Color BoardBg = new Color(0.05f, 0.06f, 0.10f, 0.97f);
        private static readonly Color Paper = new Color(0.95f, 0.93f, 0.87f);
        private static readonly Color Ink = new Color(0.15f, 0.16f, 0.19f);
        private static readonly Color InkDim = new Color(0.15f, 0.16f, 0.19f, 0.62f);
        private static readonly Color StampRed = new Color(0.82f, 0.16f, 0.12f);

        /// <summary>ポーズ中か（他システムの入力ガード用）。</summary>
        public static bool IsOpen { get; private set; }

        private RunController _run;
        private CutInView _cutIn;
        private FirstPersonController _fpc;
        private GameObject _panelRoot;
        private RectTransform _swayRoot; // クリップボードの吊り下げ支点（ポーズ中に揺らす）
        private bool _leaving; // 退職フェード中（多重発火と再オープンを防ぐ）

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
            AudioListener.pause = false; // 音の凍結も必ず解除（static なので放置すると次のシーンも無音になる）
        }

        private void Update()
        {
            // 開いている間はクリップボードが画鋲を支点にゆっくり揺れる（timeScale=0でも動く＝画面が死なない）
            if (IsOpen && _swayRoot != null)
            {
                _swayRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.unscaledTime * 0.9f) * 0.9f);
            }

            Keyboard kb = Keyboard.current;
            if (_leaving || kb == null || !kb.escapeKey.wasPressedThisFrame)
            {
                return; // 退職フェード中はESCを受け付けない（再開・再オープン事故を防ぐ）
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
            AudioListener.pause = true; // 小休止中は音も止まる（走行音・心音・SE全部）
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _panelRoot.SetActive(true);
        }

        private void Resume()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
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

        private void RestartRun()
        {
            IsOpen = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (_run != null)
            {
                _run.Restart();
            }
            else
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void GoTitle()
        {
            // 他の遷移（リトライ・日替わり）と同じ作法：黒フェードで落としてからタイトルへ。
            // フェード中は世界を止めたまま（音・timeScaleはロード直前に戻す）
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            IsOpen = false;
            if (_cutIn != null)
            {
                _cutIn.PlaySceneFadeOut(LoadTitleScene);
            }
            else
            {
                LoadTitleScene();
            }
        }

        private void LoadTitleScene()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
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

            Image dim = CreateImage("Dim", panelRootRect, new Color(0f, 0f, 0f, 0.5f)); // 背後の車内が見える濃さに留める
            Stretch(dim.rectTransform);
            dim.raycastTarget = true; // 背後のゲームUIを触らせない

            // 揺れの支点（クリップボード上端＝画鋲の位置）。ポーズ中はここを軸にゆらゆら揺れる
            var swayGo = new GameObject("BoardSway", typeof(RectTransform));
            _swayRoot = swayGo.GetComponent<RectTransform>();
            _swayRoot.SetParent(panelRootRect, false);
            _swayRoot.anchorMin = _swayRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _swayRoot.anchoredPosition = new Vector2(0f, 400f);
            _swayRoot.sizeDelta = Vector2.zero;

            // クリップボード（台座の板・ハード影）
            Image shadow = CreateImage("BoardShadow", _swayRoot, new Color(0f, 0f, 0f, 0.55f));
            Center(shadow.rectTransform, new Vector2(10f, -410f), new Vector2(620f, 800f));
            Image board = CreateImage("Board", _swayRoot, BoardBg);
            board.raycastTarget = true;
            RectTransform br = board.rectTransform;
            Center(br, new Vector2(0f, -400f), new Vector2(620f, 800f));
            UiKit.Panelize(board, 8, forceProcedural: true);
            var boardEdge = board.gameObject.AddComponent<Outline>();
            boardEdge.effectColor = new Color(1f, 1f, 1f, 0.08f);
            boardEdge.effectDistance = new Vector2(1.5f, -1.5f);

            // 紙「小休止願」
            Image paper = CreateImage("Paper", br, Paper);
            RectTransform pr = paper.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
            pr.pivot = new Vector2(0.5f, 1f);
            pr.anchoredPosition = new Vector2(0f, -46f);
            pr.sizeDelta = new Vector2(556f, 470f);
            UiKit.Panelize(paper, 4, forceProcedural: true);
            var paperShadow = paper.gameObject.AddComponent<Shadow>();
            paperShadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            paperShadow.effectDistance = new Vector2(4f, -4f);
            UiKit.AddPaperGrain(pr);

            // クリップボードの留め金具：山型の押さえ＋横長の台座＋紙を噛む爪の3ピースで
            // 「書類がクリップに挟まっている」と一目で分かる形にする（紙の上端に少し重ねる）
            Color metal = new Color(0.62f, 0.66f, 0.72f);
            Color metalDark = new Color(0.36f, 0.40f, 0.46f);

            // 爪（紙を上から噛んでいる部分。紙より手前＝後で描かれるよう紙の後に生成）
            Image clipTongue = CreateImage("ClipTongue", br, metalDark);
            clipTongue.rectTransform.anchorMin = clipTongue.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            clipTongue.rectTransform.pivot = new Vector2(0.5f, 1f);
            clipTongue.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            clipTongue.rectTransform.sizeDelta = new Vector2(96f, 22f);
            UiKit.Panelize(clipTongue, 6, forceProcedural: true);

            // 台座（横長の金属プレート）
            Image clipBase = CreateImage("ClipBase", br, metal);
            clipBase.rectTransform.anchorMin = clipBase.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            clipBase.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            clipBase.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            clipBase.rectTransform.sizeDelta = new Vector2(170f, 24f);
            UiKit.Panelize(clipBase, 9, forceProcedural: true);
            var baseEdge = clipBase.gameObject.AddComponent<Outline>();
            baseEdge.effectColor = new Color(0f, 0f, 0f, 0.35f);
            baseEdge.effectDistance = new Vector2(1.2f, -1.2f);

            // 山型の押さえ（クリップの記号。ドーム＋中央の穴）
            Image clipHump = CreateImage("ClipHump", br, metal);
            clipHump.rectTransform.anchorMin = clipHump.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            clipHump.rectTransform.pivot = new Vector2(0.5f, 0f);
            clipHump.rectTransform.anchoredPosition = new Vector2(0f, -34f);
            clipHump.rectTransform.sizeDelta = new Vector2(62f, 30f);
            UiKit.Panelize(clipHump, 15, forceProcedural: true);
            Image clipHole = CreateImage("ClipHole", clipHump.rectTransform, BoardBg);
            clipHole.rectTransform.anchorMin = clipHole.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            clipHole.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            clipHole.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            clipHole.rectTransform.sizeDelta = new Vector2(20f, 10f);
            UiKit.Panelize(clipHole, 5, forceProcedural: true);

            // 金属のツヤ（台座上辺のハイライト1本）
            Image clipShine = CreateImage("ClipShine", clipBase.rectTransform, new Color(1f, 1f, 1f, 0.35f));
            clipShine.rectTransform.anchorMin = new Vector2(0.06f, 1f);
            clipShine.rectTransform.anchorMax = new Vector2(0.94f, 1f);
            clipShine.rectTransform.pivot = new Vector2(0.5f, 1f);
            clipShine.rectTransform.anchoredPosition = new Vector2(0f, -3f);
            clipShine.rectTransform.sizeDelta = new Vector2(0f, 2.5f);

            // 見出し・宛名
            Text head = CreateText("Head", pr, 32, TextAnchor.MiddleCenter, Ink);
            head.font = UiFont.LoadMincho();
            head.text = "小 休 止 願";
            head.fontStyle = FontStyle.Bold;
            EdgeTop(head.rectTransform, 22f, 40f, 24f);

            Text sub = CreateText("Sub", pr, 17, TextAnchor.MiddleLeft, InkDim);
            sub.font = UiFont.LoadMincho();
            sub.text = "私は業務効率維持のため、下記の通り小休止いたします。";
            EdgeTop(sub.rectTransform, 74f, 26f, 30f);

            Rule(pr, 110f, 2.5f, new Color(0.95f, 0.45f, 0.15f));

            // 記入欄（紙スライダー）
            GameAudio audio = GameAudio.Instance;
            UiKit.MakePaperSlider(pr, new Vector2(30f, -128f), 496f, "全体", audio.MasterVolume, 0f, 1f, v => audio.MasterVolume = v);
            UiKit.MakePaperSlider(pr, new Vector2(30f, -172f), 496f, "音楽", audio.BgmVolume, 0f, 1f, v => audio.BgmVolume = v);
            UiKit.MakePaperSlider(pr, new Vector2(30f, -216f), 496f, "効果音", audio.SeVolume, 0f, 1f, v => audio.SeVolume = v);
            float sens = _fpc != null ? _fpc.LookSensitivity : 0.08f;
            UiKit.MakePaperSlider(pr, new Vector2(30f, -260f), 496f, "感　度", sens, 0.02f, 0.24f, v => { if (_fpc != null) _fpc.LookSensitivity = v; });
            PaperLabel(pr, 128f, "全体");
            PaperLabel(pr, 172f, "音楽");
            PaperLabel(pr, 216f, "効果音");
            PaperLabel(pr, 260f, "感　度");

            Rule(pr, 312f, 2f, new Color(Ink.r, Ink.g, Ink.b, 0.25f));

            // 但し書き＋「許可」印（承認済みの小休止＝安心してサボれ）
            Text note = CreateText("Note", pr, 16, TextAnchor.UpperLeft, InkDim);
            note.font = UiFont.LoadMincho();
            note.text = "※上司に発見された場合、当社は一切の責任を負いません。\n※小休止中も体力は回復しません。休むなら座席で。";
            EdgeTop(note.rectTransform, 328f, 60f, 30f);

            var stampGo = new GameObject("Stamp", typeof(RectTransform));
            RectTransform stamp = stampGo.GetComponent<RectTransform>();
            stamp.SetParent(pr, false);
            stamp.anchorMin = stamp.anchorMax = new Vector2(1f, 1f);
            stamp.pivot = new Vector2(0.5f, 0.5f);
            stamp.anchoredPosition = new Vector2(-70f, -400f);
            stamp.sizeDelta = new Vector2(84f, 84f);
            stamp.localRotation = Quaternion.Euler(0f, 0f, -11f);
            Image ring = CreateImage("Ring", stamp, new Color(StampRed.r, StampRed.g, StampRed.b, 0.1f));
            Stretch(ring.rectTransform);
            UiKit.Panelize(ring, 42, forceProcedural: true);
            var ringOutline = ring.gameObject.AddComponent<Outline>();
            ringOutline.effectColor = new Color(StampRed.r, StampRed.g, StampRed.b, 0.85f);
            ringOutline.effectDistance = new Vector2(2f, -2f);
            Text sealText = CreateText("Text", stamp, 26, TextAnchor.MiddleCenter, new Color(StampRed.r, StampRed.g, StampRed.b, 0.9f));
            sealText.text = "許可";
            sealText.fontStyle = FontStyle.Bold;
            Stretch(sealText.rectTransform);

            // 導線（台座の下段・共通ボタン 2×2）
            UiKit.MakeButton(br, new Vector2(-140f, -190f), new Vector2(256f, 74f), "仕事に戻る", 24, Resume);
            UiKit.MakeButton(br, new Vector2(140f, -190f), new Vector2(256f, 74f), "遊び方", 24, ShowHelp);
            UiKit.MakeButton(br, new Vector2(-140f, -282f), new Vector2(256f, 74f), "はじめから", 24, RestartRun);
            UiKit.MakeButton(br, new Vector2(140f, -282f), new Vector2(256f, 74f), "退職する", 24, GoTitle);

            Text hint = CreateText("Hint", br, 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.5f));
            hint.text = "［ESC］で仕事に戻る";
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            hint.rectTransform.pivot = new Vector2(0.5f, 0f);
            hint.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            hint.rectTransform.sizeDelta = new Vector2(400f, 26f);

            _panelRoot.SetActive(false);
        }

        // ---- 小道具 --------------------------------------------------------

        private static void Rule(RectTransform paper, float yTop, float h, Color color)
        {
            Image line = CreateImage("Rule", paper, color);
            EdgeTop(line.rectTransform, yTop, h, 30f);
        }

        private static void PaperLabel(RectTransform paper, float yTop, string label)
        {
            Text text = CreateText(label + "VisibleLabel", paper, 19, TextAnchor.MiddleLeft, Ink);
            text.text = label;
            text.fontStyle = FontStyle.Bold;
            EdgeTop(text.rectTransform, yTop, 28f, 30f);
        }

        private static void EdgeTop(RectTransform rect, float yTop, float height, float sidePad)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePad, -(yTop + height));
            rect.offsetMax = new Vector2(-sidePad, -yTop);
        }

        private static void Center(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
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

        private static Text CreateText(string goName, Transform parent, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(goName, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UiFont.Load();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

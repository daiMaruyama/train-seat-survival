using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 遊び方＝「新入社員研修のしおり」（紙の世界観）。初回出勤時に自動で開き、以降は
    /// ポーズメニューの「遊び方を見る」から再読できる。表示中は timeScale=0、
    /// ［Enter］か「出勤する」ボタンで研修終了→仕事開始。
    /// </summary>
    public sealed class TutorialView : MonoBehaviour
    {
        private static readonly Color Paper = new Color(0.94f, 0.93f, 0.89f);
        private static readonly Color Ink = new Color(0.13f, 0.14f, 0.17f);
        private static readonly Color InkDim = new Color(0.13f, 0.14f, 0.17f, 0.6f);
        private static readonly Color HeaderNavy = new Color(0.13f, 0.15f, 0.21f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color StampRed = new Color(0.82f, 0.16f, 0.12f);

        private const string SeenKey = "TutorialSeen";
        private const float PaperW = 940f;
        private const float PaperH = 860f;

        private static TutorialView _open;

        /// <summary>表示中か（ポーズ等の入力ガード用）。</summary>
        public static bool IsOpen => _open != null;

        /// <summary>初回だけ自動で開く（既読なら何もしない）。</summary>
        public static void ShowFirstTime()
        {
            if (PlayerPrefs.GetInt(SeenKey, 0) == 0)
            {
                Show();
            }
        }

        /// <summary>しおりを開く（timeScale=0で世界を止める）。</summary>
        public static void Show()
        {
            if (_open != null)
            {
                return;
            }
            var go = new GameObject("TutorialView");
            _open = go.AddComponent<TutorialView>();
            _open.Build();
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                Dismiss();
            }
        }

        private void Dismiss()
        {
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _open = null;
            Destroy(gameObject);
        }

        private void Build()
        {
            var canvasGo = new GameObject("TutorialCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 36;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            Image dim = CreateImage("Dim", root, new Color(0f, 0f, 0f, 0.7f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            // 研修のしおり（紙）
            Image paper = CreateImage("Paper", root, Paper);
            paper.raycastTarget = true;
            RectTransform pr = paper.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f);
            pr.anchoredPosition = new Vector2(0f, 20f);
            pr.sizeDelta = new Vector2(PaperW, PaperH);
            UiKit.Panelize(paper, 10);
            UiKit.AddShadow(paper, 10, blur: 36, alpha: 0.55f, offset: new Vector2(0f, -12f));
            UiKit.AddPaperGrain(pr); // 紙の繊維感

            // ヘッダ帯
            Image header = CreateImage("Header", pr, HeaderNavy);
            EdgeTop(header.rectTransform, 8f, 88f, 8f);
            UiKit.Panelize(header, 8);
            Text title = CreateText("Title", header.rectTransform, 36, TextAnchor.MiddleLeft, new Color(0.96f, 0.94f, 0.89f));
            title.text = "新入社員研修のしおり";
            title.fontStyle = FontStyle.Bold;
            StretchInset(title.rectTransform, 34f);
            Text company = CreateText("Company", header.rectTransform, 20, TextAnchor.MiddleRight, new Color(0.96f, 0.94f, 0.89f, 0.72f));
            company.text = "株式会社 ホワイト・ハッピー";
            StretchInset(company.rectTransform, 34f);

            // 心得（本文）
            Line(pr, 118f, "一、", "満員電車で座席を確保することが、あなたの唯一の業務である。");
            Line(pr, 170f, "二、", "移動は W A S D、周囲の確認はマウス。");
            Line(pr, 222f, "三、", "空席に照準を合わせ、E で着席せよ。早い者勝ちである。");
            Line(pr, 274f, "四、", "座れた日は無事出勤＝体力が回復し、翌日に進む。");
            Line(pr, 326f, "五、", "立ちっぱなしは過労で倒れる。倒れたら年収査定のうえ処分する。");
            Line(pr, 378f, "六、", "降りる客の近くに立て。席は空いた瞬間に奪われる。");
            Line(pr, 430f, "七、", "コーヒー（橙の光）は回復。データメガネ（青の光）は降車予測。");
            Line(pr, 482f, "八、", "日を追うごとに消耗は激しくなる。長くは続かない。");

            Rule(pr, 546f, 3f, AccentOrange);

            // 総務からの一言
            Text foot = CreateText("Foot", pr, 24, TextAnchor.MiddleLeft, InkDim);
            foot.text = "以上を熟読し、本日より出勤すること。健闘を祈らない。 —— 総務部";
            EdgeTop(foot.rectTransform, 566f, 40f, 52f);

            // 赤い「回覧済」印（読んだ体にする）
            var sealGo = new GameObject("Seal", typeof(RectTransform));
            RectTransform seal = sealGo.GetComponent<RectTransform>();
            seal.SetParent(pr, false);
            seal.anchorMin = seal.anchorMax = new Vector2(1f, 1f);
            seal.pivot = new Vector2(0.5f, 0.5f);
            seal.anchoredPosition = new Vector2(-110f, -160f);
            seal.sizeDelta = new Vector2(110f, 110f);
            seal.localRotation = Quaternion.Euler(0f, 0f, 12f);
            Image ring = CreateImage("Ring", seal, new Color(StampRed.r, StampRed.g, StampRed.b, 0.1f));
            Stretch(ring.rectTransform);
            UiKit.Panelize(ring, 54);
            var ringOutline = ring.gameObject.AddComponent<Outline>();
            ringOutline.effectColor = new Color(StampRed.r, StampRed.g, StampRed.b, 0.8f);
            ringOutline.effectDistance = new Vector2(2.5f, -2.5f);
            Text sealText = CreateText("Text", seal, 26, TextAnchor.MiddleCenter, new Color(StampRed.r, StampRed.g, StampRed.b, 0.9f));
            sealText.text = "回覧済";
            sealText.fontStyle = FontStyle.Bold;
            Stretch(sealText.rectTransform);

            // 出勤ボタン＋Enterヒント（紙の中に収める）
            UiKit.MakeButton(pr, new Vector2(0f, -(PaperH * 0.5f) + 90f), new Vector2(340f, 78f), "出勤する", 28, Dismiss);
            Text enter = CreateText("Enter", pr, 20, TextAnchor.MiddleCenter, new Color(StampRed.r, StampRed.g, StampRed.b, 0.9f));
            enter.text = "［Enter］で出勤";
            enter.fontStyle = FontStyle.Bold;
            EdgeTop(enter.rectTransform, PaperH - 44f, 28f, 52f);
        }

        /// <summary>「一、〜〜」形式の心得1行。</summary>
        private void Line(RectTransform paper, float yTop, string num, string body)
        {
            Text n = CreateText("Num", paper, 26, TextAnchor.MiddleLeft, AccentOrange);
            n.text = num;
            n.fontStyle = FontStyle.Bold;
            EdgeTop(n.rectTransform, yTop, 40f, 52f);

            Text t = CreateText("Body", paper, 25, TextAnchor.MiddleLeft, Ink);
            t.text = body;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(112f, -(yTop + 40f));
            rt.offsetMax = new Vector2(-52f, -yTop);
        }

        private static void Rule(RectTransform paper, float yTop, float h, Color color)
        {
            Image line = CreateImage("Rule", paper, color);
            EdgeTop(line.rectTransform, yTop, h, 52f);
        }

        private static void EdgeTop(RectTransform rect, float yTop, float height, float sidePad)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePad, -(yTop + height));
            rect.offsetMax = new Vector2(-sidePad, -yTop);
        }

        private static void StretchInset(RectTransform rect, float side)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(side, 0f);
            rect.offsetMax = new Vector2(-side, 0f);
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
            text.font = UiFont.LoadMincho(); // 会社の書類なので明朝
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

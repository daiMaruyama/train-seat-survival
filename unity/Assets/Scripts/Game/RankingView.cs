using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 社畜ランキング＝駅の発車標（LED電光掲示板）。黒い盤面にアンバーのLED文字で
    /// 「出世街道・上り」の序列を映す。電車の世界観と社畜世界観を一枚で束ね、
    /// 給与明細（紙）とは意匠をはっきり分ける。署名の儀式は明細側（CutInView）が担当し、
    /// ここは表示専用。閉じる＝盤面内のボタン／パネル外クリック／ESC。
    /// </summary>
    public sealed class RankingView : MonoBehaviour
    {
        // 深いネイビー＋生成りを本文色にする。色の意味は「金＝頂点」「橙＝今回の新記録」に限定し、
        // 上位の強調は色相ではなく輝度（金→生成り太字→青灰）で作る＝ゲーム全体のオレンジと衝突しない。
        private static readonly Color BoardBg = new Color(0.045f, 0.064f, 0.095f, 1f);
        private static readonly Color FrameGray = new Color(0.76f, 0.74f, 0.69f);
        private static readonly Color FrameEdge = new Color(0.035f, 0.048f, 0.07f, 0.78f);
        private static readonly Color LedAmber = new Color(1f, 0.48f, 0.20f);  // 「NEW（今回の記録）」専用
        private static readonly Color LedGold = new Color(1f, 0.93f, 0.55f);   // 1位＝シャンパンゴールド
        private static readonly Color LedGreen = new Color(0.95f, 0.94f, 0.89f);
        private static readonly Color LedDim = new Color(0.62f, 0.72f, 0.81f, 1f);
        private static readonly Color LedRed = new Color(1f, 0.66f, 0.40f);
        private static readonly Color RowLine = new Color(0.50f, 0.60f, 0.70f, 0.16f);

        private const float BoardW = 1600f;
        private const float BoardH = 950f;

        private static RankingView _open;

        private const string DefaultNotice = "本日も定時運行の予定はありません。ご了承ください。";

        private long _highlightTicks;
        private Image _highlightStripe;

        // 社内（ローカル）⇄ 全国（UGS）の切替。全国は取得に失敗したら社内表示のまま
        private List<RankingEntry> _localEntries;
        private RectTransform _rowsRoot;
        private Text _statusChipText;
        private Image _statusChip;
        private Text _noticeText;
        private Text _toggleLabel;
        private bool _globalMode;
        private bool _fetching;

        /// <summary>開いているか（背後のキー操作を止める判定用）。</summary>
        public static bool IsOpen => _open != null;

        /// <summary>ランキングを開く（既に開いていれば何もしない）。highlightTicks の行が点滅する。</summary>
        public static void Show(long highlightTicks = 0)
        {
            if (_open != null)
            {
                return;
            }
            var go = new GameObject("RankingView");
            _open = go.AddComponent<RankingView>();
            _open._highlightTicks = highlightTicks;
            _open.Build();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Close();
            }

            // 新着行のLEDがチカチカ瞬く
            if (_highlightStripe != null)
            {
                Color c = _highlightStripe.color;
                c.a = 0.165f + 0.035f * Mathf.Sin(Time.unscaledTime * 5f);
                _highlightStripe.color = c;
            }
        }

        private void Close()
        {
            _open = null;
            Destroy(gameObject);
        }

        private void Build()
        {
            var canvasGo = new GameObject("RankingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40; // カットインより前面
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            // 背面の暗幕。パネルの外をクリックしたら閉じる
            Image dim = CreateImage("Dim", root, new Color(0.008f, 0.014f, 0.025f, 0.90f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var dimTrigger = dim.gameObject.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(_ => Close());
            dimTrigger.triggers.Add(click);

            // 金属フレーム＋黒い盤面（発車標）
            Image frame = CreateImage("Frame", root, FrameGray);
            Center(frame.rectTransform, Vector2.zero, new Vector2(BoardW + 16f, BoardH + 16f));
            frame.raycastTarget = true; // 盤面上のクリックは閉じない
            UiKit.Panelize(frame, 18);
            UiKit.AddShadow(frame, 18, 48, 0.78f, new Vector2(0f, -16f));
            var frameEdge = frame.gameObject.AddComponent<Outline>();
            frameEdge.effectColor = FrameEdge;
            frameEdge.effectDistance = new Vector2(2f, -2f);

            Image board = CreateImage("Board", frame.rectTransform, BoardBg);
            Stretch(board.rectTransform);
            board.rectTransform.offsetMin = new Vector2(10f, 10f);
            board.rectTransform.offsetMax = new Vector2(-10f, -10f);
            UiKit.Panelize(board, 13);
            RectTransform br = board.rectTransform;

            List<RankingEntry> entries = RankingStore.Load();
            _localEntries = entries;
            int highlightIndex = _highlightTicks != 0 ? entries.FindIndex(e => e.ticks == _highlightTicks) : -1;
            bool showingNewRecord = highlightIndex >= 0;

            // ヘッダ帯：路線名ふうタイトル
            Image headBar = CreateImage("HeadBar", br, new Color(0.12f, 0.17f, 0.24f, 0.88f));
            EdgeTop(headBar.rectTransform, 0f, 112f, 0f);
            Text title = FullText("Title", headBar.rectTransform, 48, TextAnchor.MiddleLeft, LedGreen, 54f);
            title.text = "社畜ランキング";
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            title.rectTransform.anchorMax = new Vector2(0.56f, 1f);

            Text subTitle = FullText("SubTitle", headBar.rectTransform, 16, TextAnchor.MiddleLeft, LedDim, 56f);
            subTitle.text = "COMMUTER PERFORMANCE / LOCAL TOP 10";
            subTitle.fontStyle = FontStyle.Bold;
            subTitle.rectTransform.anchorMin = new Vector2(0f, 0f);
            subTitle.rectTransform.anchorMax = new Vector2(0.58f, 0.34f);

            _statusChip = CreateImage("Status", headBar.rectTransform,
                showingNewRecord
                    ? new Color(LedAmber.r, LedAmber.g, LedAmber.b, 0.18f)
                    : new Color(LedDim.r, LedDim.g, LedDim.b, 0.12f));
            UiKit.Panelize(_statusChip, 8);
            RectTransform statusRect = _statusChip.rectTransform;
            statusRect.anchorMin = new Vector2(0.66f, 0.18f);
            statusRect.anchorMax = new Vector2(0.965f, 0.82f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;
            _statusChipText = FullText("Route", statusRect, 21, TextAnchor.MiddleCenter,
                showingNewRecord ? LedAmber : LedGreen, 22f);
            _statusChipText.text = showingNewRecord
                ? $"● RANKING UPDATED　#{highlightIndex + 1:00}"
                : $"● LOCAL ARCHIVE　{entries.Count:00} / 10";
            _statusChipText.fontStyle = FontStyle.Bold;

            Image headRule = CreateImage("HeadRule", br, new Color(LedDim.r, LedDim.g, LedDim.b, 0.55f));
            EdgeTop(headRule.rectTransform, 112f, 4f, 32f);

            // 列見出し（LEDの案内行）
            RectTransform head = Row(br, 132f, 40f);
            AddCell(head, "順位", 0.00f, 0.09f, 21, LedDim, TextAnchor.MiddleCenter, bold: true);
            AddCell(head, "肩書", 0.10f, 0.26f, 21, LedDim, TextAnchor.MiddleLeft, bold: true);
            AddCell(head, "氏名", 0.27f, 0.51f, 21, LedDim, TextAnchor.MiddleLeft, bold: true);
            AddCell(head, "勤続", 0.55f, 0.67f, 21, LedDim, TextAnchor.MiddleRight, bold: true);
            AddCell(head, "推定年収", 0.69f, 0.98f, 21, LedDim, TextAnchor.MiddleRight, bold: true);

            // 序列（10行）は作り直せるコンテナに入れる（社内⇄全国の切替で丸ごと再構築する）
            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            _rowsRoot = rowsGo.GetComponent<RectTransform>();
            _rowsRoot.SetParent(br, false);
            Stretch(_rowsRoot);
            BuildRows(entries, _highlightTicks);

            // 下段の案内テロップ＋盤面内のボタン
            const float rowH = 59f;
            Image footRule = CreateImage("FootRule", br, new Color(LedDim.r, LedDim.g, LedDim.b, 0.4f));
            EdgeTop(footRule.rectTransform, 180f + 10 * rowH + 8f, 2f, 32f);
            _noticeText = FullText("Notice", br, 21, TextAnchor.MiddleLeft, LedRed, 60f);
            _noticeText.text = DefaultNotice;
            EdgeTopRect(_noticeText.rectTransform, 180f + 10 * rowH + 22f, 32f, 60f);

            Text closeHint = FullText("CloseHint", br, 18, TextAnchor.MiddleRight, LedDim, 60f);
            closeHint.text = "ESC / パネル外クリックで閉じる";
            EdgeTopRect(closeHint.rectTransform, 180f + 10 * rowH + 22f, 32f, 60f);

            // LED盤のドット格子（文字の上・ボタンの下に重ねる＝電光掲示板の質感）
            UiKit.AddLedGrid(br, 0.28f);

            UiKit.MakeButton(br, new Vector2(-170f, -(BoardH * 0.5f) + 56f), new Vector2(300f, 70f), "閉じる", 24, Close);
            Button toggle = UiKit.MakeButton(br, new Vector2(170f, -(BoardH * 0.5f) + 56f), new Vector2(300f, 70f), "全国版へ", 24, ToggleGlobal);
            _toggleLabel = toggle.GetComponentInChildren<Text>();
        }

        /// <summary>序列10行を（再）構築する。社内⇄全国どちらも同じ見た目・同じ型で描く。</summary>
        private void BuildRows(List<RankingEntry> entries, long highlightTicks)
        {
            for (int i = _rowsRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_rowsRoot.GetChild(i).gameObject);
            }
            _highlightStripe = null;

            const float rowH = 59f;
            for (int i = 0; i < 10; i++)
            {
                float y = 180f + i * rowH;
                RectTransform row = Row(_rowsRoot, y, rowH - 6f);
                bool has = i < entries.Count;
                bool highlight = has && highlightTicks != 0 && entries[i].ticks == highlightTicks;
                bool top3 = i < 3;

                // 1位は金の面、2〜3位はやや明るい青灰、以降は交互の暗い縞＝輝度の階段で序列を見せる
                Color rowColor = i == 0
                    ? new Color(LedGold.r, LedGold.g, LedGold.b, 0.12f)
                    : top3
                        ? new Color(0.38f, 0.52f, 0.66f, 0.11f)
                        : i % 2 == 0
                            ? new Color(0.52f, 0.62f, 0.72f, 0.075f)
                            : new Color(0.52f, 0.62f, 0.72f, 0.035f);
                Image rowBg = CreateImage("RowBg", row, rowColor);
                Stretch(rowBg.rectTransform);
                UiKit.Panelize(rowBg, 7);
                rowBg.transform.SetAsFirstSibling();

                Image line = CreateImage("RowLine", row, RowLine);
                line.rectTransform.anchorMin = new Vector2(0f, 0f);
                line.rectTransform.anchorMax = new Vector2(1f, 0f);
                line.rectTransform.pivot = new Vector2(0.5f, 0f);
                line.rectTransform.sizeDelta = new Vector2(0f, 1.5f);
                line.rectTransform.anchoredPosition = Vector2.zero;

                if (highlight)
                {
                    _highlightStripe = CreateImage("Hl", row, new Color(LedAmber.r, LedAmber.g, LedAmber.b, 0.14f));
                    Stretch(_highlightStripe.rectTransform);
                    _highlightStripe.transform.SetSiblingIndex(1);
                }

                // 文字色：新記録＝橙（唯一のオレンジ）、1位＝金、それ以外は生成り（上位は太字で持ち上げる）
                Color led = highlight ? LedAmber : i == 0 ? LedGold : LedGreen;
                int size = i == 0 ? 32 : 24; // 8の倍数＝ドットが割れない

                // TOP3／今回記録は左端の信号色でも判別できる（金＝上位、橙＝NEW）
                if (top3 || highlight)
                {
                    Image marker = CreateImage("Marker", row, highlight ? LedAmber : new Color(LedGold.r, LedGold.g, LedGold.b, 0.6f));
                    marker.rectTransform.anchorMin = new Vector2(0f, 0.18f);
                    marker.rectTransform.anchorMax = new Vector2(0f, 0.82f);
                    marker.rectTransform.pivot = new Vector2(0f, 0.5f);
                    marker.rectTransform.sizeDelta = new Vector2(highlight ? 8f : 4f, 0f);
                    marker.rectTransform.anchoredPosition = new Vector2(-14f, 0f);
                }

                AddCell(row, (i + 1).ToString("00"), 0.00f, 0.09f, size, led, TextAnchor.MiddleCenter, bold: top3);
                AddCell(row, RankTitle(i), 0.10f, 0.26f, 16, i == 0 ? LedGold : top3 ? LedGreen : LedDim, TextAnchor.MiddleLeft, bold: top3);
                if (has)
                {
                    RankingEntry e = entries[i];
                    AddCell(row, e.name, 0.27f, 0.51f, size, led, TextAnchor.MiddleLeft, bold: highlight || top3);
                    if (highlight)
                    {
                        AddCell(row, "NEW", 0.505f, 0.55f, 14, LedAmber, TextAnchor.MiddleCenter, bold: true);
                    }
                    AddCell(row, $"{e.days}日", 0.55f, 0.67f, size, led, TextAnchor.MiddleRight);
                    AddCell(row, $"¥{e.Score:#,0}", 0.69f, 0.98f, size, led, TextAnchor.MiddleRight, bold: true);
                }
                else
                {
                    AddCell(row, "（回送）", 0.27f, 0.51f, size, new Color(1f, 1f, 1f, 0.18f), TextAnchor.MiddleLeft);
                }
            }
        }

        /// <summary>社内⇄全国の切替。全国は非同期取得し、失敗したら社内表示のまま案内だけ出す。</summary>
        private void ToggleGlobal()
        {
            if (_fetching)
            {
                return;
            }

            if (_globalMode)
            {
                // 社内へ戻す（ローカルは手元にあるので即時）
                _globalMode = false;
                BuildRows(_localEntries, _highlightTicks);
                _statusChipText.text = $"● LOCAL ARCHIVE　{_localEntries.Count:00} / 10";
                _statusChipText.color = LedGreen;
                _noticeText.text = DefaultNotice;
                _toggleLabel.text = "全国版へ";
                return;
            }

            StartCoroutine(FetchGlobalRoutine());
        }

        private System.Collections.IEnumerator FetchGlobalRoutine()
        {
            _fetching = true;
            _noticeText.text = "全国ネットワークに接続しています…";
            var task = UgsRanking.FetchTopAsync(10);
            while (!task.IsCompleted)
            {
                yield return null;
            }
            _fetching = false;

            List<RankingEntry> global = task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? task.Result : null;
            if (global == null)
            {
                // 圏外：社内表示のまま、発車標らしい言い回しで案内
                _noticeText.text = "ただいま全国ネットワークは圏外です。社内記録を表示しています。";
                yield break;
            }

            _globalMode = true;
            BuildRows(global, 0);
            _statusChipText.text = $"● NATIONAL NETWORK　{global.Count:00} / 10";
            _statusChipText.color = LedGreen;
            _noticeText.text = "全国の社畜と比較しています。上には上がいるものです。";
            _toggleLabel.text = "社内版へ";
        }

        private static string RankTitle(int index)
        {
            if (index == 0) return "取締役";
            if (index == 1) return "部長";
            if (index == 2) return "課長";
            if (index <= 4) return "係長";
            if (index <= 6) return "主任";
            return "平社員";
        }

        // ---- レイアウト小道具（盤面の上端基準） ------------------------------

        private static RectTransform Row(RectTransform parent, float yTop, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(60f, -(yTop + height));
            rt.offsetMax = new Vector2(-60f, -yTop);
            return rt;
        }

        private static Text AddCell(RectTransform row, string value, float xMin, float xMax, int size, Color color, TextAnchor anchor, bool bold = false)
        {
            Text t = CreateText("Cell", row, size, anchor, color);
            t.text = value;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(xMin, 0f);
            rt.anchorMax = new Vector2(xMax, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        private static void EdgeTop(RectTransform rect, float yTop, float height, float sidePad)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(sidePad, -(yTop + height));
            rect.offsetMax = new Vector2(-sidePad, -yTop);
        }

        private static void EdgeTopRect(RectTransform rect, float yTop, float height, float sidePad)
        {
            EdgeTop(rect, yTop, height, sidePad);
        }

        private static Text FullText(string goName, Transform parent, int size, TextAnchor anchor, Color color, float sideInset)
        {
            Text t = CreateText(goName, parent, size, anchor, color);
            RectTransform r = t.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(sideInset, 0f);
            r.offsetMax = new Vector2(-sideInset, 0f);
            return t;
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
            text.font = UiFont.LoadLed(); // 発車標なのでドットフォント（8の倍数サイズ推奨）
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

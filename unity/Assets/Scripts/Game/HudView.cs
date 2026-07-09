using UnityEngine;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// HUD の描画"だけ"を担う（ルールは持たない）。左上は「日付・行程・体力」をまとめたカード。
    /// 体力ゲージには手触りを付けてある：
    /// ・表示値は実値へ滑らかに追従（回復がスッと伸びて見える）
    /// ・減少時は「追いバー」が遅れて減る（どれだけ削られたかが残像で見える）
    /// ・大きな回復（コーヒー等）でバーが白くフラッシュ＋カードがプルンと弾む
    /// ・残量25%を切るとバーが脈打つ（ピンチの緊張感）
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private static readonly Color CardBg = new Color(0.05f, 0.06f, 0.10f, 0.62f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color BarBack = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BarHigh = new Color(0.30f, 0.80f, 0.40f);
        private static readonly Color BarMid = new Color(0.95f, 0.75f, 0.20f);
        private static readonly Color BarLow = new Color(0.90f, 0.30f, 0.30f);
        private static readonly Color TrailColor = new Color(0.95f, 0.55f, 0.35f, 0.75f);
        private static readonly Color DimText = new Color(1f, 1f, 1f, 0.7f);

        private const float BarWidth = 380f;
        private const float BarHeight = 30f;

        private StaminaSystem _stamina;
        private CommuteDirector _director;
        private PlayerSit _player;

        private RectTransform _card;
        private RectTransform _fill;
        private RectTransform _trail;
        private Image _fillImage;
        private Image _flashImage;
        private Text _dayText;
        private Text _routeText;
        private Text _statusText;
        private Text _staminaLabel;
        private Text _prompt;
        private GameObject _hudCanvas;

        // ゲージ手触り用の内部状態
        private float _shown;      // 表示中の正規化値（実値へ追従）
        private float _trailValue; // 追いバー（減少の残像）
        private float _healFlash;  // 回復フラッシュの残り(1→0)
        private float _punch;      // カードの弾み残り(1→0)
        private bool _initialized;

        private void Start()
        {
            _stamina = FindFirstObjectByType<StaminaSystem>();
            _director = FindFirstObjectByType<CommuteDirector>();
            _player = FindFirstObjectByType<PlayerSit>();
            Build();
        }

        /// <summary>HUDの表示だけを切る（GameObject は消さない＝同居する CutInView を巻き添えにしない）。</summary>
        public void SetHudVisible(bool visible)
        {
            if (_hudCanvas != null)
            {
                _hudCanvas.SetActive(visible);
            }
            enabled = visible; // 非表示中は毎フレーム更新も止める
        }

        private void Update()
        {
            if (_stamina != null)
            {
                UpdateGauge();
            }

            if (_director != null)
            {
                _dayText.text = $"{_director.Leg + 1}日目";
                _routeText.text = $"駅 {_director.CurrentStation}/{_director.StationCount - 1}　生存 {_director.TotalStationsSurvived}駅";
                _statusText.text = _director.IsEndOfLine ? "終点"
                                 : _director.IsAtStation ? "🚉 停車中"
                                 : $"次の駅まで {_director.SecondsToNextStation:0}s";
            }

            if (_player != null)
            {
                _prompt.text = _player.IsSeated ? "次の日まで休憩"
                             : _player.CanSitNow ? "E    座る"
                             : string.Empty;
            }
        }

        /// <summary>ゲージの追従・追いバー・フラッシュ・パンチ・低体力パルスをまとめて駆動する。</summary>
        private void UpdateGauge()
        {
            float actual = _stamina.Normalized;
            if (!_initialized)
            {
                _shown = _trailValue = actual;
                _initialized = true;
            }

            // 大きな回復（コーヒー・日替わり報酬）を検知して演出を発火。座り回復(毎フレーム微増)では鳴らない
            if (actual - _shown > 0.04f)
            {
                _healFlash = 1f;
                _punch = 1f;
            }

            // 表示値は実値へ追従（回復はスッと伸び、消耗は即時に近い）
            _shown = Mathf.MoveTowards(_shown, actual, Time.deltaTime * 0.9f);

            // 追いバー：減少だけ遅れて追いかける（削られた量の残像）
            if (_trailValue < _shown)
            {
                _trailValue = _shown;
            }
            else
            {
                _trailValue = Mathf.MoveTowards(_trailValue, _shown, Time.deltaTime * 0.22f);
            }

            _fill.sizeDelta = new Vector2((BarWidth - 4f) * _shown, BarHeight - 4f);
            _trail.sizeDelta = new Vector2((BarWidth - 4f) * _trailValue, BarHeight - 4f);

            Color barColor = _shown > 0.5f ? BarHigh : _shown > 0.25f ? BarMid : BarLow;
            if (_shown <= 0.25f)
            {
                barColor.a = 0.7f + 0.3f * Mathf.Sin(Time.time * 8f); // ピンチの脈動
            }
            _fillImage.color = barColor;

            // 回復フラッシュ（白がスッと消える）
            _healFlash = Mathf.MoveTowards(_healFlash, 0f, Time.deltaTime * 2.6f);
            _flashImage.color = new Color(1f, 1f, 1f, _healFlash * 0.85f);
            _flashImage.rectTransform.sizeDelta = _fill.sizeDelta;

            // カードのプルンと弾む動き
            _punch = Mathf.MoveTowards(_punch, 0f, Time.deltaTime * 3.2f);
            float scale = 1f + 0.10f * Mathf.Sin(_punch * Mathf.PI);
            _card.localScale = Vector3.one * scale;

            _staminaLabel.text = Mathf.CeilToInt(_stamina.Current).ToString();
        }

        private void Build()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _hudCanvas = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            // ---- 左上カード：日付・行程・体力をひとまとめ ----
            Image cardBg = CreateImage("Card", root, CardBg);
            _card = cardBg.rectTransform;
            Anchor(_card, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(BarWidth + 52f, 128f));
            _card.pivot = new Vector2(0f, 1f);
            UiKit.Panelize(cardBg, 18);
            UiKit.AddShadow(cardBg, 18, blur: 30, alpha: 0.5f, offset: new Vector2(0f, -10f));

            // 左端のオレンジ帯（路線カラー・角丸に合わせて少し内側＆丸める）
            Image accent = CreateImage("Accent", _card, AccentOrange);
            accent.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            accent.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            accent.rectTransform.pivot = new Vector2(0f, 0.5f);
            accent.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            accent.rectTransform.sizeDelta = new Vector2(6f, 104f);
            UiKit.Panelize(accent, 3);

            // 1行目：日付（大）＋行程（右寄せ・控えめ）
            _dayText = CreateText("Day", _card, 34, TextAnchor.MiddleLeft);
            _dayText.fontStyle = FontStyle.Bold;
            Anchor(_dayText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -8f), new Vector2(160f, 40f));
            _dayText.rectTransform.pivot = new Vector2(0f, 1f);
            UiKit.Outline(_dayText);

            _routeText = CreateText("Route", _card, 19, TextAnchor.MiddleRight);
            _routeText.color = DimText;
            Anchor(_routeText.rectTransform, new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(260f, 30f));
            _routeText.rectTransform.pivot = new Vector2(1f, 1f);

            // 2行目：体力バー（背景→追いバー→本体→フラッシュ→数値）。すべてピル形に丸める
            Image backImage = CreateImage("StaminaBack", _card, BarBack);
            RectTransform back = backImage.rectTransform;
            Anchor(back, new Vector2(0f, 1f), new Vector2(22f, -54f), new Vector2(BarWidth, BarHeight));
            back.pivot = new Vector2(0f, 1f);
            UiKit.Panelize(backImage, 14);

            _trail = LeftFill(UiKit.Panelize(CreateImage("StaminaTrail", back, TrailColor), 13).rectTransform);
            _fillImage = UiKit.Panelize(CreateImage("StaminaFill", back, BarHigh), 13);
            _fill = LeftFill(_fillImage.rectTransform);
            _flashImage = UiKit.Panelize(CreateImage("StaminaFlash", back, new Color(1f, 1f, 1f, 0f)), 13);
            LeftFill(_flashImage.rectTransform);

            _staminaLabel = CreateText("StaminaValue", back, 20, TextAnchor.MiddleRight);
            _staminaLabel.fontStyle = FontStyle.Bold;
            UiKit.Outline(_staminaLabel);
            _staminaLabel.rectTransform.anchorMin = Vector2.zero;
            _staminaLabel.rectTransform.anchorMax = Vector2.one;
            _staminaLabel.rectTransform.offsetMin = new Vector2(0f, 0f);
            _staminaLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);

            // 3行目：運行状況（停車中／次の駅まで）
            _statusText = CreateText("Status", _card, 19, TextAnchor.MiddleLeft);
            _statusText.color = DimText;
            Anchor(_statusText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -94f), new Vector2(360f, 26f));
            _statusText.rectTransform.pivot = new Vector2(0f, 1f);

            // ---- 下中央：操作プロンプト／中央：クロスヘア ----
            _prompt = CreateText("Prompt", root, 24, TextAnchor.LowerCenter);
            _prompt.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _prompt.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _prompt.rectTransform.pivot = new Vector2(0.5f, 0f);
            _prompt.rectTransform.anchoredPosition = new Vector2(0f, 70f);
            _prompt.rectTransform.sizeDelta = new Vector2(500f, 32f);
            UiKit.Outline(_prompt);

            // 中央：ドット＋ティックのクロスヘア（"+"文字をやめる）
            UiKit.Crosshair(root, new Color(1f, 1f, 1f, 0.85f));
            // ※日替わり・ゲームオーバーの演出は CutInView が担当（ここは常時HUDのみ）
        }

        /// <summary>バー内側を左詰めで伸びる矩形にする共通設定。</summary>
        private static RectTransform LeftFill(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(2f, 0f);
            rect.sizeDelta = new Vector2(BarWidth - 4f, BarHeight - 4f);
            return rect;
        }

        private static void Anchor(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Image CreateImage(string objName, Transform parent, Color color)
        {
            var go = new GameObject(objName, typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string objName, Transform parent, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(objName, typeof(Text));
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

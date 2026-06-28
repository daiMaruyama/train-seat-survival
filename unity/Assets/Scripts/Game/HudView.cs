using UnityEngine;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// HUD の描画"だけ"を担う。<see cref="StaminaSystem"/> / <see cref="CommuteDirector"/> /
    /// <see cref="PlayerSit"/> を読み取って uGUI に反映するだけで、ゲームのルールは一切持たない。
    /// だから後で UI Toolkit や既存アセットへ替えるときは、この描画クラスを差し替えるだけで済む。
    /// （uGUI 一式はコードで生成しているので、シーン側の手組みは不要。）
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private static readonly Color BarBackColor = new Color(0f, 0f, 0f, 0.5f);
        private static readonly Color BarHighColor = new Color(0.30f, 0.80f, 0.40f);
        private static readonly Color BarMidColor = new Color(0.95f, 0.75f, 0.20f);
        private static readonly Color BarLowColor = new Color(0.90f, 0.30f, 0.30f);

        private const float BarWidth = 320f;
        private const float BarHeight = 26f;

        private StaminaSystem _stamina;
        private CommuteDirector _director;
        private PlayerSit _player;

        private RectTransform _fill;
        private Image _fillImage;
        private Text _staminaLabel;
        private Text _info;
        private Text _prompt;

        private void Start()
        {
            _stamina = FindFirstObjectByType<StaminaSystem>();
            _director = FindFirstObjectByType<CommuteDirector>();
            _player = FindFirstObjectByType<PlayerSit>();
            Build();
        }

        private void Update()
        {
            if (_stamina != null)
            {
                float t = _stamina.Normalized;
                _fill.sizeDelta = new Vector2((BarWidth - 4f) * t, BarHeight - 4f);
                _fillImage.color = t > 0.5f ? BarHighColor : t > 0.25f ? BarMidColor : BarLowColor;
                _staminaLabel.text = $"体力  {Mathf.CeilToInt(_stamina.Current)}";
            }

            if (_director != null)
            {
                string tail = _director.IsEndOfLine ? "   終点" : "   Space 次の駅へ";
                _info.text = $"駅 {_director.CurrentStation}/{_director.StationCount - 1}    空席 {_director.FreeSeats}{tail}";
            }

            if (_player != null)
            {
                _prompt.text = _player.IsSeated ? "着席中    E で立つ"
                             : _player.CanSitNow ? "E    座る"
                             : string.Empty;
            }
        }

        private void Build()
        {
            var canvasGo = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            RectTransform root = canvas.GetComponent<RectTransform>();

            // 体力ゲージ（左上）。背景＋左詰めの塗り。
            RectTransform back = CreateImage("StaminaBack", root, BarBackColor).rectTransform;
            Anchor(back, new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(BarWidth, BarHeight));

            _fillImage = CreateImage("StaminaFill", back, BarHighColor);
            _fill = _fillImage.rectTransform;
            _fill.anchorMin = new Vector2(0f, 0.5f);
            _fill.anchorMax = new Vector2(0f, 0.5f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = new Vector2(2f, 0f);
            _fill.sizeDelta = new Vector2(BarWidth - 4f, BarHeight - 4f);

            _staminaLabel = CreateText("StaminaLabel", root, 20, TextAnchor.UpperLeft);
            Anchor(_staminaLabel.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -22f), new Vector2(BarWidth, BarHeight));

            _info = CreateText("Info", root, 22, TextAnchor.UpperLeft);
            Anchor(_info.rectTransform, new Vector2(0f, 1f), new Vector2(20f, -56f), new Vector2(700f, 30f));

            _prompt = CreateText("Prompt", root, 24, TextAnchor.LowerCenter);
            _prompt.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _prompt.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _prompt.rectTransform.pivot = new Vector2(0.5f, 0f);
            _prompt.rectTransform.anchoredPosition = new Vector2(0f, 70f);
            _prompt.rectTransform.sizeDelta = new Vector2(500f, 32f);

            Text crosshair = CreateText("Crosshair", root, 26, TextAnchor.MiddleCenter);
            crosshair.text = "+";
            crosshair.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            crosshair.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            crosshair.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            crosshair.rectTransform.anchoredPosition = Vector2.zero;
            crosshair.rectTransform.sizeDelta = new Vector2(40f, 40f);
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
            return image;
        }

        private static Text CreateText(string objName, Transform parent, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(objName, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}

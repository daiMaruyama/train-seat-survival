using UnityEngine;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// UI背景を横切る「長い通勤電車」モチーフ。短い光線ではなく、
    /// 車体・窓列・中央線カラーの帯を一体で流し、画面が生きていることを伝える。
    /// レイアウトを読めるよう、全要素は raycastTarget=false で半透明。
    /// </summary>
    public sealed class TransitBackdrop : MonoBehaviour
    {
        private static readonly Color Body = new(0.17f, 0.20f, 0.29f);
        private static readonly Color Window = new(1f, 0.86f, 0.55f);
        private static readonly Color Orange = new(0.95f, 0.45f, 0.15f);

        private RectTransform _nearTrain;
        private RectTransform _farTrain;
        private float _alpha = 1f;

        public void Build(float alpha = 1f)
        {
            _alpha = Mathf.Clamp01(alpha);
            RectTransform root = transform as RectTransform;
            if (root == null)
            {
                return;
            }
            Stretch(root);

            // 奥は反対方向へゆっくり、手前は幅広の車体が大きく横切る。
            _farTrain = BuildTrain("FarTrain", root, 0.72f, 0.42f);
            _farTrain.anchoredPosition = new Vector2(0f, 300f);
            _nearTrain = BuildTrain("NearTrain", root, 1f, 0.72f);
            _nearTrain.anchoredPosition = new Vector2(0f, -315f);
        }

        private void Update()
        {
            float t = Time.unscaledTime;
            if (_nearTrain != null)
            {
                float x = Mathf.Repeat(t * 255f + 3300f, 6600f) - 3300f;
                _nearTrain.anchoredPosition = new Vector2(x, -315f);
            }
            if (_farTrain != null)
            {
                float x = 3300f - Mathf.Repeat(t * 150f + 900f, 6600f);
                _farTrain.anchoredPosition = new Vector2(x, 300f);
            }
        }

        private RectTransform BuildTrain(string name, RectTransform parent, float scale, float layerAlpha)
        {
            var train = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            train.SetParent(parent, false);
            train.anchorMin = train.anchorMax = new Vector2(0.5f, 0.5f);
            train.pivot = new Vector2(0.5f, 0.5f);
            train.sizeDelta = new Vector2(2920f, 330f);
            train.localScale = Vector3.one * scale;

            Image body = Image("Body", train, WithAlpha(Body, 0.50f * layerAlpha));
            Stretch(body.rectTransform);

            // 長いオレンジ帯が「謎の線」に見えないよう、必ず車体と窓の間に通す。
            Image stripe = Image("OrangeLine", train, WithAlpha(Orange, 0.72f * layerAlpha));
            stripe.rectTransform.anchorMin = new Vector2(0f, 0.22f);
            stripe.rectTransform.anchorMax = new Vector2(1f, 0.22f);
            stripe.rectTransform.sizeDelta = new Vector2(0f, 18f);
            stripe.rectTransform.anchoredPosition = Vector2.zero;

            for (int i = 0; i < 14; i++)
            {
                float x = -1340f + i * 205f;
                Image window = Image($"Window_{i:00}", train, WithAlpha(Window, 0.42f * layerAlpha));
                window.rectTransform.anchorMin = window.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                window.rectTransform.sizeDelta = new Vector2(128f, 112f);
                window.rectTransform.anchoredPosition = new Vector2(x, 42f);

                // 連結部の縦バーを入れ、窓の連続が「電車」と読める輪郭にする。
                if (i < 13)
                {
                    Image joint = Image($"Joint_{i:00}", train, WithAlpha(new Color(0.04f, 0.06f, 0.10f), 0.60f * layerAlpha));
                    joint.rectTransform.anchorMin = joint.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    joint.rectTransform.sizeDelta = new Vector2(8f, 260f);
                    joint.rectTransform.anchoredPosition = new Vector2(x + 102f, 0f);
                }
            }

            // 窓の下に長い床下影を置き、シルエットを締める。
            Image under = Image("UnderCar", train, WithAlpha(new Color(0.02f, 0.03f, 0.06f), 0.72f * layerAlpha));
            under.rectTransform.anchorMin = new Vector2(0f, 0f);
            under.rectTransform.anchorMax = new Vector2(1f, 0f);
            under.rectTransform.pivot = new Vector2(0.5f, 0f);
            under.rectTransform.sizeDelta = new Vector2(0f, 48f);
            under.rectTransform.anchoredPosition = Vector2.zero;
            return train;
        }

        private Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha * _alpha;
            return color;
        }

        private static Image Image(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}

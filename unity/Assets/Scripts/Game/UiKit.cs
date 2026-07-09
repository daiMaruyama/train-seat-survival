using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrainSurvival.Game
{
    /// <summary>
    /// コードで組む uGUI の「安っぽさ」を消すための共通キット。
    /// ・角丸スプライト／柔らかいドロップシャドウをテクスチャから生成し、9スライスで綺麗に伸ばす
    /// ・パネルの角丸化・影付け、文字の縁取り、ちゃんとしたクロスヘアをワンコールで付与
    /// 生成物は半径ごとにキャッシュするので何度呼んでも軽い。全 UI で同じ質感に揃える土台。
    /// </summary>
    public static class UiKit
    {
        private static readonly Dictionary<int, Sprite> _roundedCache = new Dictionary<int, Sprite>();
        private static readonly Dictionary<int, Sprite> _shadowCache = new Dictionary<int, Sprite>();

        /// <summary>角丸の塗り（9スライス用ボーダー付き）。半径ごとにキャッシュ。</summary>
        public static Sprite RoundedRect(int radius)
        {
            radius = Mathf.Clamp(radius, 2, 128);
            if (_roundedCache.TryGetValue(radius, out Sprite cached))
            {
                return cached;
            }

            int size = radius * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float box = half - radius; // 角丸を除いた矩形の半幅
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;
                    float dx = Mathf.Abs(px) - box;
                    float dy = Mathf.Abs(py) - box;
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float sdf = Mathf.Min(Mathf.Max(dx, dy), 0f) + outside - radius; // 負=内側
                    float a = Mathf.Clamp01(0.5f - sdf / 1.6f); // 縁を1.6pxでアンチエイリアス
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            float b = radius + 1f;
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            _roundedCache[radius] = sprite;
            return sprite;
        }

        /// <summary>柔らかい影（角丸をぼかしたブロブ）。key=radius*1000+blur でキャッシュ。</summary>
        public static Sprite SoftShadow(int radius, int blur)
        {
            radius = Mathf.Clamp(radius, 2, 96);
            blur = Mathf.Clamp(blur, 4, 96);
            int key = radius * 1000 + blur;
            if (_shadowCache.TryGetValue(key, out Sprite cached))
            {
                return cached;
            }

            int pad = blur;
            int size = radius * 2 + pad * 2 + 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float box = half - radius - pad;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;
                    float dx = Mathf.Abs(px) - box;
                    float dy = Mathf.Abs(py) - box;
                    float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                    float sdf = Mathf.Min(Mathf.Max(dx, dy), 0f) + outside - radius;
                    // 影は blur 幅でなだらかに減衰。内側は1、外側へ二次関数で落とす
                    float a = Mathf.Clamp01(1f - sdf / blur);
                    a = a * a;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            float bd = radius + pad + 1f;
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(bd, bd, bd, bd));
            _shadowCache[key] = sprite;
            return sprite;
        }

        /// <summary>既存の Image を角丸パネル化する（色は保持）。</summary>
        public static Image Panelize(Image image, int radius)
        {
            image.sprite = RoundedRect(radius);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        /// <summary>パネルの背後に柔らかいドロップシャドウを敷く（同じ親・同じ矩形にコピー）。</summary>
        public static void AddShadow(Image panel, int radius, int blur = 26, float alpha = 0.45f, Vector2 offset = default)
        {
            if (offset == default)
            {
                offset = new Vector2(0f, -8f);
            }

            RectTransform src = panel.rectTransform;
            var go = new GameObject(panel.name + "_Shadow", typeof(Image));
            go.transform.SetParent(src.parent, false);
            var shadow = go.GetComponent<Image>();
            shadow.sprite = SoftShadow(radius, blur);
            shadow.type = Image.Type.Sliced;
            shadow.color = new Color(0f, 0f, 0f, alpha);
            shadow.raycastTarget = false;

            RectTransform sr = shadow.rectTransform;
            sr.anchorMin = src.anchorMin;
            sr.anchorMax = src.anchorMax;
            sr.pivot = src.pivot;
            sr.sizeDelta = src.sizeDelta + new Vector2(blur, blur);
            sr.anchoredPosition = src.anchoredPosition + offset;
            sr.SetSiblingIndex(src.GetSiblingIndex()); // パネルのすぐ後ろ（背面）へ
        }

        /// <summary>文字に縁取り＋落ち影を付けて視認性と質感を上げる。</summary>
        public static void Outline(Text text, float thickness = 1.6f, float shadowAlpha = 0.5f)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(thickness, -thickness);

            if (shadowAlpha > 0f)
            {
                var shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, shadowAlpha);
                shadow.effectDistance = new Vector2(0f, -3f);
            }
        }

        /// <summary>
        /// 全シーン共通のボタン（ペルソナ／インディー調）。角はシャープ、影はぼかさないハードなオフセット、
        /// 太いオレンジの縁。ホバーで面がオレンジに反転して影から一段“浮き”、押すと影へめり込む。
        /// 生成した Button を返す（onClick 追加や RectTransform 取得に使える）。
        /// </summary>
        public static Button MakeButton(RectTransform parent, Vector2 anchoredPos, Vector2 size, string label, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            Color navy = new Color(0.10f, 0.12f, 0.18f, 1f);
            Color orange = new Color(0.95f, 0.45f, 0.15f, 1f);
            Color cream = new Color(0.96f, 0.94f, 0.89f, 1f);
            Color shadowCol = new Color(0f, 0f, 0f, 0.6f);
            const float bw = 4f; // 縁の太さ

            // root＝クリック判定エリア（透明・不動）。ホバーで動かすのは中の見た目だけにして、
            // 判定がカーソルから逃げて Enter/Exit を連打する“チカチカ”を防ぐ。
            var rootGo = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var root = rootGo.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchoredPosition = anchoredPos;
            root.sizeDelta = size;
            var hit = rootGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f); // 透明でもレイキャストは効く
            hit.raycastTarget = true;

            // ハード（ぼかさない）オフセット影＝コミック/リソグラフ感（root直下・不動）
            Image shadow = PlainImage("Shadow", root, shadowCol, false);
            Fill(shadow.rectTransform);
            shadow.rectTransform.anchoredPosition = new Vector2(9f, -9f);

            // 見た目一式（ホバー/押下で動くのはこのコンテナ）
            var visual = new GameObject("Visual", typeof(RectTransform)).GetComponent<RectTransform>();
            visual.SetParent(root, false);
            visual.anchorMin = visual.anchorMax = new Vector2(0.5f, 0.5f);
            visual.pivot = new Vector2(0.5f, 0.5f);
            visual.sizeDelta = size;
            visual.anchoredPosition = Vector2.zero;

            Image border = PlainImage("Border", visual, orange, false); // 太い縁（オレンジ）
            Fill(border.rectTransform);
            Image fill = PlainImage("Fill", border.rectTransform, navy, false); // 面（ネイビー）
            Fill(fill.rectTransform);
            Inset(fill.rectTransform, bw);
            Text text = ButtonLabel(fill.rectTransform, label, fontSize, cream);

            var btn = rootGo.GetComponent<Button>();
            btn.transition = Selectable.Transition.None; // 見た目はこちらで制御
            btn.targetGraphic = hit;
            if (onClick != null)
            {
                btn.onClick.AddListener(onClick);
            }

            var trigger = rootGo.AddComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () =>
            {
                border.color = cream; fill.color = orange; text.color = navy;
                visual.anchoredPosition = new Vector2(-3f, 3f);              // 見た目だけ浮く（判定は不動）
                shadow.rectTransform.anchoredPosition = new Vector2(13f, -13f);
            });
            AddTrigger(trigger, EventTriggerType.PointerExit, () =>
            {
                border.color = orange; fill.color = navy; text.color = cream;
                visual.anchoredPosition = Vector2.zero;
                shadow.rectTransform.anchoredPosition = new Vector2(9f, -9f);
            });
            AddTrigger(trigger, EventTriggerType.PointerDown, () =>
            {
                visual.anchoredPosition = new Vector2(6f, -6f);             // 影へめり込む
                shadow.rectTransform.anchoredPosition = new Vector2(3f, -3f);
            });
            AddTrigger(trigger, EventTriggerType.PointerUp, () =>
            {
                visual.anchoredPosition = new Vector2(-3f, 3f);
                shadow.rectTransform.anchoredPosition = new Vector2(13f, -13f);
            });
            return btn;
        }

        private static Image PlainImage(string name, Transform parent, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;           // sprite 無し＝シャープな矩形
            img.raycastTarget = raycast;
            return img;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void Inset(RectTransform rt, float m)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(m, m);
            rt.offsetMax = new Vector2(-m, -m);
        }

        private static Text ButtonLabel(RectTransform parent, string label, int size, Color color)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UiFont.Load();
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = label;
            RectTransform r = t.rectTransform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
            return t;
        }

        private static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        /// <summary>中央ドット＋四方の短いティックで構成した最小限のクロスヘアを作る。</summary>
        public static RectTransform Crosshair(RectTransform root, Color color)
        {
            var container = new GameObject("Crosshair", typeof(RectTransform)).GetComponent<RectTransform>();
            container.SetParent(root, false);
            container.anchorMin = container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = Vector2.zero;
            container.sizeDelta = new Vector2(28f, 28f);

            // 中央ドット
            Tick(container, "Dot", color, new Vector2(4f, 4f), Vector2.zero);
            // 四方の短いライン（中央に小さな隙間）
            Tick(container, "Up", color, new Vector2(2f, 7f), new Vector2(0f, 9f));
            Tick(container, "Down", color, new Vector2(2f, 7f), new Vector2(0f, -9f));
            Tick(container, "Left", color, new Vector2(7f, 2f), new Vector2(-9f, 0f));
            Tick(container, "Right", color, new Vector2(7f, 2f), new Vector2(9f, 0f));
            return container;
        }

        private static void Tick(RectTransform parent, string name, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1f, -1f);
        }
    }
}

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

        private static Sprite _kenneyPanel;
        private static bool _kenneyPanelLoaded;

        /// <summary>Kenney のパネル下地（Resources/UiSprites。未導入なら null で従来描画に落ちる）。</summary>
        private static Sprite KenneyPanel()
        {
            if (!_kenneyPanelLoaded)
            {
                _kenneyPanel = Resources.Load<Sprite>("UiSprites/button_square_flat");
                _kenneyPanelLoaded = true;
            }
            return _kenneyPanel;
        }

        /// <summary>
        /// 既存の Image を角丸パネル化する（色は保持）。中くらいの角丸（7〜20）は Kenney の
        /// 9スライス下地を使い、細い帯（〜6）と円形（21〜）は従来の手続き生成を使う。
        /// ゲージやスライダーの塗りなど高さの小さい矩形は forceProcedural=true で従来描画を強制
        /// （9スライスの境界16pxが要素サイズを超えて崩れるため）。
        /// </summary>
        public static Image Panelize(Image image, int radius, bool forceProcedural = false)
        {
            Sprite kenney = forceProcedural || radius < 7 || radius > 20 ? null : KenneyPanel();
            image.sprite = kenney != null ? kenney : RoundedRect(radius);
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

        private static Sprite _ledGrid;
        private static Sprite _paperGrain;

        /// <summary>LED盤のドット格子（4pxタイル）。文字の上に薄く重ねると電光掲示板のドット感が出る。</summary>
        public static void AddLedGrid(RectTransform parent, float alpha = 0.30f)
        {
            if (_ledGrid == null)
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                };
                var px = new Color32[16];
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        // 格子線（各タイルの左端・下端）だけ黒、残りは透明＝ドットの隙間
                        bool line = x == 0 || y == 0;
                        px[y * 4 + x] = line ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                _ledGrid = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            }

            var go = new GameObject("LedGrid", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _ledGrid;
            img.type = Image.Type.Tiled;
            img.color = new Color(0f, 0f, 0f, alpha);
            img.raycastTarget = false;
            Fill(img.rectTransform);
        }

        /// <summary>紙の繊維グレイン（ノイズタイル）。クリーム紙に薄く重ねると「書類」の質感が出る。</summary>
        public static void AddPaperGrain(RectTransform parent, float alpha = 0.045f)
        {
            if (_paperGrain == null)
            {
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
                var px = new Color32[size * size];
                var rng = new System.Random(1234); // 固定シード＝毎回同じ紙
                for (int i = 0; i < px.Length; i++)
                {
                    // まばらな暗い斑点＋ごく薄い繊維ムラ
                    double v = rng.NextDouble();
                    byte a = v > 0.86 ? (byte)rng.Next(90, 180) : v > 0.55 ? (byte)rng.Next(15, 45) : (byte)0;
                    px[i] = new Color32(40, 34, 26, a);
                }
                tex.SetPixels32(px);
                tex.Apply();
                _paperGrain = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            }

            var go = new GameObject("PaperGrain", typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = _paperGrain;
            img.type = Image.Type.Tiled;
            // テクスチャ側にも濃淡があるため、強く掛けると文字より斑点が勝つ。
            // 紙だと分かる最低限の濃さに留める。
            img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha * 4f));
            img.raycastTarget = false;
            Fill(img.rectTransform);
        }

        /// <summary>
        /// パネルに枠線（Kenney の border スプライト）を重ねて「駅サインの額縁」にする。
        /// 平らな下地だけでは手続き生成と見分けが付かないため、構造のある縁で質感を出す。
        /// </summary>
        public static void AddFrame(Image panel, Color color, float inset = 0f)
        {
            var sprite = Resources.Load<Sprite>("UiSprites/button_rectangle_border");
            if (sprite == null)
            {
                return; // 未導入なら何もしない
            }
            var go = new GameObject("Frame", typeof(Image));
            go.transform.SetParent(panel.transform, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>
        /// 紙の上のスライダー（採用版）：明朝ラベル＋インクの線＋蛍光マーカーの塗り＋インクの丸つまみ。
        /// 中吊り広告・小休止願など「紙もの」で共通に使う。topLeft は紙の左上基準。
        /// </summary>
        public static Slider MakePaperSlider(RectTransform paper, Vector2 topLeft, float totalWidth, string label, float value, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
        {
            Color ink = new Color(0.15f, 0.16f, 0.19f);
            const float labelW = 68f;

            var tagGo = new GameObject(label + "Tag", typeof(Text));
            tagGo.transform.SetParent(paper, false);
            var tag = tagGo.GetComponent<Text>();
            tag.font = UiFont.LoadMincho();
            tag.fontSize = 20;
            tag.fontStyle = FontStyle.Bold;
            tag.color = ink;
            tag.alignment = TextAnchor.MiddleLeft;
            tag.raycastTarget = false;
            tag.horizontalOverflow = HorizontalWrapMode.Overflow;
            tag.text = label;
            RectTransform tagRect = tag.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = topLeft;
            tagRect.sizeDelta = new Vector2(labelW, 28f);

            var sliderGo = new GameObject(label + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderGo.transform.SetParent(paper, false);
            RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(topLeft.x + labelW + 8f, topLeft.y);
            sliderRect.sizeDelta = new Vector2(Mathf.Max(60f, totalWidth - labelW - 8f), 28f);
            Image hit = sliderGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            // 小さい明朝ラベルはPlayerで欠落する環境があるため、呼び出し側で通常フォントの
            // ラベルを重ねる。ここで作ったものはレイアウト幅の計算だけに使い非表示にする。
            tag.gameObject.SetActive(false);

            // 蛍光マーカーの塗り（線より太く、後ろに）
            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(sliderRect, false);
            RectTransform fillArea = fillAreaGo.GetComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(0f, -2f);
            fillArea.offsetMax = new Vector2(0f, 2f);
            Image fill = PlainImage("Fill", fillArea, new Color(1f, 0.58f, 0.12f, 0.8f), false);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 10f);

            // インクの線
            Image line = PlainImage("InkLine", sliderRect, new Color(ink.r, ink.g, ink.b, 0.95f), false);
            line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f);
            line.rectTransform.anchoredPosition = Vector2.zero;

            // インクの丸つまみ
            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(sliderRect, false);
            RectTransform handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            Image knob = PlainImage("Handle", handleArea, ink, true);
            RectTransform knobRect = knob.rectTransform;
            knobRect.sizeDelta = new Vector2(16f, 16f);
            Panelize(knob, 8, forceProcedural: true); // 正円

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.targetGraphic = hit;
            slider.fillRect = fillRect;
            slider.handleRect = knobRect;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(onChanged);
            }
            return slider;
        }

        /// <summary>
        /// 機器盤のフェーダー（旧版）。ドット文字ラベル＋インセットの溝＋目盛り＋
        /// 矩形キャップ＋通電表示のアンバーライン。※紙もの画面では MakePaperSlider を使う。
        /// </summary>
        public static Slider MakeFader(RectTransform parent, Vector2 topLeft, float totalWidth, string label, float value, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
        {
            Color amber = new Color(1f, 0.72f, 0.2f);
            Color orange = new Color(0.95f, 0.45f, 0.15f);
            const float labelW = 84f;
            float faderW = Mathf.Max(60f, totalWidth - labelW - 8f);

            var tagGo = new GameObject(label + "Tag", typeof(Text));
            tagGo.transform.SetParent(parent, false);
            var tag = tagGo.GetComponent<Text>();
            tag.font = UiFont.LoadLed(); // 機器ラベルはドット文字
            tag.fontSize = 16;
            tag.color = new Color(0.85f, 0.88f, 0.9f, 0.85f);
            tag.alignment = TextAnchor.MiddleLeft;
            tag.raycastTarget = false;
            tag.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform tagRect = tag.rectTransform;
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 1f);
            tagRect.anchoredPosition = topLeft;
            tagRect.sizeDelta = new Vector2(labelW, 26f);

            var sliderGo = new GameObject(label + "Fader", typeof(RectTransform), typeof(Image), typeof(Slider));
            sliderGo.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0f, 1f);
            sliderRect.pivot = new Vector2(0f, 1f);
            sliderRect.anchoredPosition = new Vector2(topLeft.x + labelW + 8f, topLeft.y);
            sliderRect.sizeDelta = new Vector2(faderW, 26f);
            Image hit = sliderGo.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f); // 透明の当たり判定

            // 溝（インセットの細いスリット）
            Image groove = PlainImage("Groove", sliderRect, new Color(0f, 0f, 0f, 0.62f), false);
            RectTransform gr = groove.rectTransform;
            gr.anchorMin = new Vector2(0f, 0.5f);
            gr.anchorMax = new Vector2(1f, 0.5f);
            gr.pivot = new Vector2(0.5f, 0.5f);
            gr.sizeDelta = new Vector2(0f, 6f);
            gr.anchoredPosition = Vector2.zero;
            var grooveEdge = groove.gameObject.AddComponent<Outline>();
            grooveEdge.effectColor = new Color(1f, 1f, 1f, 0.08f);
            grooveEdge.effectDistance = new Vector2(0f, -1f);

            // 目盛り（下側に5本）
            for (int i = 0; i <= 4; i++)
            {
                Image tick = PlainImage("Tick", sliderRect, new Color(1f, 1f, 1f, 0.22f), false);
                RectTransform tr = tick.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(i / 4f, 0f);
                tr.pivot = new Vector2(0.5f, 0f);
                tr.anchoredPosition = new Vector2(i == 0 ? 3f : i == 4 ? -3f : 0f, -1f);
                tr.sizeDelta = new Vector2(2f, 5f);
            }

            // 塗り（アンバーの通電ライン）
            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(sliderRect, false);
            RectTransform fillArea = fillAreaGo.GetComponent<RectTransform>();
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.offsetMin = new Vector2(2f, -2f);
            fillArea.offsetMax = new Vector2(-2f, 2f);
            Image fill = PlainImage("Fill", fillArea, new Color(amber.r, amber.g, amber.b, 0.85f), false);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(0f, 0.5f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 4f);

            // フェーダーキャップ（矩形・オレンジ・刻み線）
            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(sliderRect, false);
            RectTransform handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.anchorMin = new Vector2(0f, 0.5f);
            handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.offsetMin = new Vector2(7f, 0f);
            handleArea.offsetMax = new Vector2(-7f, 0f);
            Image cap = PlainImage("Handle", handleArea, orange, true);
            RectTransform capRect = cap.rectTransform;
            capRect.sizeDelta = new Vector2(14f, 24f);
            var capEdge = cap.gameObject.AddComponent<Outline>();
            capEdge.effectColor = new Color(0f, 0f, 0f, 0.6f);
            capEdge.effectDistance = new Vector2(1.5f, -1.5f);
            Image capLine = PlainImage("CapLine", capRect, new Color(0.12f, 0.08f, 0.04f, 0.9f), false);
            capLine.rectTransform.anchorMin = new Vector2(0.5f, 0.15f);
            capLine.rectTransform.anchorMax = new Vector2(0.5f, 0.85f);
            capLine.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            capLine.rectTransform.sizeDelta = new Vector2(2f, 0f);
            capLine.rectTransform.anchoredPosition = Vector2.zero;

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.targetGraphic = hit;
            slider.fillRect = fillRect;
            slider.handleRect = capRect;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(onChanged);
            }
            return slider;
        }

        /// <summary>
        /// 共通スライダー（ラベル左＋トラック右・オレンジ塗り・丸ノブ）。タイトルの音量スライダーと
        /// 同じ意匠を全画面の基準として使い回す。
        /// </summary>
        public static Slider MakeSlider(RectTransform parent, Vector2 anchoredPos, float width, string label, float value, float min, float max, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var rowGo = new GameObject(label + "Slider", typeof(RectTransform));
            var row = rowGo.GetComponent<RectTransform>();
            row.SetParent(parent, false);
            row.anchoredPosition = anchoredPos;
            row.sizeDelta = new Vector2(width, 30f);

            Text tag = ButtonLabel(row, label, 20, new Color(1f, 1f, 1f, 0.72f));
            tag.alignment = TextAnchor.MiddleLeft;
            tag.rectTransform.anchorMin = new Vector2(0f, 0f);
            tag.rectTransform.anchorMax = new Vector2(0.34f, 1f);

            var trackGo = new GameObject("Track", typeof(RectTransform), typeof(Image), typeof(Slider));
            trackGo.transform.SetParent(row, false);
            var track = trackGo.GetComponent<RectTransform>();
            track.anchorMin = new Vector2(0.36f, 0.22f);
            track.anchorMax = new Vector2(1f, 0.78f);
            track.offsetMin = Vector2.zero;
            track.offsetMax = Vector2.zero;
            // トラックは柔らかい溝（Kenneyのslide素材は形が尖っていて世界観に合わなかったため不採用）
            var bg = trackGo.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.38f);
            Panelize(bg, 9, forceProcedural: true);

            var fillAreaGo = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaGo.transform.SetParent(track, false);
            var fillArea = fillAreaGo.GetComponent<RectTransform>();
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(fillArea, false);
            var fill = fillGo.GetComponent<Image>();
            fill.color = new Color(0.95f, 0.45f, 0.15f);
            fill.raycastTarget = false;
            Panelize(fill, 8, forceProcedural: true); // 高さの小さい塗りは9スライスが崩れる
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.sizeDelta = Vector2.zero;

            var handleAreaGo = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleAreaGo.transform.SetParent(track, false);
            var handleArea = handleAreaGo.GetComponent<RectTransform>();
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(10f, 0f);
            handleArea.offsetMax = new Vector2(-10f, 0f);

            // ノブは丸（クリーム色＋細いオレンジ縁）＝駅サインの意匠に寄せる
            var handleGo = new GameObject("Handle", typeof(Image));
            handleGo.transform.SetParent(handleArea, false);
            var handle = handleGo.GetComponent<Image>();
            handle.color = new Color(0.96f, 0.94f, 0.89f);
            Panelize(handle, 11, forceProcedural: true); // 22px＋半径11＝正円
            handle.rectTransform.sizeDelta = new Vector2(22f, 22f);
            var handleEdge = handleGo.AddComponent<Outline>();
            handleEdge.effectColor = new Color(0.95f, 0.45f, 0.15f, 0.9f);
            handleEdge.effectDistance = new Vector2(1.5f, -1.5f);

            var slider = trackGo.GetComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fillRect;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            if (onChanged != null)
            {
                slider.onValueChanged.AddListener(onChanged);
            }
            return slider;
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

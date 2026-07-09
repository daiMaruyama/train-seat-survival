using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// データメガネの視界（席スキャン）。有効な間だけ、着席客が座っている「席」から色付きのビームと
    /// 足元パッドを灯す。緑＝次の駅で降りる（＝すぐ空く狙い目）／黄→橙→赤＝当分乗る。降りそうな席ほど
    /// 明るく高く脈打ち、当分空かない席は淡く沈むので、狙うべき席が一目で浮かび上がる。乗客本体には
    /// 触れないので不気味さが出ない。隠し情報 <see cref="Core.Passenger.DestinationStation"/> を
    /// <see cref="CommuteDirector.TryReadSeatIntel"/> 経由で覗くだけの描画専用ビュー（ルールは持たない）。
    /// </summary>
    public sealed class DataVisionView : MonoBehaviour
    {
        public static DataVisionView Instance { get; private set; }

        private CommuteDirector _director;
        private Transform[] _slots;
        private Transform[] _beams;
        private Material[] _beamMats;
        private Material[] _padMats;
        private float _activeUntil;

        /// <summary>いまデータ視界が有効か。</summary>
        public bool IsActive => Time.unscaledTime < _activeUntil;

        /// <summary>残り有効秒数（HUD 表示用）。</summary>
        public float Remaining => Mathf.Max(0f, _activeUntil - Time.unscaledTime);

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>メガネを拾ったら一定時間だけデータ視界を点ける（延長は累積せず上書き）。</summary>
        public void Activate(float duration)
        {
            _activeUntil = Time.unscaledTime + Mathf.Max(0f, duration);
        }

        /// <summary>効果を即オフにする（翌日への切り替えで持ち越さないため）。</summary>
        public void Deactivate()
        {
            _activeUntil = 0f;
        }

        private void LateUpdate()
        {
            if (_director == null)
            {
                _director = FindFirstObjectByType<CommuteDirector>();
            }
            if (_director == null || _director.SeatCount == 0)
            {
                return;
            }
            if (_slots == null)
            {
                BuildPool(_director.SeatCount);
            }

            bool on = IsActive;
            Camera cam = Camera.main;
            float t = Time.unscaledTime;

            for (int seat = 0; seat < _slots.Length; seat++)
            {
                bool readable = _director.TryReadSeatIntel(seat, out _, out int stations);
                bool show = on && cam != null && readable;
                if (!show)
                {
                    if (_slots[seat].gameObject.activeSelf)
                    {
                        _slots[seat].gameObject.SetActive(false);
                    }
                    continue;
                }

                Transform slot = _slots[seat];
                if (!slot.gameObject.activeSelf)
                {
                    slot.gameObject.SetActive(true);
                }
                // 席の足元に立て、カメラへ正対させる（縦ビーム）
                slot.SetPositionAndRotation(_director.GetSeat(seat).Position, cam.transform.rotation);

                // 降りそう度：0=次の駅で降りる … 大きいほど当分乗る
                float urgency = Mathf.Clamp01(1f - stations / 4f); // 1=すぐ空く, 0=当分
                Color c = UrgencyColor(stations);

                // 降りそうな席ほど眩しく脈動、当分空かない席は淡く沈める＝狙い目が浮かぶ
                float strength = Mathf.Lerp(0.14f, 1f, urgency);
                float pulse = 1f + Mathf.Sin(t * 3.5f + seat) * 0.14f * urgency;

                SetGlow(_beamMats[seat], c * (strength * pulse));
                SetGlow(_padMats[seat], c * (strength * 1.1f * pulse));

                // すぐ空く席ほどビームを高く（当分は低く）
                float height = 0.55f + 0.55f * urgency;
                _beams[seat].localScale = new Vector3(0.42f, 1.9f * height, 1f);
            }
        }

        private void BuildPool(int count)
        {
            _slots = new Transform[count];
            _beams = new Transform[count];
            _beamMats = new Material[count];
            _padMats = new Material[count];

            Texture2D radial = ItemBeacon.RadialTexture();
            for (int i = 0; i < count; i++)
            {
                var root = new GameObject($"SeatIntel_{i}");
                root.transform.SetParent(transform, false);
                root.SetActive(false);

                // 席から立ち上がる縦ビーム（人の頭より上まで届き、着席中でも視認できる）
                _beams[i] = MakeGlow("Beam", root.transform, radial,
                    localPos: new Vector3(0f, 1.0f, 0.02f), size: new Vector2(0.42f, 1.9f), out _beamMats[i]);

                // クッション付近の足元パッド（席そのものを指す明るい芯）
                MakeGlow("Pad", root.transform, radial,
                    localPos: new Vector3(0f, 0.45f, 0f), size: new Vector2(0.62f, 0.4f), out _padMats[i]);

                _slots[i] = root.transform;
            }
        }

        private static Transform MakeGlow(string name, Transform parent, Texture2D radial, Vector3 localPos, Vector2 size, out Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);

            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mat = ItemBeacon.AdditiveUnlit(Color.white, radial);
            renderer.sharedMaterial = mat;
            return go.transform;
        }

        private static void SetGlow(Material mat, Color color)
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }
        }

        private static Color UrgencyColor(int delta)
        {
            if (delta <= 0) return new Color(0.35f, 1f, 0.5f);    // 今にも空く（緑）
            if (delta == 1) return new Color(0.55f, 1f, 0.45f);   // 次で降りる
            if (delta == 2) return new Color(0.95f, 0.9f, 0.35f); // 黄
            if (delta == 3) return new Color(1f, 0.62f, 0.25f);   // 橙
            return new Color(1f, 0.4f, 0.34f);                    // 当分乗る（赤）
        }
    }
}

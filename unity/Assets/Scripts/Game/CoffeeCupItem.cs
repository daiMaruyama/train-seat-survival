using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内に浮かぶコーヒー。くるくる回転＋上下ボブ＋暖色の光で「取れるもの」だと一目で分かるようにし、
    /// プレイヤーが触れた瞬間に自動で飲んで体力を戻す（E 操作は不要）。当たり判定はトリガー。
    /// </summary>
    public sealed class CoffeeCupItem : MonoBehaviour
    {
        [SerializeField] private float _staminaRestore = 22f;
        [SerializeField] private float _spinSpeed = 100f;   // 回転（度/秒）
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField] private float _bobSpeed = 2.2f;
        [SerializeField] private Color _glowColor = new(1f, 0.72f, 0.4f);
        [SerializeField, Range(0f, 5f)] private float _glowIntensity = 1.2f;

        private Vector3 _basePosition;
        private float _phase;
        private Transform _halo;
        private Transform _beam;

        private static Texture2D _radialTexture;   // ハロー用の放射グラデ（全カップで共有）
        private static Material _haloMaterial;
        private static Material _beamMaterial;

        public bool IsConsumed { get; private set; }
        public float StaminaRestore => _staminaRestore;

        private void Start()
        {
            _basePosition = transform.localPosition;
            _phase = Random.value * 10f;

            ApplyGlow();
            CreateBeacon();
        }

        /// <summary>
        /// ライトやエミッションに頼らない「アイテムの記号」：床から立ち上る光の柱＋カメラを向くハロー。
        /// どちらも Unlit の加算描画なので、車内がどれだけ明るくても必ず光って見え、周囲は照らさない。
        /// </summary>
        private void CreateBeacon()
        {
            // 光の柱（床〜頭上）
            GameObject beamGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beamGo.name = "Beam";
            Destroy(beamGo.GetComponent<Collider>());
            beamGo.transform.SetParent(transform, false);
            beamGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            beamGo.transform.localScale = new Vector3(0.07f, 1.0f, 0.07f);
            var beamRenderer = beamGo.GetComponent<Renderer>();
            beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beamRenderer.sharedMaterial = BeamMaterial();
            _beam = beamGo.transform;

            // ハロー（放射グラデの板。毎フレームカメラへ向ける）
            GameObject haloGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            haloGo.name = "Halo";
            Destroy(haloGo.GetComponent<Collider>());
            haloGo.transform.SetParent(transform, false);
            haloGo.transform.localScale = Vector3.one * 0.6f;
            var haloRenderer = haloGo.GetComponent<Renderer>();
            haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            haloRenderer.sharedMaterial = HaloMaterial();
            _halo = haloGo.transform;
        }

        private Material BeamMaterial()
        {
            if (_beamMaterial == null)
            {
                _beamMaterial = MakeAdditiveUnlit(_glowColor * 0.1f, null);
            }
            return _beamMaterial;
        }

        private Material HaloMaterial()
        {
            if (_haloMaterial == null)
            {
                if (_radialTexture == null)
                {
                    _radialTexture = BuildRadialTexture(64);
                }
                _haloMaterial = MakeAdditiveUnlit(_glowColor * 0.9f, _radialTexture);
            }
            return _haloMaterial;
        }

        /// <summary>Unlit＋加算ブレンドのマテリアル（シーンの明るさに埋もれず、周囲も照らさない）。</summary>
        private static Material MakeAdditiveUnlit(Color color, Texture2D texture)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); // 加算
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.color = color;
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", color);
            }
            if (texture != null && m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", texture);
            }
            return m;
        }

        /// <summary>中心が明るく端へ滑らかに消える放射グラデテクスチャを手続き生成。</summary>
        private static Texture2D BuildRadialTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float a = t * t; // 中心ほど強く
                    tex.SetPixel(x, y, new Color(a, a, a, a));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private void ApplyGlow()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var material = renderer.material;
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", _glowColor * _glowIntensity);
            }
        }

        private void Update()
        {
            transform.Rotate(0f, _spinSpeed * Time.deltaTime, 0f, Space.World);
            Vector3 p = _basePosition;
            p.y += Mathf.Sin(Time.time * _bobSpeed + _phase) * _bobAmplitude;
            transform.localPosition = p;
        }

        private void LateUpdate()
        {
            // ハローは常にカメラへ向け、軽く脈動させて「取れるもの」感を出す（親の回転を打ち消す）
            float pulse = 1f + Mathf.Sin(Time.time * 3f + _phase) * 0.12f;
            if (_halo != null && Camera.main != null)
            {
                _halo.rotation = Camera.main.transform.rotation;
                _halo.localScale = Vector3.one * (0.6f * pulse);
            }
            if (_beam != null)
            {
                Vector3 s = _beam.localScale;
                s.x = s.z = 0.07f * pulse;
                _beam.localScale = s;
                _beam.rotation = Quaternion.identity; // 親のスピンに引っ張られない
            }
        }

        // プレイヤー（CharacterController）が触れたら自動で飲む
        private void OnTriggerEnter(Collider other)
        {
            var stamina = other.GetComponentInParent<StaminaSystem>();
            if (stamina != null)
            {
                TryDrink(stamina);
            }
        }

        public bool TryDrink(StaminaSystem stamina)
        {
            if (IsConsumed || stamina == null)
            {
                return false;
            }

            IsConsumed = true;
            stamina.Restore(_staminaRestore);
            Destroy(gameObject);
            return true;
        }
    }
}

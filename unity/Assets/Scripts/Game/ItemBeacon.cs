using UnityEngine;
using UnityEngine.Rendering;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 「取れるもの」だと一目で分かる光の演出（床から立ち上る光の柱＋カメラを向くハロー）。
    /// Unlit＋加算描画なので車内がどれだけ明るくても必ず光り、周囲は照らさない。コーヒーでもメガネでも
    /// 同じ記号で拾える所在を示すため、コンポーネント化して共有する（色だけ差し替え）。
    /// </summary>
    public sealed class ItemBeacon : MonoBehaviour
    {
        private Color _glow = new Color(1f, 0.72f, 0.4f);
        private Transform _halo;
        private Transform _beam;
        private float _phase;

        private static Texture2D _radialTexture;

        /// <summary>色を指定してビーコンを作る（Start より前に呼ぶ）。</summary>
        public void Configure(Color glow)
        {
            _glow = glow;
        }

        private void Start()
        {
            _phase = Random.value * 10f;

            GameObject beamGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beamGo.name = "Beam";
            Destroy(beamGo.GetComponent<Collider>());
            beamGo.transform.SetParent(transform, false);
            beamGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            beamGo.transform.localScale = new Vector3(0.07f, 1.0f, 0.07f);
            var beamRenderer = beamGo.GetComponent<Renderer>();
            beamRenderer.shadowCastingMode = ShadowCastingMode.Off;
            beamRenderer.sharedMaterial = AdditiveUnlit(_glow * 0.1f, null);
            _beam = beamGo.transform;

            GameObject haloGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            haloGo.name = "Halo";
            Destroy(haloGo.GetComponent<Collider>());
            haloGo.transform.SetParent(transform, false);
            haloGo.transform.localScale = Vector3.one * 0.6f;
            var haloRenderer = haloGo.GetComponent<Renderer>();
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.sharedMaterial = AdditiveUnlit(_glow * 0.9f, RadialTexture());
            _halo = haloGo.transform;
        }

        private void LateUpdate()
        {
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

        /// <summary>
        /// 取り込んだモデルの非URPマテリアル（Standard等）をURP/Litへ差し替える。
        /// これを通さないとURPで真っピンク（マテリアル欠損）になる。テクスチャ・色・金属/滑らかさを引き継ぐ。
        /// </summary>
        public static void EnsureUrpMaterials(GameObject target)
        {
            Shader lit = RuntimeMaterials.LitShader;
            if (lit == null)
            {
                return;
            }

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || source.shader == lit)
                    {
                        continue;
                    }

                    Texture texture = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : source.mainTexture;
                    Color color = source.HasProperty("_Color") ? source.color : Color.white;
                    float metallic = source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f;
                    float smoothness = source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.35f;

                    // エミッション対応テンプレを複製（後段の ApplyEmission がキーワードを立てても
                    // ビルドにそのバリアントが入っているようにする）
                    Material upgraded = RuntimeMaterials.LitEmissive();
                    upgraded.name = $"{source.name}_URP";
                    upgraded.SetColor("_EmissionColor", Color.black); // 既定は光らせない
                    if (texture != null && upgraded.HasProperty("_BaseMap"))
                    {
                        upgraded.SetTexture("_BaseMap", texture);
                    }
                    if (upgraded.HasProperty("_BaseColor"))
                    {
                        upgraded.SetColor("_BaseColor", color);
                    }
                    if (upgraded.HasProperty("_Metallic"))
                    {
                        upgraded.SetFloat("_Metallic", metallic);
                    }
                    if (upgraded.HasProperty("_Smoothness"))
                    {
                        upgraded.SetFloat("_Smoothness", smoothness);
                    }
                    materials[i] = upgraded;
                }
                renderer.materials = materials;
            }
        }

        /// <summary>アイテム本体のマテリアルへエミッションを足して自発光させる（任意）。</summary>
        public static void ApplyEmission(GameObject target, Color glow, float intensity)
        {
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                var material = renderer.material;
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", glow * intensity);
            }
        }

        /// <summary>Unlit＋加算ブレンドのマテリアル（シーンの明るさに埋もれず、周囲も照らさない）。</summary>
        public static Material AdditiveUnlit(Color color, Texture2D texture)
        {
            // テンプレート複製（ビルドに加算バリアントを確実に含めるため Shader.Find では組まない）
            var m = RuntimeMaterials.UnlitAdditive();
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.One); // 加算
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
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

        /// <summary>中心が明るく端へ滑らかに消える放射グラデ（全アイテム共有）。</summary>
        public static Texture2D RadialTexture()
        {
            if (_radialTexture != null)
            {
                return _radialTexture;
            }

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    float a = t * t;
                    tex.SetPixel(x, y, new Color(a, a, a, a));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            _radialTexture = tex;
            return tex;
        }
    }
}

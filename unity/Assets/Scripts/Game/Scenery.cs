using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 窓の外の景色。ローポリのビル群を2層（近景＝速い／遠景＝遅い）でパララックススクロールさせ、
    /// 「走っている電車」の車窓を作る。ビルは車両後方へ流れ、端まで行くと前方へループして高さ・色を
    /// 引き直すので、単調な繰り返しに見えにくい。地面と遠景の帯もここで生成する。
    /// すべてプリミティブ＋共有マテリアルなので負荷は軽い。速度は Inspector で調整可。
    /// </summary>
    public sealed class Scenery : MonoBehaviour
    {
        [Header("スクロール（電車の疑似速度）")]
        [SerializeField] private float _nearSpeed = 9f;   // 近景の流れる速さ(m/s)
        [SerializeField] private float _farSpeed = 3.5f;  // 遠景の流れる速さ(m/s)

        [Header("配置")]
        [SerializeField] private float _loopLength = 90f; // ループ区間の長さ
        [SerializeField] private float _nearDistance = 8f;  // 窓からの距離（近景）
        [SerializeField] private float _farDistance = 22f;  // 窓からの距離（遠景）
        [SerializeField] private int _buildingsPerLayer = 14; // 片側・1層あたりのビル数

        // 車内（明るい暖色グレー）と被らない、彩度のある街色パレット
        private static readonly Color[] BuildingColors =
        {
            new Color(0.58f, 0.30f, 0.24f), // レンガ
            new Color(0.22f, 0.42f, 0.48f), // 青緑
            new Color(0.72f, 0.56f, 0.28f), // 黄土
            new Color(0.28f, 0.32f, 0.46f), // 紺鼠
            new Color(0.38f, 0.46f, 0.34f), // 深緑
            new Color(0.45f, 0.35f, 0.42f), // 小豆
        };
        private static readonly Color GroundColor = new Color(0.35f, 0.37f, 0.34f);
        private static readonly Color FarTintMul = new Color(1.12f, 1.12f, 1.18f); // 遠景は空気遠近で薄く
        private static readonly Color WindowColor = new Color(1f, 0.86f, 0.50f, 0.88f);

        private sealed class Layer
        {
            public readonly List<Transform> Buildings = new List<Transform>();
            public float Speed;
        }

        private readonly List<Layer> _layers = new List<Layer>();
        private readonly List<Material> _materials = new List<Material>();
        private CommuteDirector _director;

        private void Start()
        {
            _director = FindFirstObjectByType<CommuteDirector>();
            // 地面（車両の下～遠くまで。線路まわりの暗い帯）
            CreateGround();

            // 近景・遠景 × 左右
            foreach (int side in new[] { -1, 1 })
            {
                BuildLayer(side, _nearDistance, _nearSpeed, heightMin: 3f, heightMax: 9f, tint: Color.white);
                BuildLayer(side, _farDistance, _farSpeed, heightMin: 8f, heightMax: 20f, tint: FarTintMul);
            }
        }

        private void Update()
        {
            // 電車の疑似速度（停車で0、発車で1へ）に連動して流す
            float speed01 = _director != null ? _director.TrainSpeed01 : 1f;

            // 後方(-z)へ流し、ループ端で前方へ戻して高さを引き直す
            foreach (Layer layer in _layers)
            {
                foreach (Transform b in layer.Buildings)
                {
                    Vector3 p = b.position;
                    p.z -= layer.Speed * speed01 * Time.deltaTime;
                    if (p.z < -_loopLength * 0.5f)
                    {
                        p.z += _loopLength;
                        RerollBuilding(b);
                    }
                    b.position = p;
                }
            }
        }

        private void BuildLayer(int side, float distance, float speed, float heightMin, float heightMax, Color tint)
        {
            var layer = new Layer { Speed = speed };
            float spacing = _loopLength / _buildingsPerLayer;

            for (int i = 0; i < _buildingsPerLayer; i++)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Bldg_{side}_{distance:0}_{i}";
                go.transform.SetParent(transform, false);
                Destroy(go.GetComponent<Collider>());

                float height = Random.Range(heightMin, heightMax);
                float width = Random.Range(3f, 7f);
                float depth = Random.Range(3f, 6f);
                float z = -_loopLength * 0.5f + spacing * i + Random.Range(-spacing * 0.3f, spacing * 0.3f);
                go.transform.position = new Vector3(side * distance + Random.Range(-1.5f, 1.5f), height * 0.5f - 1f, z);
                go.transform.localScale = new Vector3(width, height, depth);

                var renderer = go.GetComponent<Renderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                Color c = BuildingColors[Random.Range(0, BuildingColors.Length)];
                c = new Color(Mathf.Min(1f, c.r * tint.r), Mathf.Min(1f, c.g * tint.g), Mathf.Min(1f, c.b * tint.b));
                Material m = renderer.material;
                m.color = c;
                if (m.HasProperty("_BaseColor"))
                {
                    m.SetColor("_BaseColor", c);
                }
                _materials.Add(m);
                AddWindows(go.transform, side, width, height, depth);

                layer.Buildings.Add(go.transform);
            }
            _layers.Add(layer);
        }

        private void AddWindows(Transform building, int side, float width, float height, float depth)
        {
            int rows = Mathf.Clamp(Mathf.FloorToInt(height / 1.1f), 2, 9);
            int cols = Mathf.Clamp(Mathf.FloorToInt(depth / 0.9f), 2, 7);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if ((r + c) % 3 == 0)
                    {
                        continue;
                    }

                    GameObject win = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    win.name = $"Win_{r}_{c}";
                    win.transform.SetParent(building, false);
                    float y = -0.36f + (r + 0.5f) / rows * 0.72f;
                    float z = -0.38f + (c + 0.5f) / cols * 0.76f;
                    win.transform.localPosition = new Vector3(-side * 0.505f, y, z);
                    win.transform.localScale = new Vector3(0.025f / width, 0.22f / height, 0.34f / depth);
                    Destroy(win.GetComponent<Collider>());

                    var renderer = win.GetComponent<Renderer>();
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    Material material = renderer.material;
                    Color color = WindowColor;
                    color.a *= Random.Range(0.55f, 1f);
                    material.color = color;
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", color);
                    }
                    _materials.Add(material);
                }
            }
        }

        /// <summary>ループで戻ってきたビルの高さだけ引き直す（色まで変えるとチカチカするので据え置き）。</summary>
        private void RerollBuilding(Transform b)
        {
            Vector3 s = b.localScale;
            float newHeight = s.y * Random.Range(0.75f, 1.3f);
            b.localScale = new Vector3(s.x, newHeight, s.z);
            Vector3 p = b.position;
            p.y = newHeight * 0.5f - 1f;
            b.position = p;
        }

        private void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "OutsideGround";
            ground.transform.SetParent(transform, false);
            Destroy(ground.GetComponent<Collider>());
            ground.transform.position = new Vector3(0f, -1.05f, 0f);
            ground.transform.localScale = new Vector3(120f, 1f, _loopLength + 40f);
            var renderer = ground.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Material m = renderer.material;
            m.color = GroundColor;
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", GroundColor);
            }
            _materials.Add(m);
        }

        private void OnDestroy()
        {
            foreach (Material m in _materials)
            {
                Destroy(m);
            }
        }
    }
}

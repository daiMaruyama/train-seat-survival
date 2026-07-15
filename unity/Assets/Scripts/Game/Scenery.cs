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
        [SerializeField] private int _buildingsPerLayer = 6; // 片側・1層あたりのビル数

        [Header("停車ホーム（軽量プリミティブ）")]
        [SerializeField] private float _stationApproachTime = 1.1f;
        [SerializeField] private float _stationApproachDistance = 54f;

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
        private readonly Dictionary<Color32, Material> _opaqueMaterials = new Dictionary<Color32, Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Material _windowMaterial;
        private CommuteDirector _director;
        private Transform _stationRoot;
        private bool _wasAtStation;
        private static int? _nextBuildingCountOverride;

        public static void OverrideNextBuildingCount(int count)
        {
            _nextBuildingCountOverride = Mathf.Max(1, count);
        }

        private void Start()
        {
            if (_nextBuildingCountOverride.HasValue)
            {
                _buildingsPerLayer = _nextBuildingCountOverride.Value;
                _nextBuildingCountOverride = null;
            }

            _director = FindFirstObjectByType<CommuteDirector>();
            // 地面（車両の下～遠くまで。線路まわりの暗い帯）
            CreateGround();

            // 近景・遠景 × 左右
            foreach (int side in new[] { -1, 1 })
            {
                BuildLayer(side, _nearDistance, _nearSpeed, heightMin: 3f, heightMax: 9f, tint: Color.white);
                BuildLayer(side, _farDistance, _farSpeed, heightMin: 8f, heightMax: 20f, tint: FarTintMul);
            }
            BuildStationPlatform();
        }

        private void Update()
        {
            bool atStation = _director != null && _director.IsAtStation;
            UpdateStationPlatform(atStation);

            // 電車の疑似速度（停車で0、発車で1へ）に連動して流す
            float speed01 = _director != null ? _director.TrainSpeed01 : 1f;
            if (speed01 <= 0.001f)
            {
                return;
            }

            // 小休止（timeScale=0）中も車窓だけは流し続ける＝「世界は止まっても電車は走っている」演出。
            // ゲームロジック（駅進行・席取り）は timeScale=0 のままなので有利不利は生まれない
            float dt = PauseMenuView.IsOpen ? Time.unscaledDeltaTime : Time.deltaTime;

            // 後方(-z)へ流し、ループ端で前方へ戻して高さを引き直す
            foreach (Layer layer in _layers)
            {
                foreach (Transform b in layer.Buildings)
                {
                    Vector3 p = b.position;
                    p.z -= layer.Speed * speed01 * dt;
                    if (p.z < -_loopLength * 0.5f)
                    {
                        p.z += _loopLength;
                        RerollBuilding(b);
                    }
                    b.position = p;
                }
            }
        }

        /// <summary>
        /// 本格的な駅アセットの代わりに、床・黄色線・壁・柱だけの軽いホームを作る。
        /// 減速開始で車両前方から滑り込み、停止と同時にドア前へ揃う。
        /// </summary>
        private void BuildStationPlatform()
        {
            _stationRoot = new GameObject("StationPlatform").transform;
            _stationRoot.SetParent(transform, false);

            Color platform = new Color(0.52f, 0.53f, 0.51f);
            Color platformEdge = new Color(0.92f, 0.73f, 0.16f);
            Color wall = new Color(0.29f, 0.34f, 0.39f);
            Color pillar = new Color(0.66f, 0.67f, 0.65f);

            foreach (int side in new[] { -1, 1 })
            {
                StationBlock($"Platform_{side}", new Vector3(side * 3.25f, -0.36f, 0f),
                    new Vector3(3.0f, 0.40f, 72f), platform);
                StationBlock($"SafetyLine_{side}", new Vector3(side * 1.82f, -0.145f, 0f),
                    new Vector3(0.12f, 0.035f, 72f), platformEdge);
                StationBlock($"StationWall_{side}", new Vector3(side * 5.35f, 1.15f, 0f),
                    new Vector3(0.16f, 2.8f, 72f), wall);

                for (int i = -4; i <= 4; i++)
                {
                    StationBlock($"Pillar_{side}_{i}", new Vector3(side * 4.62f, 1.12f, i * 8f),
                        new Vector3(0.20f, 2.7f, 0.20f), pillar);
                }

                // 壁の白い駅名標シルエット。文字は作らず、停車場と読める記号だけ足す。
                for (int i = -3; i <= 3; i += 2)
                {
                    StationBlock($"StationSign_{side}_{i}", new Vector3(side * 5.24f, 1.35f, i * 10f),
                        new Vector3(0.08f, 0.62f, 3.1f), new Color(0.91f, 0.90f, 0.84f));
                    StationBlock($"StationSignLine_{side}_{i}", new Vector3(side * 5.19f, 1.12f, i * 10f),
                        new Vector3(0.04f, 0.08f, 2.65f), new Color(0.95f, 0.45f, 0.15f));
                }
            }

            _stationRoot.gameObject.SetActive(false);
        }

        private void UpdateStationPlatform(bool atStation)
        {
            if (_stationRoot == null)
            {
                return;
            }

            if (atStation && !_wasAtStation)
            {
                _stationRoot.localPosition = new Vector3(0f, 0f, Mathf.Max(1f, _stationApproachDistance));
                _stationRoot.gameObject.SetActive(true);
            }

            if (atStation)
            {
                Vector3 p = _stationRoot.localPosition;
                float speed = Mathf.Max(1f, _stationApproachDistance) / Mathf.Max(0.1f, _stationApproachTime);
                p.z = Mathf.MoveTowards(p.z, 0f, speed * Time.deltaTime);
                _stationRoot.localPosition = p;
            }
            else if (_wasAtStation)
            {
                // ドアが閉じ、発車するフレームでしまう。開口中にホームが消えることはない。
                _stationRoot.gameObject.SetActive(false);
            }
            _wasAtStation = atStation;
        }

        private void StationBlock(string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_stationRoot, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = OpaqueMaterial(color);
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
                renderer.receiveShadows = false;
                Color c = BuildingColors[Random.Range(0, BuildingColors.Length)];
                c = new Color(Mathf.Min(1f, c.r * tint.r), Mathf.Min(1f, c.g * tint.g), Mathf.Min(1f, c.b * tint.b));
                renderer.sharedMaterial = OpaqueMaterial(c);
                AddWindows(go.transform, side, width, height, depth);

                layer.Buildings.Add(go.transform);
            }
            _layers.Add(layer);
        }

        private void AddWindows(Transform building, int side, float width, float height, float depth)
        {
            int rows = Mathf.Clamp(Mathf.FloorToInt(height / 1.5f), 2, 5);
            int cols = Mathf.Clamp(Mathf.FloorToInt(depth / 1.3f), 2, 4);
            var verts = new List<Vector3>(rows * cols * 4);
            var normals = new List<Vector3>(rows * cols * 4);
            var tris = new List<int>(rows * cols * 6);
            Vector3 normal = new Vector3(-side, 0f, 0f);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if ((r + c) % 2 == 0)
                    {
                        continue;
                    }

                    float y = -0.36f + (r + 0.5f) / rows * 0.72f;
                    float z = -0.38f + (c + 0.5f) / cols * 0.76f;
                    float halfY = 0.11f / height;
                    float halfZ = 0.17f / depth;
                    float x = -side * 0.506f;
                    int v = verts.Count;

                    verts.Add(new Vector3(x, y - halfY, z - halfZ));
                    verts.Add(new Vector3(x, y + halfY, z - halfZ));
                    verts.Add(new Vector3(x, y + halfY, z + halfZ));
                    verts.Add(new Vector3(x, y - halfY, z + halfZ));
                    normals.Add(normal);
                    normals.Add(normal);
                    normals.Add(normal);
                    normals.Add(normal);

                    tris.Add(v);
                    tris.Add(v + 1);
                    tris.Add(v + 2);
                    tris.Add(v);
                    tris.Add(v + 2);
                    tris.Add(v + 3);
                }
            }

            if (verts.Count == 0)
            {
                return;
            }

            var go = new GameObject("Windows", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(building, false);
            var mesh = new Mesh { name = "BuildingWindows" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = WindowMaterial();
        }

        private Material WindowMaterial()
        {
            if (_windowMaterial == null)
            {
                _windowMaterial = RuntimeMaterials.LitEmissive(); // テンプレ複製（エミッションのバリアントをビルドへ）
                _windowMaterial.color = WindowColor;
                if (_windowMaterial.HasProperty("_BaseColor"))
                {
                    _windowMaterial.SetColor("_BaseColor", WindowColor);
                }
                _windowMaterial.EnableKeyword("_EMISSION");
                if (_windowMaterial.HasProperty("_EmissionColor"))
                {
                    _windowMaterial.SetColor("_EmissionColor", WindowColor * 0.6f);
                }
                if (_windowMaterial.HasProperty("_Cull"))
                {
                    _windowMaterial.SetFloat("_Cull", (float)CullMode.Off);
                }
                _windowMaterial.enableInstancing = true;
            }
            return _windowMaterial;
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
            renderer.receiveShadows = false;
            renderer.sharedMaterial = OpaqueMaterial(GroundColor);
        }

        private Material OpaqueMaterial(Color color)
        {
            Color32 key = color;
            if (_opaqueMaterials.TryGetValue(key, out Material material))
            {
                return material;
            }

            material = RuntimeMaterials.Lit();
            material.name = $"SceneryLit_{key.r:X2}{key.g:X2}{key.b:X2}";
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            material.enableInstancing = true;
            _opaqueMaterials.Add(key, material);
            return material;
        }

        private void OnDestroy()
        {
            foreach (Material m in _opaqueMaterials.Values)
            {
                Destroy(m);
            }
            _opaqueMaterials.Clear();
            foreach (Mesh mesh in _meshes)
            {
                Destroy(mesh);
            }
            if (_windowMaterial != null)
            {
                Destroy(_windowMaterial);
            }
        }
    }
}

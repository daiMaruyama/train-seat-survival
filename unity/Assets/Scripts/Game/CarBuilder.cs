using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1両ぶんの車内をランタイムに組み立てる「美術層」。実車の記号（ロングシート・ドア・吊り革・
    /// 握り棒・網棚・優先席・天井）を、コミカル寄りのフラットカラーで並べる。座席ごとの
    /// <see cref="SeatAnchor"/> と視線判定トリガー（<see cref="SeatMarker"/>）、ドア前の通路点を記録する
    /// のは従来どおりで、ここを本物のアセットに差し替えてもゲーム側は変わらない。
    /// 影は落とさない設定にして、明るいフラットな絵にしている。
    /// </summary>
    public sealed class CarBuilder : MonoBehaviour
    {
        [SerializeField] private int[] _benchPattern = { 3, 7, 7, 7, 3 };
        [SerializeField] private float _seatWidth = 0.68f;   // 座り姿勢の膝の広がりがぶつからないピッチ
        [SerializeField] private float _doorWidth = 1.4f;
        [SerializeField] private float _interiorWidth = 3.4f;
        [SerializeField] private float _wallHeight = 2.4f;
        [SerializeField] private float _floorDrop = 0.16f;   // 床の上面の高さ＝-この値（Inspectorで微調整可）
        [SerializeField] private bool _spawnCoffeeCups = true;
        [SerializeField] private float _coffeeCupHeight = 0.26f; // 見つけやすい大きさ
        [SerializeField] private float _coffeeFloatY = 1.05f;    // 浮かせる高さ（胸元＝視界に入る）

        // ---- パレット ----
        private static readonly Color WallColor = new Color(0.91f, 0.90f, 0.87f);
        private static readonly Color CeilingColor = new Color(0.96f, 0.95f, 0.93f);
        private static readonly Color FloorColor = new Color(0.55f, 0.53f, 0.48f);
        private static readonly Color FloorLineColor = new Color(0.62f, 0.60f, 0.55f);
        private static readonly Color CushionColor = new Color(0.18f, 0.36f, 0.62f);
        private static readonly Color BackrestColor = new Color(0.13f, 0.27f, 0.48f);
        private static readonly Color PriorityCushion = new Color(0.48f, 0.34f, 0.66f);
        private static readonly Color PriorityBackrest = new Color(0.36f, 0.25f, 0.52f);
        private static readonly Color DoorColor = new Color(0.78f, 0.77f, 0.74f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color MetalColor = new Color(0.72f, 0.73f, 0.75f);
        private static readonly Color StrapColor = new Color(0.93f, 0.93f, 0.90f);
        private static readonly Color RackColor = new Color(0.80f, 0.80f, 0.82f);

        private readonly List<SeatAnchor> _seats = new List<SeatAnchor>();
        private readonly List<Vector3> _doors = new List<Vector3>();
        private readonly List<SeatMarker> _markers = new List<SeatMarker>();
        private sealed class DoorAssembly
        {
            public Transform LeftLeaf;
            public Transform RightLeaf;
            public Vector3 LeftClosed;
            public Vector3 RightClosed;
            public float Travel;
        }

        private readonly List<DoorAssembly> _doorAssemblies = new List<DoorAssembly>();
        // 車内プリミティブはパレット色ごとに1枚のURPマテリアルを共有する。
        // CreatePrimitive の既定マテリアルはURP Playerで非対応になり得るため使用しない。
        private readonly Dictionary<Color32, Material> _opaqueMaterials = new Dictionary<Color32, Material>();
        private static bool? _nextSpawnCoffeeOverride;

        /// <summary>車内の全座席（組み立て順）。Awake 後に有効。</summary>
        public IReadOnlyList<SeatAnchor> Seats => _seats;

        /// <summary>各ドア前の通路点（乗客が現れる／出ていく場所）。</summary>
        public IReadOnlyList<Vector3> Doors => _doors;

        /// <summary>座席インデックスと同順のマーカー（空席ハイライトの制御用）。</summary>
        public IReadOnlyList<SeatMarker> Markers => _markers;

        /// <summary>次にランタイム生成する車両だけ、コーヒー生成の有無を上書きする（タイトル用）。</summary>
        public static void OverrideNextCoffeeSpawn(bool spawn)
        {
            _nextSpawnCoffeeOverride = spawn;
        }

        /// <summary>
        /// ドアの2枚の戸を左右へ滑らせる。透明バリアは固定なので車外へは出られない。
        /// 乗降処理はこの完了後に始め、人が閉じたドアから現れる印象を防ぐ。
        /// </summary>
        public IEnumerator AnimateDoors(bool open, float duration = 0.46f)
        {
            duration = Mathf.Max(0.01f, duration);
            var leftFrom = new Vector3[_doorAssemblies.Count];
            var rightFrom = new Vector3[_doorAssemblies.Count];
            for (int i = 0; i < _doorAssemblies.Count; i++)
            {
                DoorAssembly door = _doorAssemblies[i];
                leftFrom[i] = door.LeftLeaf.localPosition;
                rightFrom[i] = door.RightLeaf.localPosition;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = t * t * (3f - 2f * t);
                for (int i = 0; i < _doorAssemblies.Count; i++)
                {
                    DoorAssembly door = _doorAssemblies[i];
                    Vector3 leftTo = door.LeftClosed + Vector3.back * (open ? door.Travel : 0f);
                    Vector3 rightTo = door.RightClosed + Vector3.forward * (open ? door.Travel : 0f);
                    door.LeftLeaf.localPosition = Vector3.Lerp(leftFrom[i], leftTo, smooth);
                    door.RightLeaf.localPosition = Vector3.Lerp(rightFrom[i], rightTo, smooth);
                }
                yield return null;
            }
        }

        private void Awake()
        {
            float length = ComputeLength();
            float halfL = length * 0.5f;
            float halfW = _interiorWidth * 0.5f;

            // 床・天井・妻面（前後の壁）。床は -_floorDrop に下げ、壁はその分だけ下へ伸ばす
            float wallCenterY = (_wallHeight - _floorDrop) * 0.5f;
            float wallSpanY = _wallHeight + _floorDrop;
            CreateBlock("Floor", new Vector3(0f, -_floorDrop - 0.05f, 0f), new Vector3(_interiorWidth, 0.1f, length), FloorColor);
            CreateBlock("FloorLine", new Vector3(0f, -_floorDrop + 0.006f, 0f), new Vector3(0.9f, 0.012f, length), FloorLineColor, withCollider: false);
            CreateBlock("Ceiling", new Vector3(0f, _wallHeight + 0.04f, 0f), new Vector3(_interiorWidth, 0.08f, length), CeilingColor);
            CreateBlock("Wall_Front", new Vector3(0f, wallCenterY, halfL), new Vector3(_interiorWidth, wallSpanY, 0.1f), WallColor);
            CreateBlock("Wall_Back", new Vector3(0f, wallCenterY, -halfL), new Vector3(_interiorWidth, wallSpanY, 0.1f), WallColor);

            BuildSide(-1, length, recordDoors: true);
            BuildSide(1, length, recordDoors: false);
            BuildHangingLine(-1, length);
            BuildHangingLine(1, length);
            bool spawnCoffee = _nextSpawnCoffeeOverride ?? _spawnCoffeeCups;
            _nextSpawnCoffeeOverride = null;
            BuildCoffeeCups(length, spawnCoffee);
        }

        /// <summary>片側ぶんの壁・ベンチ・窓・ドア・ポール・網棚・座席アンカーを並べる。</summary>
        private void BuildSide(int sign, float length, bool recordDoors)
        {
            float halfW = _interiorWidth * 0.5f;
            float wallX = sign * halfW;
            float cushionX = sign * (halfW - 0.3f);
            float backrestX = sign * (halfW - 0.12f);
            Quaternion facing = Quaternion.LookRotation(new Vector3(-sign, 0f, 0f));

            // 窓帯(1.25〜1.85)だけ開口した壁。ドア部分まで一枚壁で塞がないよう、
            // 壁はベンチ区画ごとに置く（ドアが開いた時だけ床まで抜ける）。
            const float glassBottom = 1.25f;
            const float glassTop = 1.85f;

            float z = -length * 0.5f;
            for (int s = 0; s < _benchPattern.Length; s++)
            {
                int seats = _benchPattern[s];
                float segLen = seats * _seatWidth;
                float center = z + segLen * 0.5f;

                // 両端の短いベンチ＝優先席（実車どおり車端に置く）
                bool priority = s == 0 || s == _benchPattern.Length - 1;
                Color cushion = priority ? PriorityCushion : CushionColor;
                Color backrest = priority ? PriorityBackrest : BackrestColor;

                CreateBlock($"Wall_{sign}_{s}_Low", new Vector3(wallX, (glassBottom - _floorDrop) * 0.5f, center),
                    new Vector3(0.1f, glassBottom + _floorDrop, segLen), WallColor);
                CreateBlock($"Wall_{sign}_{s}_High", new Vector3(wallX, (glassTop + _wallHeight) * 0.5f, center),
                    new Vector3(0.1f, _wallHeight - glassTop, segLen), WallColor);

                // 窓＝半透明ガラス（開口部に張る。外の景色が見える）
                CreateGlass($"Window_{sign}_{s}", new Vector3(wallX, (glassBottom + glassTop) * 0.5f, center),
                    new Vector3(0.05f, glassTop - glassBottom, segLen));
                CreateBlock($"Rack_{sign}_{s}", new Vector3(sign * (halfW - 0.28f), 1.98f, center),
                    new Vector3(0.4f, 0.025f, segLen - 0.1f), RackColor, withCollider: false);

                for (int k = 0; k < seats; k++)
                {
                    float cz = z + _seatWidth * (k + 0.5f);
                    GameObject cushionGo = CreateBlock($"Seat_{sign}_{s}_{k}", new Vector3(cushionX, 0.36f, cz),
                        new Vector3(_seatWidth * 0.9f, 0.12f, _seatWidth * 0.92f), cushion); // 幅(z)は背もたれと同じ、中心0.36
                    // 背もたれも席ごとに分割（空席ハイライトを1席単位で光らせるため）
                    GameObject backGo = CreateBlock($"Backrest_{sign}_{s}_{k}", new Vector3(backrestX, 0.75f, cz),
                        new Vector3(0.12f, 0.7f, _seatWidth * 0.92f), backrest);

                    int index = _seats.Count;
                    bool isBenchEnd = k == 0 || k == seats - 1;
                    _seats.Add(new SeatAnchor(index, new Vector3(cushionX, 0f, cz), facing, isBenchEnd));
                    CreateSeatTarget(index, new Vector3(cushionX, 0.7f, cz),
                        cushionGo.GetComponent<Renderer>(), cushion, backGo.GetComponent<Renderer>(), backrest);
                }

                // ベンチ両端の握り棒（床から天井へ）
                CreatePole(new Vector3(sign * (halfW - 0.55f), 0f, z + 0.05f));
                CreatePole(new Vector3(sign * (halfW - 0.55f), 0f, z + segLen - 0.05f));

                z += segLen;

                if (s < _benchPattern.Length - 1)
                {
                    float dz = z + _doorWidth * 0.5f;
                    // ドア＝窓部分をくり抜いた枠＋ガラス（外が見える）。壁より少し内側に配置してZファイト回避
                    float doorX = wallX - sign * 0.03f;
                    float dw = _doorWidth - 0.1f;
                    const float doorWinB = 1.25f;
                    const float doorWinT = 1.78f;
                    float doorH = 2.1f + _floorDrop;
                    float leafW = dw * 0.5f;
                    Transform leftLeaf = BuildDoorLeaf($"Door_{sign}_{s}_Left", doorX, dz - leafW * 0.5f,
                        leafW, doorWinB, doorWinT);
                    Transform rightLeaf = BuildDoorLeaf($"Door_{sign}_{s}_Right", doorX, dz + leafW * 0.5f,
                        leafW, doorWinB, doorWinT);
                    _doorAssemblies.Add(new DoorAssembly
                    {
                        LeftLeaf = leftLeaf,
                        RightLeaf = rightLeaf,
                        LeftClosed = leftLeaf.localPosition,
                        RightClosed = rightLeaf.localPosition,
                        Travel = leafW * 0.94f,
                    });

                    // 見た目の戸は滑らせるが、車外へ落ちないよう透明バリアは開口部に残す。
                    var barrier = new GameObject($"DoorBarrier_{sign}_{s}");
                    barrier.transform.SetParent(transform, false);
                    barrier.transform.localPosition = new Vector3(doorX, (2.1f - _floorDrop) * 0.5f, dz);
                    var barrierCollider = barrier.AddComponent<BoxCollider>();
                    barrierCollider.size = new Vector3(0.08f, doorH, dw);
                    CreateBlock($"DoorAccent_{sign}_{s}", new Vector3(wallX - sign * 0.03f, 2.2f, dz),
                        new Vector3(0.03f, 0.12f, _doorWidth - 0.1f), AccentOrange, withCollider: false);
                    if (recordDoors)
                    {
                        _doors.Add(new Vector3(0f, 0f, dz));
                    }
                    z += _doorWidth;
                }
            }
        }

        private Transform BuildDoorLeaf(string name, float x, float z, float width, float windowBottom, float windowTop)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false);

            GameObject low = CreateBlock("Lower", new Vector3(x, (windowBottom - _floorDrop) * 0.5f, z),
                new Vector3(0.06f, windowBottom + _floorDrop, width - 0.02f), DoorColor, withCollider: false);
            low.transform.SetParent(root, true);
            GameObject high = CreateBlock("Upper", new Vector3(x, (windowTop + 2.1f) * 0.5f, z),
                new Vector3(0.06f, 2.1f - windowTop, width - 0.02f), DoorColor, withCollider: false);
            high.transform.SetParent(root, true);
            GameObject glass = CreateGlass("Glass", new Vector3(x, (windowBottom + windowTop) * 0.5f, z),
                new Vector3(0.04f, windowTop - windowBottom, width - 0.11f));
            glass.transform.SetParent(root, true);

            // 中央合わせと外縁の細い枠で、2枚戸として読めるようにする。
            GameObject rimA = CreateBlock("RimA", new Vector3(x - 0.006f, (2.1f - _floorDrop) * 0.5f, z - width * 0.5f + 0.025f),
                new Vector3(0.075f, 2.1f + _floorDrop, 0.05f), DoorColor, withCollider: false);
            rimA.transform.SetParent(root, true);
            GameObject rimB = CreateBlock("RimB", new Vector3(x - 0.006f, (2.1f - _floorDrop) * 0.5f, z + width * 0.5f - 0.025f),
                new Vector3(0.075f, 2.1f + _floorDrop, 0.05f), DoorColor, withCollider: false);
            rimB.transform.SetParent(root, true);
            return root;
        }

        /// <summary>通路の左右に走る吊り革のライン（レール＋等間隔の吊り革）。</summary>
        private void BuildHangingLine(int sign, float length)
        {
            float x = sign * 0.55f;
            float railY = 1.95f;

            GameObject rail = CreateCylinder($"HandRail_{sign}", new Vector3(x, railY, 0f),
                Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, length * 0.5f - 0.2f, 0.035f), MetalColor);

            const float spacing = 0.9f;
            int count = Mathf.FloorToInt((length - 0.8f) / spacing);
            float start = -(count - 1) * spacing * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float z = start + spacing * i;
                // 支点（レール取り付け点）を親にして、帯と輪をぶら下げる＝振り子として揺らせる
                var pivot = new GameObject($"Strap_{sign}_{i}");
                pivot.transform.SetParent(transform, false);
                pivot.transform.localPosition = new Vector3(x, railY, z);
                pivot.AddComponent<StrapSway>(); // ポーズ中・停車中も揺れ続ける（unscaled駆動）

                GameObject band = CreateBlock($"StrapBand_{sign}_{i}", new Vector3(x, railY - 0.09f, z),
                    new Vector3(0.03f, 0.14f, 0.01f), StrapColor, withCollider: false);
                band.transform.SetParent(pivot.transform, true);
                // 輪っか＝本物と同じ穴あきリング（トーラスを手続き生成）
                GameObject ring = CreateStrapRing($"StrapRing_{sign}_{i}", new Vector3(x, railY - 0.20f, z));
                ring.transform.SetParent(pivot.transform, true);
            }
        }

        private Mesh _strapRingMesh;
        private Material _strapRingMaterial;
        private Material _glassMaterial;

        /// <summary>半透明ガラス板（コライダー無し）。マテリアルは1枚を全ガラスで共有。</summary>
        private GameObject CreateGlass(string glassName, Vector3 position, Vector3 size)
        {
            if (_glassMaterial == null)
            {
                var m = RuntimeMaterials.Glass(); // テンプレ複製（ビルドへ透明バリアントを確実に含める）
                m.SetFloat("_Surface", 1f); // Transparent
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                Color c = new Color(0.72f, 0.86f, 0.95f, 0.20f);
                m.color = c;
                if (m.HasProperty("_BaseColor"))
                {
                    m.SetColor("_BaseColor", c);
                }
                _glassMaterial = m;
            }

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = glassName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.sharedMaterial = _glassMaterial;
            return go;
        }

        /// <summary>吊り革の輪。プリミティブにトーラスは無いのでメッシュを一度だけ生成して全輪で共有する。</summary>
        private GameObject CreateStrapRing(string ringName, Vector3 position)
        {
            if (_strapRingMesh == null)
            {
                _strapRingMesh = BuildTorusMesh(0.0275f, 0.010f, 20, 10); // 外径≒従来の円盤(0.075)と同じ
                _strapRingMaterial = RuntimeMaterials.Lit();
                _strapRingMaterial.color = StrapColor;
                if (_strapRingMaterial.HasProperty("_BaseColor"))
                {
                    _strapRingMaterial.SetColor("_BaseColor", StrapColor);
                }
            }

            var go = new GameObject(ringName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // リング面を通路向きに
            go.AddComponent<MeshFilter>().sharedMesh = _strapRingMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _strapRingMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>トーラス（ドーナツ）メッシュ生成。radius=輪の半径、tube=管の太さ。</summary>
        private static Mesh BuildTorusMesh(float radius, float tube, int segments, int sides)
        {
            var verts = new Vector3[(segments + 1) * (sides + 1)];
            var norms = new Vector3[verts.Length];
            var tris = new int[segments * sides * 6];

            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments * Mathf.PI * 2f;
                Vector3 center = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u)) * radius;
                for (int j = 0; j <= sides; j++)
                {
                    float v = (float)j / sides * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                    int idx = i * (sides + 1) + j;
                    verts[idx] = center + dir * tube;
                    norms[idx] = dir;
                }
            }

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j;
                    int b = a + sides + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
                }
            }

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.triangles = tris;
            return mesh;
        }

        private void CreatePole(Vector3 floorPos)
        {
            CreateCylinder($"Pole_{floorPos.z:0.0}_{floorPos.x:0.0}",
                new Vector3(floorPos.x, (_wallHeight - _floorDrop) * 0.5f, floorPos.z),
                Quaternion.identity, new Vector3(0.045f, (_wallHeight + _floorDrop) * 0.5f, 0.045f), MetalColor,
                withCollider: true);
        }

        private void BuildCoffeeCups(float length, bool spawn)
        {
            if (!spawn)
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>("CoffeeCup/Coffee_Cup");
            if (prefab == null)
            {
                Debug.LogWarning("CoffeeCup model was not found at Resources/CoffeeCup/Coffee_Cup.");
                return;
            }

            // ドア前の通路に胸の高さで浮かせる（見下ろし不要・歩けば自然に視界へ入る）
            for (int i = 0; i < _doors.Count; i++)
            {
                PlaceCoffeeCup(prefab, i, new Vector3(0f, _coffeeFloatY, _doors[i].z), i * 70f);
            }
        }

        private void PlaceCoffeeCup(GameObject prefab, int index, Vector3 floorPosition, float yaw)
        {
            GameObject go = Instantiate(prefab, transform);
            go.name = $"CoffeeCupItem_{index}";
            go.transform.localPosition = floorPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            UpgradeCoffeeMaterials(go);
            NormalizeCoffeeSize(go);
            SetCoffeeCenter(go, floorPosition); // 指定位置を見た目の中心に合わせる（浮遊配置）
            EnsureCoffeeItemCollider(go);
        }

        private void NormalizeCoffeeSize(GameObject go)
        {
            if (_coffeeCupHeight <= 0f || !TryGetRenderBounds(go, out Bounds bounds) || bounds.size.y <= 0.0001f)
            {
                return;
            }

            float scale = _coffeeCupHeight / bounds.size.y;
            go.transform.localScale *= scale;
        }

        private static void SetCoffeeCenter(GameObject go, Vector3 targetLocal)
        {
            if (!TryGetRenderBounds(go, out Bounds bounds))
            {
                return;
            }

            // モデルのピボットずれを吸収し、見た目の中心を狙った位置へ
            go.transform.localPosition += targetLocal - bounds.center;
        }

        private static void EnsureCoffeeItemCollider(GameObject go)
        {
            if (go.GetComponent<CoffeeCupItem>() == null)
            {
                go.AddComponent<CoffeeCupItem>();
            }

            if (!TryGetRenderBounds(go, out Bounds bounds))
            {
                return;
            }

            var box = go.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = go.AddComponent<BoxCollider>();
            }

            Vector3 lossy = go.transform.lossyScale;
            box.isTrigger = true; // 触れたら自動で飲む（CoffeeCupItem.OnTriggerEnter）
            box.center = go.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Max(0.12f, bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x))),
                Mathf.Max(0.12f, bounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y))),
                Mathf.Max(0.12f, bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)))) * 1.6f;
        }

        private static bool TryGetRenderBounds(GameObject go, out Bounds bounds)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return found;
        }

        private static void UpgradeCoffeeMaterials(GameObject go)
        {
            // URP変換は共通実装へ（テンプレ .mat 経由＝ビルドでもピンクにならない）
            ItemBeacon.EnsureUrpMaterials(go);
        }

        /// <summary>座席を視線で狙うための見えないトリガー（isTrigger なので歩行は邪魔しない）。</summary>
        private void CreateSeatTarget(int index, Vector3 position,
            Renderer cushionRenderer, Color cushionBase, Renderer backrestRenderer, Color backrestBase)
        {
            var go = new GameObject($"SeatTarget_{index}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.5f, 1.4f, 0.6f);

            var marker = go.AddComponent<SeatMarker>();
            marker.Init(index, cushionRenderer, cushionBase, backrestRenderer, backrestBase);
            _markers.Add(marker);
        }

        private float ComputeLength()
        {
            float benches = 0f;
            foreach (int seats in _benchPattern)
            {
                benches += seats * _seatWidth;
            }
            int doors = Mathf.Max(0, _benchPattern.Length - 1);
            return benches + doors * _doorWidth;
        }

        private GameObject CreateBlock(string blockName, Vector3 position, Vector3 size, Color color, bool withCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(go, blockName, position, Quaternion.identity, size, color, withCollider);
            return go;
        }

        private GameObject CreateCylinder(string cylName, Vector3 position, Quaternion rotation, Vector3 size, Color color, bool withCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(go, cylName, position, rotation, size, color, withCollider);
            return go;
        }

        private void Setup(GameObject go, string goName, Vector3 position, Quaternion rotation, Vector3 size, Color color, bool withCollider)
        {
            go.name = goName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;

            if (!withCollider)
            {
                Destroy(go.GetComponent<Collider>());
            }

            var renderer = go.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off; // 明るいフラットな絵にする
            renderer.sharedMaterial = OpaqueMaterial(color);
        }

        private Material OpaqueMaterial(Color color)
        {
            Color32 key = color;
            if (_opaqueMaterials.TryGetValue(key, out Material material))
            {
                return material;
            }

            material = RuntimeMaterials.Lit();
            material.name = $"CarLit_{key.r:X2}{key.g:X2}{key.b:X2}";
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
            foreach (Material material in _opaqueMaterials.Values)
            {
                Destroy(material);
            }
            _opaqueMaterials.Clear();

            if (_glassMaterial != null)
            {
                Destroy(_glassMaterial);
            }
            if (_strapRingMaterial != null)
            {
                Destroy(_strapRingMaterial);
            }
            if (_strapRingMesh != null)
            {
                Destroy(_strapRingMesh);
            }
        }
    }
}

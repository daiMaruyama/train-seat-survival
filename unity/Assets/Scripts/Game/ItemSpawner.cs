using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内アイテム（コーヒー＝回復／データメガネ＝情報／ダッシュ靴＝加速）をプールで管理する生成器。
    /// ・prefab・個数・大きさは Inspector からシリアライズして差し替え可能
    /// ・日の開始時に1〜2個、その後は毎駅ドア脇へ1個ずつ補充する
    /// ・未取得品はその日の間は残し、「空席を狙うか、アイテムへ寄るか」の選択を作る
    /// モデルの寸法はバウンズから自動正規化し、当たり判定と拾い挙動は生成時に付与する。
    /// </summary>
    public sealed class ItemSpawner : MonoBehaviour
    {
        public enum ItemKind { Coffee, Glasses, Shoes }

        [System.Serializable]
        public sealed class Pool
        {
            public string label = "Coffee";
            public GameObject prefab;
            [Min(0)] public int count = 4;
            public float targetSize = 0.26f;      // 見た目の最大寸法（ここへ正規化）
            public Vector3 modelEuler;            // 置き向きの微調整
            public ItemKind kind = ItemKind.Coffee;
        }

        [Header("プール（prefab をアサインしてね。靴は未設定ならプリミティブで代用）")]
        [SerializeField] private Pool _coffee = new() { label = "Coffee", count = 2, targetSize = 0.26f, kind = ItemKind.Coffee };
        [SerializeField] private Pool _glasses = new() { label = "Glasses", count = 2, targetSize = 0.34f, kind = ItemKind.Glasses };
        [SerializeField] private Pool _shoes = new() { label = "DashShoes", count = 2, targetSize = 0.30f, kind = ItemKind.Shoes };

        [Header("駅到着時の出現（ドア前・高さ固定）")]
        [SerializeField] private float _floatHeight = 1.05f;             // 胸元の高さ
        [SerializeField] private float _doorSideOffset = 0.68f;         // ドア中央を塞がず、左右へ寄せる
        [SerializeField] private float _doorZJitter = 0.18f;
        [SerializeField, Min(0)] private int _initialSpawnMin = 1;
        [SerializeField, Min(1)] private int _initialSpawnMax = 2;
        [SerializeField, Min(1)] private int _spawnEveryStations = 1;    // 毎駅1個補充
        [SerializeField, Min(1)] private int _maxSpawnsPerDay = 6;
        [SerializeField, Min(1)] private int _maxActiveItems = 4;        // 未取得品の溜まり過ぎを防ぐ
        [SerializeField] private float _minPlayerDistance = 1.8f;       // 足元へ突然生えない距離
        [SerializeField] private bool _despawnOnDoorClose = false;

        private CommuteDirector _director;
        private CarBuilder _car;
        private Transform _player;
        private readonly List<GameObject> _all = new();
        private readonly List<GameObject> _spawnQueue = new();
        private readonly List<GameObject> _activeItems = new();
        private int _lastLeg = -1;
        private int _spawnedThisDay;
        private bool _ready;

        /// <summary>いま駅のドア脇に受け取れるアイテムが出ているか（HUD通知用）。</summary>
        public bool HasActiveItem
        {
            get
            {
                RemoveInactiveItems();
                return _activeItems.Count > 0;
            }
        }

        private void Start()
        {
            StartCoroutine(BuildWhenReady());
        }

        private IEnumerator BuildWhenReady()
        {
            _director = FindFirstObjectByType<CommuteDirector>();
            _car = FindFirstObjectByType<CarBuilder>();
            // 車両（座席）が組み上がるまで待つ
            while (_director == null || _director.SeatCount == 0)
            {
                _director = _director != null ? _director : FindFirstObjectByType<CommuteDirector>();
                _car = _car != null ? _car : FindFirstObjectByType<CarBuilder>();
                yield return null;
            }

            BuildPool(_coffee);
            BuildPool(_glasses);
            BuildPool(_shoes);
            _player = FindFirstObjectByType<StaminaSystem>()?.transform;
            _director.StationReached += HandleStationReached;
            _director.DoorsClosed += HandleDoorsClosed;
            _lastLeg = _director.Leg;
            PrepareDay();
            _ready = true;
            SpawnInitialItems();
        }

        private void Update()
        {
            if (!_ready || _director == null)
            {
                return;
            }
            if (_director.Leg != _lastLeg)
            {
                _lastLeg = _director.Leg;
                PrepareDay();
                SpawnInitialItems();
            }
        }

        private void OnDestroy()
        {
            if (_director != null)
            {
                _director.StationReached -= HandleStationReached;
                _director.DoorsClosed -= HandleDoorsClosed;
            }
        }

        private void BuildPool(Pool pool)
        {
            if (pool == null || pool.count <= 0)
            {
                return;
            }
            bool canFallback = pool.kind == ItemKind.Shoes;
            if (pool.prefab == null && !canFallback)
            {
                Debug.LogWarning($"ItemSpawner: '{pool.label}' の prefab が未設定のためスキップします。");
                return;
            }

            for (int i = 0; i < pool.count; i++)
            {
                var root = new GameObject($"{pool.label}_{i}");
                root.transform.SetParent(transform, false);

                GameObject model;
                if (pool.prefab != null)
                {
                    model = Instantiate(pool.prefab, root.transform);
                    FlattenSkinnedMeshes(model); // NPC衣装系アセット対策（スキンのままだとバウンズが身長分になる）
                    KeepOnlyHighestLod(model);   // LODGroupを持たない小物化なので、放っておくと全LODが重なって描画される
                    ItemBeacon.EnsureUrpMaterials(model); // 真っピンク（マテリアル欠損）を防ぐ
                }
                else
                {
                    // 美術が来るまでのプリミティブ代用（prefab を挿せばそちらが優先される）
                    model = BuildFallbackModel(pool.kind, root.transform);
                }
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(pool.modelEuler);
                NormalizeModel(root.transform, model, pool.targetSize);

                var col = root.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.34f;

                switch (pool.kind)
                {
                    case ItemKind.Coffee: root.AddComponent<CoffeeCupItem>(); break;
                    case ItemKind.Shoes: root.AddComponent<DashShoesItem>(); break;
                    default: root.AddComponent<GlassesItem>(); break;
                }

                root.SetActive(false);
                _all.Add(root);
            }
        }

        /// <summary>
        /// スキンメッシュを素の MeshFilter+MeshRenderer へ差し替える。NPC衣装用アセット（靴など）は
        /// 人体リグにスキンされていてバウンズが身長サイズになるため、そのままだと正規化（寸法合わせ）が狂う。
        /// バインドポーズのメッシュを直接使えば小物として正しい寸法で扱える。
        /// </summary>
        private static void FlattenSkinnedMeshes(GameObject model)
        {
            foreach (SkinnedMeshRenderer skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                GameObject go = skinned.gameObject;
                Mesh mesh = skinned.sharedMesh;
                Material[] materials = skinned.sharedMaterials;
                DestroyImmediate(skinned); // 直後にバウンズを測るので即時破棄
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
        }

        /// <summary>
        /// 「〜_lod1」以降のレンダラーを非表示にして最高品質（lod0）だけ残す。
        /// LOD付きFBXをただ Instantiate すると全段が同時に描画され、無駄とZファイトの元になる。
        /// </summary>
        private static void KeepOnlyHighestLod(GameObject model)
        {
            foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
            {
                string n = renderer.name.ToLowerInvariant();
                int at = n.LastIndexOf("_lod", System.StringComparison.Ordinal);
                if (at >= 0 && at + 4 < n.Length && char.IsDigit(n[at + 4]) && n[at + 4] != '0')
                {
                    renderer.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>prefab 未設定アイテムのプリミティブ見た目（靴っぽいL字ブロック）。コライダーは付けない。</summary>
        private static GameObject BuildFallbackModel(ItemKind kind, Transform parent)
        {
            var model = new GameObject("Visual");
            model.transform.SetParent(parent, false);

            // 甲＋つま先の2ブロックで「靴」を象る（本番Prefab未設定時だけ使う簡易表示）
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(model.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.14f, -0.1f);
            body.transform.localScale = new Vector3(0.3f, 0.28f, 0.42f);

            GameObject toe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(toe.GetComponent<Collider>());
            toe.transform.SetParent(model.transform, false);
            toe.transform.localPosition = new Vector3(0f, 0.06f, 0.22f);
            toe.transform.localScale = new Vector3(0.3f, 0.12f, 0.35f);

            var m = RuntimeMaterials.Lit();
            body.GetComponent<Renderer>().sharedMaterial = m;
            toe.GetComponent<Renderer>().sharedMaterial = m;
            return model;
        }

        /// <summary>モデルの最大寸法を targetSize へ正規化し、中心を root 原点へ合わせる（浮遊配置用）。</summary>
        private static void NormalizeModel(Transform root, GameObject model, float targetSize)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (maxDim > 1e-4f)
            {
                model.transform.localScale *= targetSize / maxDim;
            }

            // 正規化後に中心を測り直し、root 原点に一致させる
            renderers = model.GetComponentsInChildren<Renderer>();
            Bounds b2 = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                b2.Encapsulate(renderers[i].bounds);
            }
            model.transform.position -= b2.center - root.position;
        }

        /// <summary>新しい日は全品を一旦隠し、出現順だけシャッフルする。</summary>
        private void PrepareDay()
        {
            _activeItems.Clear();
            _spawnedThisDay = 0;
            _spawnQueue.Clear();
            foreach (GameObject item in _all)
            {
                if (item == null) continue;
                item.SetActive(false);
            }

            // 3枠の日はコーヒーだけに偏らず、回復・情報・加速を1回ずつ候補にする。
            QueueFirstOfType<CoffeeCupItem>();
            QueueFirstOfType<GlassesItem>();
            QueueFirstOfType<DashShoesItem>();
            for (int i = _spawnQueue.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_spawnQueue[i], _spawnQueue[j]) = (_spawnQueue[j], _spawnQueue[i]);
            }

            // 各種類の代表を並べた後ろへ余剰分を足す。上限を増やした場合だけ使われる。
            foreach (GameObject item in _all)
            {
                if (item != null && !_spawnQueue.Contains(item))
                {
                    _spawnQueue.Add(item);
                }
            }
        }

        private void QueueFirstOfType<T>() where T : Component
        {
            foreach (GameObject item in _all)
            {
                if (item != null && item.GetComponent<T>() != null)
                {
                    _spawnQueue.Add(item);
                    return;
                }
            }
        }

        private void HandleStationReached(int station)
        {
            if (!_ready || station <= 0 || _spawnedThisDay >= Mathf.Max(1, _maxSpawnsPerDay))
            {
                return;
            }

            int cadence = Mathf.Max(1, _spawnEveryStations);
            if ((station - 1) % cadence != 0)
            {
                return;
            }
            RemoveInactiveItems();
            if (_activeItems.Count < Mathf.Max(1, _maxActiveItems))
            {
                SpawnNextAtDoor(playSound: true);
            }
        }

        private void HandleDoorsClosed()
        {
            if (!_despawnOnDoorClose)
            {
                return;
            }
            for (int i = 0; i < _activeItems.Count; i++)
            {
                if (_activeItems[i] != null)
                {
                    _activeItems[i].SetActive(false);
                }
            }
            _activeItems.Clear();
        }

        private void SpawnInitialItems()
        {
            int min = Mathf.Clamp(_initialSpawnMin, 0, _spawnQueue.Count);
            int max = Mathf.Clamp(Mathf.Max(min, _initialSpawnMax), min, _spawnQueue.Count);
            int count = max > min ? Random.Range(min, max + 1) : min;
            count = Mathf.Min(count, Mathf.Max(1, _maxActiveItems));
            for (int i = 0; i < count; i++)
            {
                SpawnNextAtDoor(playSound: false);
            }
        }

        private void RemoveInactiveItems()
        {
            for (int i = _activeItems.Count - 1; i >= 0; i--)
            {
                GameObject item = _activeItems[i];
                if (item == null || !item.activeSelf)
                {
                    _activeItems.RemoveAt(i);
                }
            }
        }

        private void SpawnNextAtDoor(bool playSound)
        {
            if (_spawnQueue.Count == 0 || _car == null || _car.Doors.Count == 0)
            {
                return;
            }

            GameObject item = _spawnQueue[0];
            _spawnQueue.RemoveAt(0);
            _spawnedThisDay++;

            Vector3 door = PickDoorAwayFromPlayer();
            // 初期2個が同じドア脇に重ならないよう、既存品と遠い側を優先する。
            float side = PickDoorSide(door);
            Vector3 carLocal = door + new Vector3(side * _doorSideOffset, _floatHeight,
                Random.Range(-_doorZJitter, _doorZJitter));
            Vector3 world = _car.transform.TransformPoint(carLocal);

            // Activeのまま動かすとアイテム側のボブ基準が古いままなので、位置決定後にEnableする。
            item.SetActive(false);
            item.transform.localPosition = transform.InverseTransformPoint(world);
            item.SetActive(true);
            _activeItems.Add(item);
            if (playSound)
            {
                GameAudio.Instance.Play(GameAudio.Sfx.Ding, 1.18f);
            }
        }

        private float PickDoorSide(Vector3 door)
        {
            Vector3 left = _car.transform.TransformPoint(door + Vector3.left * _doorSideOffset);
            Vector3 right = _car.transform.TransformPoint(door + Vector3.right * _doorSideOffset);
            float leftNearest = NearestActiveDistance(left);
            float rightNearest = NearestActiveDistance(right);
            if (Mathf.Abs(leftNearest - rightNearest) < 0.05f)
            {
                return Random.value < 0.5f ? -1f : 1f;
            }
            return leftNearest > rightNearest ? -1f : 1f;
        }

        private float NearestActiveDistance(Vector3 world)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < _activeItems.Count; i++)
            {
                GameObject active = _activeItems[i];
                if (active != null && active.activeSelf)
                {
                    nearest = Mathf.Min(nearest, Vector3.Distance(world, active.transform.position));
                }
            }
            return nearest;
        }

        private Vector3 PickDoorAwayFromPlayer()
        {
            IReadOnlyList<Vector3> doors = _car.Doors;
            if (_player == null)
            {
                _player = FindFirstObjectByType<StaminaSystem>()?.transform;
            }

            var candidates = new List<Vector3>();
            for (int i = 0; i < doors.Count; i++)
            {
                Vector3 world = _car.transform.TransformPoint(doors[i]);
                if (_player == null || Vector3.Distance(world, _player.position) >= _minPlayerDistance)
                {
                    candidates.Add(doors[i]);
                }
            }

            IReadOnlyList<Vector3> source = candidates.Count > 0 ? candidates : doors;
            return source[Random.Range(0, source.Count)];
        }
    }
}

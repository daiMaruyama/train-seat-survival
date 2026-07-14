using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内アイテム（コーヒー＝回復／データメガネ＝情報／ダッシュ靴＝加速）をプールで管理する生成器。
    /// ・prefab・個数・大きさは Inspector からシリアライズして差し替え可能
    /// ・毎回作り直さずプールを使い回し、配置だけ「プレイヤーが取れる通路上」でランダム化（高さは固定）
    /// ・消費されたアイテムは非表示になり、日替わり（Leg 更新）で位置を撒き直して再表示＝それ以上増えない
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
        [SerializeField] private Pool _glasses = new() { label = "Glasses", count = 1, targetSize = 0.34f, kind = ItemKind.Glasses };
        [SerializeField] private Pool _shoes = new() { label = "DashShoes", count = 1, targetSize = 0.30f, kind = ItemKind.Shoes };

        [Header("配置（プレイヤーが取れる通路上・高さ固定）")]
        [SerializeField] private float _floatHeight = 1.05f;             // 浮遊高さ（胸元＝視界に入る）
        [SerializeField] private Vector2 _aisleX = new(-0.4f, 0.4f);     // 通路の左右の振れ幅
        [SerializeField] private float _endMargin = 1.4f;               // 車端から内側へ空ける距離
        [SerializeField] private float _doorZJitter = 0.28f;            // ドア中央への重なりを避ける前後幅
        [SerializeField] private float _minSpacing = 1.1f;              // アイテム同士の最小間隔
        [SerializeField] private bool _respawnEachDay = true;           // 日替わりで撒き直す

        private CommuteDirector _director;
        private CarBuilder _car;
        private readonly List<GameObject> _all = new();
        private readonly HashSet<GameObject> _oncePerRun = new(); // ラン中1回きり（消費後は日替わりでも復活しない）
        private bool _placedOnce;
        private float _zMin;
        private float _zMax;
        private int _lastLeg = -1;
        private bool _ready;

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

            ComputeAisleRange();
            BuildPool(_coffee);
            BuildPool(_glasses);
            BuildPool(_shoes);
            RepositionAll();
            _lastLeg = _director.Leg;
            _ready = true;
        }

        private void Update()
        {
            if (!_ready || !_respawnEachDay || _director == null)
            {
                return;
            }
            if (_director.Leg != _lastLeg)
            {
                _lastLeg = _director.Leg;
                RepositionAll(); // 新しい日：全アイテムを撒き直して再表示（消費済みも復活）
            }
        }

        private void ComputeAisleRange()
        {
            _zMin = float.MaxValue;
            _zMax = float.MinValue;
            for (int i = 0; i < _director.SeatCount; i++)
            {
                float z = _director.GetSeat(i).Position.z;
                _zMin = Mathf.Min(_zMin, z);
                _zMax = Mathf.Max(_zMax, z);
            }
            if (_zMin > _zMax)
            {
                _zMin = -2f;
                _zMax = 2f;
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
                if (pool.kind == ItemKind.Shoes)
                {
                    _oncePerRun.Add(root); // 強アイテムはラン中1足だけ
                }
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

        private void RepositionAll()
        {
            bool firstPlacement = !_placedOnce;
            _placedOnce = true;

            var placed = new List<Vector3>();
            foreach (GameObject item in _all)
            {
                if (item == null)
                {
                    continue;
                }
                // ラン1回きりのアイテムは、消費済み（非表示）なら日替わりでも復活させない
                if (!firstPlacement && _oncePerRun.Contains(item) && !item.activeSelf)
                {
                    continue;
                }
                // 偶数番はドア前、奇数番は通路へ。どちらも中央通路内・同じ高さに限定する。
                bool preferDoor = placed.Count % 2 == 0;
                Vector3 pos = PickReachableSpot(placed, preferDoor);
                placed.Add(pos);
                // Activeのまま位置だけ変えると各アイテムが保持するボブ基準座標が更新されない。
                // 必ず再Enableして、全種類を同じ高さ・新しい位置から動かす。
                item.SetActive(false);
                item.transform.localPosition = pos;
                item.SetActive(true); // OnEnable で消費フラグ・基準位置がリセットされる
            }
        }

        /// <summary>ドア前または通路上から、高さ固定で既存アイテムと近すぎない一点を選ぶ。</summary>
        private Vector3 PickReachableSpot(List<Vector3> placed, bool preferDoor)
        {
            float zLo = _zMin + _endMargin;
            float zHi = _zMax - _endMargin;
            if (zLo > zHi)
            {
                float mid = (_zMin + _zMax) * 0.5f;
                zLo = zHi = mid;
            }

            Vector3 best = Candidate(preferDoor, zLo, zHi);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // ドア前が混んでいたら後半は通路へ逃がし、重なりを作らない。
                bool useDoor = preferDoor && attempt < 10;
                Vector3 candidate = Candidate(useDoor, zLo, zHi);
                bool ok = true;
                foreach (Vector3 p in placed)
                {
                    if (Vector3.Distance(candidate, p) < _minSpacing)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    return candidate;
                }
                best = candidate;
            }
            return best;
        }

        private Vector3 Candidate(bool useDoor, float zLo, float zHi)
        {
            float z;
            if (useDoor && _car != null && _car.Doors.Count > 0)
            {
                Vector3 door = _car.Doors[Random.Range(0, _car.Doors.Count)];
                z = Mathf.Clamp(door.z + Random.Range(-_doorZJitter, _doorZJitter), zLo, zHi);
            }
            else
            {
                z = Random.Range(zLo, zHi);
            }
            return new Vector3(Random.Range(_aisleX.x, _aisleX.y), _floatHeight, z);
        }
    }
}

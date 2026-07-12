using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 車内アイテム（コーヒー＝回復／データメガネ＝情報）をプールで管理する生成器。
    /// ・prefab・個数・大きさは Inspector からシリアライズして差し替え可能（コーヒーは初期4つ等）
    /// ・毎回作り直さずプールを使い回し、配置だけ「プレイヤーが取れる通路上」でランダム化（高さは固定）
    /// ・消費されたアイテムは非表示になり、日替わり（Leg 更新）で位置を撒き直して再表示＝それ以上増えない
    /// モデルの寸法はバウンズから自動正規化し、当たり判定と拾い挙動は生成時に付与する。
    /// </summary>
    public sealed class ItemSpawner : MonoBehaviour
    {
        public enum ItemKind { Coffee, Glasses, EnergyDrink, Moretsu }

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

        [Header("プール（prefab をアサインしてね。エナドリ/モーレツは未設定ならプリミティブで代用）")]
        [SerializeField] private Pool _coffee = new() { label = "Coffee", count = 4, targetSize = 0.26f, kind = ItemKind.Coffee };
        [SerializeField] private Pool _glasses = new() { label = "Glasses", count = 1, targetSize = 0.34f, kind = ItemKind.Glasses };
        [SerializeField] private Pool _energyDrink = new() { label = "EnergyDrink", count = 1, targetSize = 0.24f, kind = ItemKind.EnergyDrink };
        [SerializeField] private Pool _moretsu = new() { label = "Moretsu", count = 1, targetSize = 0.20f, kind = ItemKind.Moretsu };

        [Header("配置（プレイヤーが取れる通路上・高さ固定）")]
        [SerializeField] private float _floatHeight = 1.05f;             // 浮遊高さ（胸元＝視界に入る）
        [SerializeField] private Vector2 _aisleX = new(-0.4f, 0.4f);     // 通路の左右の振れ幅
        [SerializeField] private float _endMargin = 1.4f;               // 車端から内側へ空ける距離
        [SerializeField] private float _minSpacing = 1.1f;              // アイテム同士の最小間隔
        [SerializeField] private bool _respawnEachDay = true;           // 日替わりで撒き直す

        private CommuteDirector _director;
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
            // 車両（座席）が組み上がるまで待つ
            while (_director == null || _director.SeatCount == 0)
            {
                _director = _director != null ? _director : FindFirstObjectByType<CommuteDirector>();
                yield return null;
            }

            ComputeAisleRange();
            BuildPool(_coffee);
            BuildPool(_glasses);
            BuildPool(_energyDrink);
            BuildPool(_moretsu);
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
            bool canFallback = pool.kind == ItemKind.EnergyDrink || pool.kind == ItemKind.Moretsu;
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
                    case ItemKind.EnergyDrink: root.AddComponent<EnergyDrinkItem>(); break;
                    case ItemKind.Moretsu: root.AddComponent<MoretsuDrinkItem>(); break;
                    default: root.AddComponent<GlassesItem>(); break;
                }

                root.SetActive(false);
                _all.Add(root);
                if (pool.kind == ItemKind.EnergyDrink || pool.kind == ItemKind.Moretsu)
                {
                    _oncePerRun.Add(root); // 強アイテムはラン中1本だけ
                }
            }
        }

        /// <summary>prefab 未設定アイテムのプリミティブ見た目（缶／小瓶）。コライダーは付けない。</summary>
        private static GameObject BuildFallbackModel(ItemKind kind, Transform parent)
        {
            var model = new GameObject("Visual");
            model.transform.SetParent(parent, false);

            if (kind == ItemKind.EnergyDrink)
            {
                // 金の缶（円柱）＋銀のプルタブ面
                GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(can.GetComponent<Collider>());
                can.transform.SetParent(model.transform, false);
                can.transform.localScale = new Vector3(0.5f, 0.62f, 0.5f);
                var m = RuntimeMaterials.Lit();
                m.color = new Color(0.95f, 0.78f, 0.22f);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.95f, 0.78f, 0.22f));
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.75f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
                can.GetComponent<Renderer>().sharedMaterial = m;
            }
            else
            {
                // 茶褐色の小瓶（カプセル）＋金のキャップ
                GameObject bottle = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(bottle.GetComponent<Collider>());
                bottle.transform.SetParent(model.transform, false);
                bottle.transform.localScale = new Vector3(0.45f, 0.5f, 0.45f);
                var m = RuntimeMaterials.Lit();
                var brown = new Color(0.42f, 0.2f, 0.1f);
                m.color = brown;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", brown);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.65f);
                bottle.GetComponent<Renderer>().sharedMaterial = m;

                GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(model.transform, false);
                cap.transform.localPosition = new Vector3(0f, 0.52f, 0f);
                cap.transform.localScale = new Vector3(0.22f, 0.1f, 0.22f);
                var cm = RuntimeMaterials.Lit();
                var gold = new Color(0.9f, 0.75f, 0.3f);
                cm.color = gold;
                if (cm.HasProperty("_BaseColor")) cm.SetColor("_BaseColor", gold);
                if (cm.HasProperty("_Metallic")) cm.SetFloat("_Metallic", 0.7f);
                cap.GetComponent<Renderer>().sharedMaterial = cm;
            }
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
                Vector3 pos = PickReachableSpot(placed);
                placed.Add(pos);
                item.transform.localPosition = pos;
                item.SetActive(true); // OnEnable で消費フラグ・基準位置がリセットされる
            }
        }

        /// <summary>通路上・高さ固定で、既存アイテムと近すぎない一点を選ぶ（数回の棄却サンプリング）。</summary>
        private Vector3 PickReachableSpot(List<Vector3> placed)
        {
            float zLo = _zMin + _endMargin;
            float zHi = _zMax - _endMargin;
            if (zLo > zHi)
            {
                float mid = (_zMin + _zMax) * 0.5f;
                zLo = zHi = mid;
            }

            Vector3 best = new Vector3(Random.Range(_aisleX.x, _aisleX.y), _floatHeight, Random.Range(zLo, zHi));
            for (int attempt = 0; attempt < 20; attempt++)
            {
                var candidate = new Vector3(Random.Range(_aisleX.x, _aisleX.y), _floatHeight, Random.Range(zLo, zHi));
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
    }
}

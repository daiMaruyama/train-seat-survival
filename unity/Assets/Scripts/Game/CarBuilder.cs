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
        [SerializeField] private float _seatWidth = 0.46f;
        [SerializeField] private float _doorWidth = 1.4f;
        [SerializeField] private float _interiorWidth = 3.2f;
        [SerializeField] private float _wallHeight = 2.4f;

        // ---- パレット（明るいフラットカラー。中央線イメージのオレンジを差し色に）----
        private static readonly Color WallColor = new Color(0.91f, 0.90f, 0.87f);
        private static readonly Color CeilingColor = new Color(0.96f, 0.95f, 0.93f);
        private static readonly Color FloorColor = new Color(0.55f, 0.53f, 0.48f);
        private static readonly Color FloorLineColor = new Color(0.62f, 0.60f, 0.55f);
        private static readonly Color CushionColor = new Color(0.18f, 0.36f, 0.62f);
        private static readonly Color BackrestColor = new Color(0.13f, 0.27f, 0.48f);
        private static readonly Color PriorityCushion = new Color(0.48f, 0.34f, 0.66f);
        private static readonly Color PriorityBackrest = new Color(0.36f, 0.25f, 0.52f);
        private static readonly Color WindowColor = new Color(0.66f, 0.83f, 0.94f);
        private static readonly Color DoorColor = new Color(0.78f, 0.77f, 0.74f);
        private static readonly Color DoorWindowColor = new Color(0.70f, 0.86f, 0.95f);
        private static readonly Color AccentOrange = new Color(0.95f, 0.45f, 0.15f);
        private static readonly Color MetalColor = new Color(0.72f, 0.73f, 0.75f);
        private static readonly Color StrapColor = new Color(0.93f, 0.93f, 0.90f);
        private static readonly Color RackColor = new Color(0.80f, 0.80f, 0.82f);

        private readonly List<SeatAnchor> _seats = new List<SeatAnchor>();
        private readonly List<Vector3> _doors = new List<Vector3>();

        /// <summary>車内の全座席（組み立て順）。Awake 後に有効。</summary>
        public IReadOnlyList<SeatAnchor> Seats => _seats;

        /// <summary>各ドア前の通路点（乗客が現れる／出ていく場所）。</summary>
        public IReadOnlyList<Vector3> Doors => _doors;

        private void Awake()
        {
            float length = ComputeLength();
            float halfL = length * 0.5f;
            float halfW = _interiorWidth * 0.5f;

            // 床・天井・妻面（前後の壁）
            CreateBlock("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(_interiorWidth, 0.1f, length), FloorColor);
            CreateBlock("FloorLine", new Vector3(0f, 0.005f, 0f), new Vector3(0.9f, 0.012f, length), FloorLineColor, withCollider: false);
            CreateBlock("Ceiling", new Vector3(0f, _wallHeight + 0.04f, 0f), new Vector3(_interiorWidth, 0.08f, length), CeilingColor);
            CreateBlock("Wall_Front", new Vector3(0f, _wallHeight * 0.5f, halfL), new Vector3(_interiorWidth, _wallHeight, 0.1f), WallColor);
            CreateBlock("Wall_Back", new Vector3(0f, _wallHeight * 0.5f, -halfL), new Vector3(_interiorWidth, _wallHeight, 0.1f), WallColor);

            BuildSide(-1, length, recordDoors: true);
            BuildSide(1, length, recordDoors: false);
            BuildHangingLine(-1, length);
            BuildHangingLine(1, length);
        }

        /// <summary>片側ぶんの壁・ベンチ・窓・ドア・ポール・網棚・座席アンカーを並べる。</summary>
        private void BuildSide(int sign, float length, bool recordDoors)
        {
            float halfW = _interiorWidth * 0.5f;
            float wallX = sign * halfW;
            float cushionX = sign * (halfW - 0.3f);
            float backrestX = sign * (halfW - 0.12f);
            Quaternion facing = Quaternion.LookRotation(new Vector3(-sign, 0f, 0f));

            CreateBlock($"Wall_{sign}", new Vector3(wallX, _wallHeight * 0.5f, 0f), new Vector3(0.1f, _wallHeight, length), WallColor);

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

                CreateBlock($"Backrest_{sign}_{s}", new Vector3(backrestX, 0.75f, center),
                    new Vector3(0.12f, 0.7f, segLen), backrest);
                CreateBlock($"Window_{sign}_{s}", new Vector3(wallX - sign * 0.03f, 1.55f, center),
                    new Vector3(0.05f, 0.6f, segLen - 0.15f), WindowColor, withCollider: false);
                CreateBlock($"Rack_{sign}_{s}", new Vector3(sign * (halfW - 0.28f), 1.98f, center),
                    new Vector3(0.4f, 0.025f, segLen - 0.1f), RackColor, withCollider: false);

                for (int k = 0; k < seats; k++)
                {
                    float cz = z + _seatWidth * (k + 0.5f);
                    GameObject cushionGo = CreateBlock($"Seat_{sign}_{s}_{k}", new Vector3(cushionX, 0.4f, cz),
                        new Vector3(_seatWidth * 0.9f, 0.12f, 0.5f), cushion);

                    int index = _seats.Count;
                    _seats.Add(new SeatAnchor(index, new Vector3(cushionX, 0f, cz), facing));
                    CreateSeatTarget(index, new Vector3(cushionX, 0.7f, cz), cushionGo.GetComponent<Renderer>(), cushion);
                }

                // ベンチ両端の握り棒（床から天井へ）
                CreatePole(new Vector3(sign * (halfW - 0.55f), 0f, z + 0.05f));
                CreatePole(new Vector3(sign * (halfW - 0.55f), 0f, z + segLen - 0.05f));

                z += segLen;

                if (s < _benchPattern.Length - 1)
                {
                    float dz = z + _doorWidth * 0.5f;
                    CreateBlock($"Door_{sign}_{s}", new Vector3(wallX, 1.05f, dz),
                        new Vector3(0.06f, 2.1f, _doorWidth - 0.1f), DoorColor);
                    CreateBlock($"DoorWindow_{sign}_{s}", new Vector3(wallX - sign * 0.035f, 1.45f, dz),
                        new Vector3(0.03f, 0.6f, _doorWidth * 0.6f), DoorWindowColor, withCollider: false);
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
                CreateBlock($"StrapBand_{sign}_{i}", new Vector3(x, railY - 0.09f, z),
                    new Vector3(0.03f, 0.14f, 0.01f), StrapColor, withCollider: false);
                // 輪っかは薄い円盤で代用（スタイライズ表現）
                CreateCylinder($"StrapRing_{sign}_{i}", new Vector3(x, railY - 0.20f, z),
                    Quaternion.Euler(0f, 0f, 90f), new Vector3(0.075f, 0.006f, 0.075f), StrapColor);
            }
        }

        private void CreatePole(Vector3 floorPos)
        {
            CreateCylinder($"Pole_{floorPos.z:0.0}_{floorPos.x:0.0}",
                new Vector3(floorPos.x, _wallHeight * 0.5f, floorPos.z),
                Quaternion.identity, new Vector3(0.045f, _wallHeight * 0.5f, 0.045f), MetalColor,
                withCollider: true);
        }

        /// <summary>座席を視線で狙うための見えないトリガー（isTrigger なので歩行は邪魔しない）。</summary>
        private void CreateSeatTarget(int index, Vector3 position, Renderer cushionRenderer, Color baseColor)
        {
            var go = new GameObject($"SeatTarget_{index}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.5f, 1.4f, 0.6f);

            go.AddComponent<SeatMarker>().Init(index, cushionRenderer, baseColor);
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
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }
    }
}

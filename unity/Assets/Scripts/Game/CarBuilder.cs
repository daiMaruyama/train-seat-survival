using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 1両ぶんの車内をランタイムに「箱」で組み立てる（グレーボックス）。実車のように壁沿いの
    /// ロングシートをドアで区切る（既定 3-7-7-7-3 ＝片側27席・両側54席・ドア4枚）。床や壁は当たり判定
    /// を残し、通路を歩けるようにする。組み立て中に座席ごとの <see cref="SeatAnchor"/> と、各座席を
    /// 視線で狙うためのトリガー（<see cref="SeatMarker"/>）、ドア前の通路点を記録する。これらが
    /// 「美術↔ロジック」の接点なので、後で本物のアセットに差し替えてもゲーム側は変えずに済む。
    /// （車内は実行中に変化しないのでプール不要。プールが効くのは乗り降りする乗客の方。）
    /// </summary>
    public sealed class CarBuilder : MonoBehaviour
    {
        [SerializeField] private int[] _benchPattern = { 3, 7, 7, 7, 3 };
        [SerializeField] private float _seatWidth = 0.46f;
        [SerializeField] private float _doorWidth = 1.4f;
        [SerializeField] private float _interiorWidth = 3.2f;
        [SerializeField] private float _wallHeight = 2.4f;

        private static readonly Color FloorColor = new Color(0.15f, 0.16f, 0.19f);
        private static readonly Color WallColor = new Color(0.58f, 0.60f, 0.64f);
        private static readonly Color CushionColor = new Color(0.20f, 0.42f, 0.72f);
        private static readonly Color BackrestColor = new Color(0.14f, 0.28f, 0.52f);
        private static readonly Color WindowColor = new Color(0.62f, 0.80f, 0.92f);
        private static readonly Color DoorColor = new Color(0.30f, 0.34f, 0.40f);

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

            CreateBlock("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(_interiorWidth, 0.1f, length), FloorColor);
            CreateBlock("Wall_L", new Vector3(-halfW, _wallHeight * 0.5f, 0f), new Vector3(0.1f, _wallHeight, length), WallColor);
            CreateBlock("Wall_R", new Vector3(halfW, _wallHeight * 0.5f, 0f), new Vector3(0.1f, _wallHeight, length), WallColor);
            CreateBlock("Wall_Front", new Vector3(0f, _wallHeight * 0.5f, halfL), new Vector3(_interiorWidth, _wallHeight, 0.1f), WallColor);
            CreateBlock("Wall_Back", new Vector3(0f, _wallHeight * 0.5f, -halfL), new Vector3(_interiorWidth, _wallHeight, 0.1f), WallColor);

            BuildSide(-1, length, recordDoors: true);
            BuildSide(1, length, recordDoors: false);
        }

        /// <summary>片側ぶんのベンチ・窓・ドア・座席アンカー・判定トリガーを並べる。</summary>
        private void BuildSide(int sign, float length, bool recordDoors)
        {
            float halfW = _interiorWidth * 0.5f;
            float wallX = sign * halfW;
            float cushionX = sign * (halfW - 0.3f);
            float backrestX = sign * (halfW - 0.12f);
            Quaternion facing = Quaternion.LookRotation(new Vector3(-sign, 0f, 0f));

            float z = -length * 0.5f;
            for (int s = 0; s < _benchPattern.Length; s++)
            {
                int seats = _benchPattern[s];
                float segLen = seats * _seatWidth;
                float center = z + segLen * 0.5f;

                CreateBlock($"Backrest_{sign}_{s}", new Vector3(backrestX, 0.75f, center),
                    new Vector3(0.12f, 0.7f, segLen), BackrestColor);
                CreateBlock($"Window_{sign}_{s}", new Vector3(wallX, 1.55f, center),
                    new Vector3(0.06f, 0.55f, segLen - 0.1f), WindowColor);

                for (int k = 0; k < seats; k++)
                {
                    float cz = z + _seatWidth * (k + 0.5f);
                    GameObject cushion = CreateBlock($"Seat_{sign}_{s}_{k}", new Vector3(cushionX, 0.4f, cz),
                        new Vector3(_seatWidth * 0.9f, 0.12f, 0.5f), CushionColor);

                    int index = _seats.Count;
                    _seats.Add(new SeatAnchor(index, new Vector3(cushionX, 0f, cz), facing));
                    CreateSeatTarget(index, new Vector3(cushionX, 0.7f, cz), cushion.GetComponent<Renderer>());
                }

                z += segLen;

                if (s < _benchPattern.Length - 1)
                {
                    float dz = z + _doorWidth * 0.5f;
                    CreateBlock($"Door_{sign}_{s}", new Vector3(wallX, 1.05f, dz),
                        new Vector3(0.05f, 2.1f, _doorWidth - 0.1f), DoorColor);
                    if (recordDoors)
                    {
                        _doors.Add(new Vector3(0f, 0f, dz));
                    }
                    z += _doorWidth;
                }
            }
        }

        /// <summary>
        /// 座席を視線で狙うための、見えないトリガー判定を置く。歩行を邪魔しないよう isTrigger にし、
        /// クッションより少し通路側まで包むことで、レイが先に当たって確実にこの席だと分かるようにする。
        /// </summary>
        private void CreateSeatTarget(int index, Vector3 position, Renderer cushionRenderer)
        {
            var go = new GameObject($"SeatTarget_{index}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.5f, 1.4f, 0.6f);

            go.AddComponent<SeatMarker>().Init(index, cushionRenderer, CushionColor);
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

        private GameObject CreateBlock(string blockName, Vector3 position, Vector3 size, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = blockName;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;

            var renderer = go.GetComponent<Renderer>();
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            return go;
        }
    }
}

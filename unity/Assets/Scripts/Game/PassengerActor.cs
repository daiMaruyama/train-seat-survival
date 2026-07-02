using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客の体（ミニフィグ）。胴カプセル＋頭＋髪の3パーツで、服・肌・髪の色をランダムに着せ替えて
    /// 通勤客の群れを出す（コミカルなチビ頭身）。座り／立ちはパーツの高さで表現する。
    /// 移動は従来どおり：短い経路（降車なら 席→通路→ドア、乗車なら ドア→通路→席）を歩き、
    /// 到着時にコールバックを呼ぶ。ルートの位置 y は足元＝床である前提。
    /// </summary>
    public sealed class PassengerActor : MonoBehaviour
    {
        [SerializeField] private float _speed = 2.6f;

        // ---- 着せ替えパレット（コミカル寄りのフラットカラー）----
        private static readonly Color[] OutfitColors =
        {
            new Color(0.16f, 0.20f, 0.32f), // 紺スーツ
            new Color(0.25f, 0.25f, 0.28f), // チャコール
            new Color(0.45f, 0.32f, 0.24f), // ブラウン
            new Color(0.55f, 0.58f, 0.62f), // グレー
            new Color(0.20f, 0.33f, 0.25f), // 深緑
            new Color(0.72f, 0.45f, 0.50f), // くすみピンク
            new Color(0.35f, 0.50f, 0.65f), // 水色ジャケット
            new Color(0.80f, 0.68f, 0.40f), // マスタード
        };

        private static readonly Color[] SkinColors =
        {
            new Color(0.96f, 0.80f, 0.68f),
            new Color(0.90f, 0.72f, 0.58f),
            new Color(0.76f, 0.57f, 0.44f),
        };

        private static readonly Color[] HairColors =
        {
            new Color(0.12f, 0.10f, 0.10f), // 黒
            new Color(0.25f, 0.17f, 0.12f), // 焦げ茶
            new Color(0.45f, 0.32f, 0.20f), // 茶
            new Color(0.65f, 0.65f, 0.66f), // 白髪
        };

        private Transform _body;
        private Transform _head;
        private Transform _hair;
        private Renderer _bodyRenderer;
        private Renderer _headRenderer;
        private Renderer _hairRenderer;

        private readonly Queue<Vector3> _path = new Queue<Vector3>();
        private Action _onArrive;
        private bool _moving;

        /// <summary>パーツを組み立てる（プール生成時に1回だけ呼ぶ）。</summary>
        public void BuildBody()
        {
            _body = CreatePart(PrimitiveType.Capsule, "Body", out _bodyRenderer);
            _head = CreatePart(PrimitiveType.Sphere, "Head", out _headRenderer);
            _hair = CreatePart(PrimitiveType.Sphere, "Hair", out _hairRenderer);
            SetSeated(false);
        }

        /// <summary>服・肌・髪をランダムに着せ替える（プールから取り出すたびに呼ぶと群衆に見える）。</summary>
        public void RandomizeLook()
        {
            SetColor(_bodyRenderer, OutfitColors[UnityEngine.Random.Range(0, OutfitColors.Length)]);
            SetColor(_headRenderer, SkinColors[UnityEngine.Random.Range(0, SkinColors.Length)]);
            SetColor(_hairRenderer, HairColors[UnityEngine.Random.Range(0, HairColors.Length)]);
        }

        /// <summary>座り／立ちのポーズ切り替え（パーツの高さと胴の縮みで表現）。</summary>
        public void SetSeated(bool seated)
        {
            if (_body == null)
            {
                return;
            }

            if (seated)
            {
                _body.localScale = new Vector3(0.36f, 0.26f, 0.36f);
                _body.localPosition = new Vector3(0f, 0.26f, 0f);
                _head.localPosition = new Vector3(0f, 0.70f, 0f);
                _hair.localPosition = new Vector3(0f, 0.82f, -0.05f);
            }
            else
            {
                // 立ち姿は座り客（座面のぶん高い）より頭が上に来る高さにする。
                _body.localScale = new Vector3(0.36f, 0.56f, 0.36f);
                _body.localPosition = new Vector3(0f, 0.56f, 0f);
                _head.localPosition = new Vector3(0f, 1.30f, 0f);
                _hair.localPosition = new Vector3(0f, 1.41f, -0.05f);
            }
        }

        public void Snap(Vector3 position, Quaternion rotation)
        {
            _path.Clear();
            _onArrive = null;
            _moving = false;
            transform.SetPositionAndRotation(position, rotation);
        }

        public void Travel(IReadOnlyList<Vector3> waypoints, Action onArrive)
        {
            _path.Clear();
            foreach (Vector3 w in waypoints)
            {
                _path.Enqueue(w);
            }
            _onArrive = onArrive;
            _moving = _path.Count > 0;
        }

        private void Update()
        {
            if (!_moving)
            {
                return;
            }

            Vector3 target = _path.Peek();
            Vector3 to = target - transform.position;
            float distance = to.magnitude;
            float step = _speed * Time.deltaTime;

            if (distance <= step)
            {
                transform.position = target;
                _path.Dequeue();
                if (_path.Count == 0)
                {
                    _moving = false;
                    Action callback = _onArrive;
                    _onArrive = null;
                    callback?.Invoke();
                }
                return;
            }

            transform.position += to / distance * step;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat);
            }
        }

        private Transform CreatePart(PrimitiveType type, string partName, out Renderer renderer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(transform, false);
            Destroy(go.GetComponent<Collider>());
            renderer = go.GetComponent<Renderer>();

            if (type == PrimitiveType.Sphere)
            {
                go.transform.localScale = partName == "Hair"
                    ? new Vector3(0.25f, 0.13f, 0.26f) // 髪＝つぶした球をのせるだけ（頭より少し小さく）
                    : new Vector3(0.28f, 0.28f, 0.28f);
            }
            return go.transform;
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }
    }
}

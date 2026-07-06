using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客の体（「着席ブレンド版」＝評判の良かった構成）。
    /// ・立ち＝Stand ステート（速度0）で静止／歩き＝Walk ループ（切替は即時）
    /// ・座り＝歩いて来た乗客は SitDown（腰を下ろす動きの後半）を再生してから Sit（速度0・90%姿勢）で静止。
    /// 　初期配置などは即 Sit。腕は LateUpdate で「肩→肘やや外→手は膝の上」の六角形補正（終盤にブレンドイン）
    /// ・接地＝生成時にボーン実測（立ち：足首→床／座り：腰を座面中央・足首を床）＋ Director から渡される
    /// 　微調整オフセット（Scale/StandY/SitY/SeatForward は Car の Inspector で編集可能）
    /// </summary>
    public sealed class PassengerActor : MonoBehaviour
    {
        [SerializeField] private float _speed = 2.6f;
        [SerializeField] private float _sitStartTime = 0.75f; // 着席モーションの再生開始点(0..1)
        [SerializeField] private float _sitTime = 0.9f;       // Sit で静止させる再生位置(0..1)

        // 座り姿勢の腕補正（総当たり最適化で決定）："六角形"
        private const float SitLean = 8f;
        private static readonly Vector3 SitArmL = new Vector3(20f, -10f, 10f);
        private static readonly Vector3 SitArmR = new Vector3(20f, 10f, -20f);   // 右腕は左のミラーでは合わない（右手が左膝側へ食い込む）ため個別に最適化した値
        private static readonly Vector3 SitForearmL = new Vector3(-42f, 45f, 0f);
        private static readonly Vector3 SitForearmR = new Vector3(-60f, -15f, 0f);

        // ---- Director（Car の Inspector）から注入されるチューニング ----
        /// <summary>身長スケール（素のモデル約2.5m→0.72で約1.8m）。</summary>
        public float BaseScale { get; set; } = 0.72f;
        /// <summary>立ち姿勢の上下微調整（＋で浮く）。</summary>
        public float StandYOffset { get; set; }
        /// <summary>座り姿勢の上下微調整（＋で浮く）。</summary>
        public float SitYOffset { get; set; }
        /// <summary>座面中心から通路側へ尻を寄せる量。</summary>
        public float SeatForward { get; set; } = 0.15f;

        private Transform _visual;
        private Animator _animator;
        private Transform _spine, _hips, _foot, _armL, _armR, _foreL, _foreR;
        private float _scale = 0.72f;
        private Vector3 _sitAlignUnit;   // 座りポーズの腰x/z・足首yのズレ（スケール1あたり、生成時に実測）
        private float _standAlignUnitY;  // 立ちポーズの足首の浮き（スケール1あたり）

        private bool _seated;
        private bool _walking;
        private bool _sittingDown;
        private float _sitPoseWeight;
        private float _swayPhase;

        private readonly Queue<Vector3> _path = new Queue<Vector3>();
        private Action _onArrive;
        private bool _moving;

        /// <summary>体を組み立てる（プール生成時に1回だけ）。プレハブが無ければカプセルで代用。</summary>
        public void BuildBody(GameObject visualPrefab)
        {
            if (visualPrefab != null)
            {
                GameObject go = Instantiate(visualPrefab, transform);
                go.name = "Visual";
                _visual = go.transform;
                _scale = BaseScale;
                _visual.localScale = Vector3.one * _scale;
                _animator = go.GetComponent<Animator>();
                if (_animator != null)
                {
                    _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // 画面外でも姿勢を書き続ける
                }
                CacheBones();
                CaptureAlignments();
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Visual";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                _visual = go.transform;
            }

            _swayPhase = UnityEngine.Random.value * 10f;
            SetSeated(false);
        }

        /// <summary>群衆の個体差：身長を少し変える。</summary>
        public void RandomizeLook()
        {
            _scale = BaseScale * UnityEngine.Random.Range(0.94f, 1.05f);
            if (_visual == null)
            {
                return;
            }
            _visual.localScale = Vector3.one * _scale;
            if (_seated)
            {
                ApplySitAlignment();
            }
            else
            {
                ApplyStandAlignment();
            }
        }

        /// <summary>座り／立ちの切り替え。instant=false なら腰を下ろすモーションを再生してから静止。</summary>
        public void SetSeated(bool seated, bool instant = true)
        {
            _seated = seated;
            _walking = false;
            _sittingDown = false;
            if (_visual == null)
            {
                return;
            }

            if (seated)
            {
                ApplySitAlignment();
                if (instant || _animator == null)
                {
                    _sitPoseWeight = 1f;
                    if (_animator != null)
                    {
                        _animator.Play("Sit", 0, _sitTime);
                        _animator.Update(0.0001f);
                    }
                }
                else
                {
                    _sittingDown = true;
                    _sitPoseWeight = 0f;
                    _animator.CrossFade("SitDown", 0.25f, 0, _sitStartTime);
                }
            }
            else
            {
                _sitPoseWeight = 0f;
                ApplyStandAlignment();
                if (_animator != null)
                {
                    _animator.Play("Stand", 0, 0f); // 速度0ステートなので姿勢は一切ズレない
                }
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
            if (_moving)
            {
                Step();
            }

            if (_animator == null)
            {
                return;
            }

            // 着席モーション：座り切ったら Sit（静止）へ。腕補正は終盤にかけて効かせる
            if (_sittingDown)
            {
                AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("SitDown"))
                {
                    _sitPoseWeight = Mathf.Clamp01((st.normalizedTime - 0.82f) / 0.15f);
                    if (st.normalizedTime >= 0.97f)
                    {
                        _sittingDown = false;
                        _sitPoseWeight = 1f;
                        // 終端(0.97)と静止姿勢(0.9)は微妙に違うので、切らずにブレンドで馴染ませる
                        _animator.CrossFade("Sit", 0.2f, 0, _sitTime);
                    }
                }
                return;
            }

            if (_seated)
            {
                return;
            }

            // 歩き出し／立ち止まり（このバージョンは即時切替）
            if (_moving && !_walking)
            {
                _walking = true;
                _animator.Play("Walk", 0, UnityEngine.Random.value); // 位相をずらして行進を防ぐ
            }
            else if (!_moving && _walking)
            {
                _walking = false;
                _animator.Play("Stand", 0, 0f);
            }
        }

        private void LateUpdate()
        {
            // Animator の書き込みの後に、揺れと座り補正を重ねる（毎フレーム基準から掛けるので蓄積しない）
            if (_spine == null)
            {
                return;
            }

            float t = Time.time + _swayPhase;
            float sway = Mathf.Sin(t * 2.1f) * 1.2f + Mathf.Sin(t * 0.8f) * 0.8f;
            float w = _sitPoseWeight;

            if (w > 0.001f)
            {
                _spine.localRotation *= Quaternion.Euler(SitLean * w + sway * 0.5f, 0f, sway * 0.7f);
                _armL.localRotation *= Quaternion.Euler(SitArmL * w);
                _armR.localRotation *= Quaternion.Euler(SitArmR * w);
                _foreL.localRotation *= Quaternion.Euler(SitForearmL * w);
                _foreR.localRotation *= Quaternion.Euler(SitForearmR * w);
            }
            else
            {
                _spine.localRotation *= Quaternion.Euler(sway * 0.5f, 0f, sway * 0.7f);
            }
        }

        /// <summary>生成時に立ち・座りポーズを一度サンプリングし、ボーンの位置ズレ（スケール1あたり）を実測する。</summary>
        private void CaptureAlignments()
        {
            if (_animator == null || _hips == null || _foot == null)
            {
                return;
            }

            _animator.Play("Sit", 0, _sitTime);
            _animator.Update(0.0001f);
            Vector3 hipsLocal = transform.InverseTransformPoint(_hips.position);
            Vector3 footLocal = transform.InverseTransformPoint(_foot.position);
            _sitAlignUnit = new Vector3(-hipsLocal.x, -footLocal.y, -hipsLocal.z) / _scale;

            _animator.Play("Stand", 0, 0f);
            _animator.Update(0.0001f);
            _standAlignUnitY = transform.InverseTransformPoint(_foot.position).y / _scale;
        }

        /// <summary>立ち姿勢：足首の浮きぶんだけ下げ、微調整 StandYOffset を足す。</summary>
        private void ApplyStandAlignment()
        {
            _visual.localPosition = new Vector3(0f, -_standAlignUnitY * _scale + StandYOffset, 0f);
        }

        /// <summary>座り姿勢：足首を床へ・腰を座面中央へ、微調整 SitYOffset／SeatForward を足す。</summary>
        private void ApplySitAlignment()
        {
            _visual.localPosition = _sitAlignUnit * _scale + new Vector3(0f, SitYOffset, SeatForward);
        }

        private void Step()
        {
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

        private void CacheBones()
        {
            foreach (Transform t in _visual.GetComponentsInChildren<Transform>())
            {
                switch (t.name)
                {
                    case "spine": _hips = t; break;
                    case "spine.001": _spine = t; break;
                    case "foot.L": _foot = t; break;
                    case "upper_arm.L": _armL = t; break;
                    case "upper_arm.R": _armR = t; break;
                    case "forearm.L": _foreL = t; break;
                    case "forearm.R": _foreR = t; break;
                }
            }
            if (_spine == null || _hips == null || _foot == null
                || _armL == null || _armR == null || _foreL == null || _foreR == null)
            {
                Debug.LogWarning("[PassengerActor] 想定ボーン（Rigify名）が見つからないため姿勢調整を無効化", this);
                _spine = null;
            }
        }
    }
}

using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 各座席に付く「判定マーカー」。視線レイキャストの当たり判定（どの席か＝<see cref="Index"/>）に加え、
    /// 座席の見た目状態を持つ：空席なら座面＋背もたれを常時light-up（座れる場所が一目で分かる）、
    /// さらに視線で狙っている席は別の色で強調する。
    /// </summary>
    public sealed class SeatMarker : MonoBehaviour
    {
        private static readonly Color TargetColor = new Color(1f, 0.85f, 0.2f);   // 狙っている席
        private static readonly Color AvailableTint = new Color(0.55f, 1f, 0.75f); // 空席の淡い発光色
        // HDRを少しだけ使い、色ベタ塗りにせず「発光」として認識できる明るさにする。
        private static readonly Color IntelGlow = new Color(0.4f, 0.82f, 1.3f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Renderer _cushion;
        private Renderer _backrest;
        private MaterialPropertyBlock _cushionBlock;
        private MaterialPropertyBlock _backrestBlock;
        private Color _cushionBase;
        private Color _backrestBase;

        private bool _available;
        private bool _targeted;
        private bool _intel;        // データメガネ中の降車予測ハイライト
        private float _intelStrength;

        public int Index { get; private set; }

        public void Init(int index, Renderer cushion, Color cushionBase, Renderer backrest, Color backrestBase)
        {
            Index = index;
            _cushion = cushion;
            _backrest = backrest;
            _cushionBase = cushionBase;
            _backrestBase = backrestBase;
            _cushionBlock = new MaterialPropertyBlock();
            _backrestBlock = new MaterialPropertyBlock();
            EnableEmission(_cushion);
            EnableEmission(_backrest);
        }

        /// <summary>空席（座れる）かどうか。Director が毎フレーム状態を流し込む。</summary>
        public void SetAvailable(bool on)
        {
            if (_available == on)
            {
                return;
            }
            _available = on;
            Apply();
        }

        /// <summary>視線で狙われているか。PlayerSit が付け外しする。</summary>
        public void SetTargeted(bool on)
        {
            if (_targeted == on)
            {
                return;
            }
            _targeted = on;
            Apply();
        }

        /// <summary>
        /// データメガネの降車予測発光（次駅で空く席が対象）。元色は変えず、発光の強さだけを受け取る。
        /// </summary>
        public void SetIntel(bool on, float strength)
        {
            strength = Mathf.Clamp01(strength);
            if (_intel == on && (!on || Mathf.Approximately(_intelStrength, strength)))
            {
                return;
            }
            _intel = on;
            _intelStrength = strength;
            Apply();
        }

        private void Apply()
        {
            if (_targeted)
            {
                SetVisual(TargetColor, TargetColor, Color.black);
            }
            else if (_intel)
            {
                // メガネのスキャン中：元色を保ち、次駅で空く席へ青白光を足す。
                SetVisual(_cushionBase, _backrestBase, IntelGlow * _intelStrength);
            }
            else if (_available)
            {
                // 元色を明るい緑寄りに引っ張る（優先席の紫でも「空いてる」と分かる）
                SetVisual(Color.Lerp(_cushionBase, AvailableTint, 0.6f),
                    Color.Lerp(_backrestBase, AvailableTint, 0.5f), Color.black);
            }
            else
            {
                SetVisual(_cushionBase, _backrestBase, Color.black);
            }
        }

        private void SetVisual(Color cushionColor, Color backrestColor, Color emission)
        {
            SetVisual(_cushion, _cushionBlock, cushionColor, emission);
            SetVisual(_backrest, _backrestBlock, backrestColor, emission * 0.72f);
        }

        private static void EnableEmission(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }
            Material material = renderer.sharedMaterial;
            if (material != null && material.HasProperty(EmissionColorId))
            {
                // 発光色自体はRendererごとのPropertyBlockで渡す。共有Materialはキーワードだけ有効化する。
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, Color.black);
            }
        }

        private static void SetVisual(Renderer renderer, MaterialPropertyBlock block, Color color, Color emission)
        {
            if (renderer == null || block == null)
            {
                return;
            }
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            block.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(block);
        }
    }
}

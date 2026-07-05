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

        private Renderer _cushion;
        private Renderer _backrest;
        private Color _cushionBase;
        private Color _backrestBase;

        private bool _available;
        private bool _targeted;

        public int Index { get; private set; }

        public void Init(int index, Renderer cushion, Color cushionBase, Renderer backrest, Color backrestBase)
        {
            Index = index;
            _cushion = cushion;
            _backrest = backrest;
            _cushionBase = cushionBase;
            _backrestBase = backrestBase;
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

        private void Apply()
        {
            if (_targeted)
            {
                Tint(TargetColor, TargetColor);
            }
            else if (_available)
            {
                // 元色を明るい緑寄りに引っ張る（優先席の紫でも「空いてる」と分かる）
                Tint(Color.Lerp(_cushionBase, AvailableTint, 0.6f), Color.Lerp(_backrestBase, AvailableTint, 0.5f));
            }
            else
            {
                Tint(_cushionBase, _backrestBase);
            }
        }

        private void Tint(Color cushionColor, Color backrestColor)
        {
            SetColor(_cushion, cushionColor);
            SetColor(_backrest, backrestColor);
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }
            Material material = renderer.material;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }
    }
}

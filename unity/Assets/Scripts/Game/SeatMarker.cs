using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 各座席に付く「判定マーカー」。視線レイキャストが当たった席がどれか（<see cref="Index"/>）を返し、
    /// 狙われている空席の座面の色を変えてプレイヤーに「ここに座れる」と伝える。
    /// 当たり判定はこのマーカーが持つトリガーで取り、見た目はクッションの Renderer を借りて点灯させる。
    /// </summary>
    public sealed class SeatMarker : MonoBehaviour
    {
        private static readonly Color HighlightColor = new Color(1f, 0.85f, 0.2f);

        private Renderer _cushion;
        private Color _baseColor;

        public int Index { get; private set; }

        public void Init(int index, Renderer cushion, Color baseColor)
        {
            Index = index;
            _cushion = cushion;
            _baseColor = baseColor;
        }

        /// <summary>狙われている間だけ座面を点灯させる。</summary>
        public void SetHighlighted(bool on)
        {
            if (_cushion == null)
            {
                return;
            }

            Color color = on ? HighlightColor : _baseColor;
            Material material = _cushion.material;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }
    }
}

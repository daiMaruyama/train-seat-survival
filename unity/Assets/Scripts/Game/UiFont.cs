using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// UI 全体で使うフォントの一元管理。Resources/Fonts/GameFont（現在は07にくまるフォント）を読み、
    /// 無い環境（フォント未導入のクローン等）では Unity 内蔵フォントにフォールバックする。
    /// フォントを差し替えたいときはファイルを置き換えるだけでよい。
    /// </summary>
    public static class UiFont
    {
        private static Font _cached;

        public static Font Load()
        {
            if (_cached == null)
            {
                _cached = Resources.Load<Font>("Fonts/GameFont");
                if (_cached == null)
                {
                    _cached = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }
            return _cached;
        }
    }
}

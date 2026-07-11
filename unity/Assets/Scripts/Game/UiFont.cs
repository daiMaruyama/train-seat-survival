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
        private static Font _led;
        private static bool _ledLoaded;

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

        /// <summary>
        /// LED電光掲示板用のドットフォント（Resources/Fonts/LedFont＝美咲ゴシック第2）。
        /// 8×8ドット由来なので、フォントサイズは8の倍数で使うとドットが割れず綺麗に出る。
        /// 未導入なら通常フォントにフォールバック。
        /// ※LedFontAlt.ttf（DotGothic16）も同梱している。LedFont.ttf と入れ替えれば比較できる。
        /// </summary>
        public static Font LoadLed()
        {
            if (!_ledLoaded)
            {
                _led = Resources.Load<Font>("Fonts/LedFont");
                _ledLoaded = true;
            }
            return _led != null ? _led : Load();
        }

        private static Font _hand;
        private static bool _handLoaded;
        private static Font _mincho;
        private static bool _minchoLoaded;
        private static Font _display;
        private static bool _displayLoaded;

        /// <summary>直筆（毛筆）フォント＝佑字肅。署名・査定コメントなど「人の手」の文字。</summary>
        public static Font LoadHand() => LoadRole("Fonts/HandFont", ref _hand, ref _handLoaded);

        /// <summary>明朝フォント＝しっぽり明朝。給与明細・しおり等「会社の書類」の文字。</summary>
        public static Font LoadMincho() => LoadRole("Fonts/MinchoFont", ref _mincho, ref _minchoLoaded);

        /// <summary>見出し用ポップ体＝Mochiy Pop One。タイトルロゴ等のディスプレイ用途。</summary>
        public static Font LoadDisplay() => LoadRole("Fonts/DisplayFont", ref _display, ref _displayLoaded);

        private static Font LoadRole(string path, ref Font cache, ref bool loaded)
        {
            if (!loaded)
            {
                cache = Resources.Load<Font>(path);
                loaded = true;
            }
            return cache != null ? cache : Load();
        }
    }
}

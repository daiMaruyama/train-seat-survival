namespace TrainSurvival.Game
{
    /// <summary>
    /// シーンをまたぐ「開幕の日替わり演出を出すか」の受け渡しフラグ。
    /// タイトル→InGame の遷移時に <see cref="RequestOpeningDayTransition"/> しておくと、InGame 側が
    /// 最初に一度だけ <see cref="ConsumeOpeningDayTransition"/> して開幕演出を再生する。static なので
    /// シーンロードを越えて持ち越せる（InGame を直接再生したときは要求なし＝通常開始）。
    /// </summary>
    public static class RunStartContext
    {
        private static bool _openingDayTransition;

        /// <summary>開幕の日替わり演出を要求する（タイトルからゲーム開始時に呼ぶ）。</summary>
        public static void RequestOpeningDayTransition() => _openingDayTransition = true;

        /// <summary>要求されているか（消費はしない。CutInView が暗幕準備の判定に使う）。</summary>
        public static bool IsOpeningDayTransitionRequested => _openingDayTransition;

        /// <summary>要求を1回だけ消費する（true が返れば要求ありだった＝フラグはクリアされる）。</summary>
        public static bool ConsumeOpeningDayTransition()
        {
            bool requested = _openingDayTransition;
            _openingDayTransition = false;
            return requested;
        }
    }
}

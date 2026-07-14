using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using Unity.Services.Leaderboards.Models;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// UGS Leaderboards による全国ランキング。<see cref="RankingStore"/>（ローカル）の後段で、
    /// 送信も取得も失敗したら黙って諦める＝オフラインでもゲームは一切壊れない二段構え。
    /// 匿名サインイン（アカウント登録なし・端末にキャッシュ）で、表示名はメタデータに
    /// <see cref="PlayerProfile.Name"/>（社畜####）を載せて使う。UIは持たない（RankingViewが読む）。
    /// </summary>
    public static class UgsRanking
    {
        private const string LeaderboardId = "annual-income";

        private static Task<bool> _initTask;

        /// <summary>初期化＆サインイン済みで、通信できる見込みがあるか。</summary>
        public static bool IsReady =>
            _initTask is { IsCompletedSuccessfully: true } && _initTask.Result;

        /// <summary>メタデータ（名前・日数・駅数）。Leaderboards にはスコアしか無いのでここに載せる。</summary>
        [Serializable]
        private class Meta
        {
            public string name;
            public int days;
            public int stations;
        }

        /// <summary>
        /// 起動時に一度だけ呼ぶ（多重呼び出しは同じTaskを返す）。プロジェクト未リンク・オフラインでも
        /// 例外は握りつぶして false＝以後ローカルのみで動く。
        /// </summary>
        public static Task<bool> InitializeAsync()
        {
            _initTask ??= InitializeCore();
            return _initTask;
        }

        private static async Task<bool> InitializeCore()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UgsRanking: オンラインランキング無効（{e.Message}）。ローカルのみで続行します。");
                return false;
            }
        }

        /// <summary>記録を送信（自己ベストのみ盤面に残る＝リーダーボード側の Best score 設定）。失敗は握りつぶす。</summary>
        public static async void Submit(RankingEntry entry)
        {
            try
            {
                if (!await InitializeAsync())
                {
                    return;
                }
                var meta = new Meta { name = entry.name, days = entry.days, stations = entry.stations };
                await LeaderboardsService.Instance.AddPlayerScoreAsync(
                    LeaderboardId,
                    entry.Score,
                    new AddPlayerScoreOptions { Metadata = meta });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UgsRanking: スコア送信に失敗（{e.Message}）");
            }
        }

        /// <summary>
        /// 全国TOPを取得して <see cref="RankingEntry"/> に写して返す（表示側はローカルと同じ型で描ける）。
        /// 失敗時は null＝呼び出し側はローカル表示のままにする。
        /// </summary>
        public static async Task<List<RankingEntry>> FetchTopAsync(int limit)
        {
            try
            {
                if (!await InitializeAsync())
                {
                    return null;
                }
                LeaderboardScoresPage page = await LeaderboardsService.Instance.GetScoresAsync(
                    LeaderboardId,
                    new GetScoresOptions { Limit = limit, IncludeMetadata = true });

                var list = new List<RankingEntry>(page.Results.Count);
                foreach (LeaderboardEntry e in page.Results)
                {
                    Meta meta = ParseMeta(e.Metadata);
                    list.Add(new RankingEntry
                    {
                        name = !string.IsNullOrEmpty(meta?.name) ? meta.name : FallbackName(e.PlayerName),
                        days = meta?.days ?? 0,
                        stations = meta?.stations ?? 0,
                        yen = (long)e.Score,
                        ticks = 0, // 全国版はハイライト（新記録演出）の対象にしない
                    });
                }
                return list;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UgsRanking: 全国ランキング取得に失敗（{e.Message}）");
                return null;
            }
        }

        private static Meta ParseMeta(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                return JsonUtility.FromJson<Meta>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>メタデータが無い古い記録用。UGSの自動名（Player#1234）をそれらしく整える。</summary>
        private static string FallbackName(string playerName)
        {
            return string.IsNullOrEmpty(playerName) ? "名無しの社畜" : playerName;
        }
    }
}

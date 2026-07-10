using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>給与計算（明細とランキングで同じ式を使う唯一の置き場）。</summary>
    public static class Payroll
    {
        public const int BasePerDay = 8000;    // 基本給（日給）
        public const int PerStation = 300;     // 精勤手当（駅あたり）
        public const int Deduction = 12000;    // 過労控除

        /// <summary>差引支給額（マイナスにはならない）。</summary>
        public static int Total(int days, int stations)
            => Mathf.Max(0, days * BasePerDay + stations * PerStation - Deduction);

        /// <summary>推定年収＝この勤務ぶりが12ヶ月続いた場合（スコアの正体）。</summary>
        public static long Annual(int days, int stations) => Total(days, stations) * 12L;
    }

    /// <summary>ランキング1件。共有DB（UGS等）とローカル保存の両方でこの形を使う。</summary>
    [Serializable]
    public struct RankingEntry
    {
        public string name;
        public int days;      // 何日目まで生き延びたか
        public int stations;  // 通算駅数
        public long yen;      // 推定年収（スコアの主キー）
        public long ticks;    // 記録日時（DateTime.Ticks）

        /// <summary>並び替え用スコア＝推定年収。古い記録（yen未保存）は実績から換算。</summary>
        public long Score => yen > 0 ? yen : Payroll.Annual(days, stations);
    }

    /// <summary>
    /// ローカルのランキング保存（PlayerPrefs に JSON、上位10件）。
    /// 共有DB（UGS Leaderboards）を後段に差しても、ここは「自分の記録のキャッシュ／オフライン時の表示」
    /// として残す二段構え。純粋なデータ操作だけで UI は持たない。
    /// </summary>
    public static class RankingStore
    {
        private const string Key = "LocalRanking";
        private const int Max = 10;

        /// <summary>直近に記録したエントリの ticks（リザルトからランキングを開いたときの強調表示用）。</summary>
        public static long LastRecordedTicks { get; private set; }

        /// <summary>直近の記録が何位に入ったか（0始まり、圏外は -1）。強制署名の発火判定に使う。</summary>
        public static int LastRecordRank { get; private set; } = -1;

        [Serializable]
        private class Data
        {
            public List<RankingEntry> entries = new List<RankingEntry>();
        }

        /// <summary>上位から並んだ一覧（最大10件）。</summary>
        public static List<RankingEntry> Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json))
            {
                return new List<RankingEntry>();
            }
            try
            {
                Data data = JsonUtility.FromJson<Data>(json);
                return data?.entries ?? new List<RankingEntry>();
            }
            catch
            {
                return new List<RankingEntry>();
            }
        }

        /// <summary>記録を追加して保存。何位に入ったか（0始まり、圏外は -1）を返す。</summary>
        public static int Record(RankingEntry entry)
        {
            LastRecordedTicks = entry.ticks;
            List<RankingEntry> entries = Load();
            entries.Add(entry);
            entries.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.ticks.CompareTo(b.ticks); // 同点は先着上位
            });
            if (entries.Count > Max)
            {
                entries.RemoveRange(Max, entries.Count - Max);
            }

            var data = new Data { entries = entries };
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();

            LastRecordRank = entries.FindIndex(e => e.ticks == entry.ticks);
            return LastRecordRank;
        }

        /// <summary>指定の記録の名前を書き換える（ランキング画面の「署名」用）。</summary>
        public static void Rename(long ticks, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }
            List<RankingEntry> entries = Load();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].ticks == ticks)
                {
                    RankingEntry e = entries[i];
                    e.name = newName.Trim();
                    entries[i] = e;
                }
            }
            var data = new Data { entries = entries };
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
    }

    /// <summary>プレイヤーの呼び名（ランキング表示名）。未設定なら「社畜1234」を自動生成。</summary>
    public static class PlayerProfile
    {
        private const string Key = "PlayerName";

        public static string Name
        {
            get
            {
                string name = PlayerPrefs.GetString(Key, "");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "社畜" + UnityEngine.Random.Range(1000, 10000);
                    PlayerPrefs.SetString(Key, name);
                }
                return name;
            }
            set
            {
                string trimmed = (value ?? "").Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    PlayerPrefs.SetString(Key, trimmed);
                    PlayerPrefs.Save();
                }
            }
        }
    }
}

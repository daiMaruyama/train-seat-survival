# 設計・技術メモ（座れ！サラリーマン！）

審査・説明用の簡易まとめ。詳細は各クラスの XML ドキュメントコメント参照。

## 全体構成：Core / Game の2層

```
Assets/Scripts/
├── Core/   … 純粋C#（UnityEngine非依存）。ゲームのルール＝「脳」
│     CommuteWorld / Passenger / StationChange / CommuteConfig / DeterministicRng
└── Game/   … MonoBehaviour。描画・物理・入力＝「身体」
      CommuteDirector（司令塔）, 各View, アイテム, UIツールキット
```

- **Core は Unity を知らない**。asmdef で分離し、EditMode テスト（`CommuteWorldTests`）で
  ルールだけを高速に検証できる。
- **決定論**：`DeterministicRng`（seed 固定の擬似乱数）で「誰がどの駅で降りるか」を再現可能にしている。
  リプレイ・デバッグ・テストが安定する。
- **View はルールを持たない**（単一責任）。例：`StaminaSystem`＝疲労の数式だけ／`HudView`＝ゲージ描画だけ／
  `RunController`＝終了判定とリスタートだけ。

## 主要パターン

| パターン | 使い所 | 狙い |
|---|---|---|
| **司令塔（Mediator/Facade）** | `CommuteDirector` | Core の乗降イベントと、物理的な席・乗客・プレイヤーの取り合いを一箇所で仲介。View 同士は直接会話しない |
| **オブジェクトプール** | `PassengerPool`（乗客）、`ItemSpawner`（アイテム） | 毎日全員入れ替わる満員電車でも生成/破棄をしない。アイテムは非表示→日替わりで撒き直し |
| **情報隠蔽の唯一の窓口** | `CommuteDirector.TryReadSeatIntel` | 「あと何駅で降りるか」という隠し情報を覗ける口をここだけに限定。データメガネ（`DataVisionView`）もここ経由 |
| **静的ストア（永続化）** | `RankingStore` / `Payroll` / `PlayerProfile` | PlayerPrefs＋JSON のローカル保存。給与式は `Payroll` に一本化（明細とランキングが同じ式を共有） |
| **シーン間コンテキスト** | `RunStartContext` | 「タイトル→初日演出」等のフラグを static で受け渡し（Consume 型＝読んだら消える） |
| **控えめなシングルトン** | `GameAudio.Instance` / `DataVisionView.Instance` | 横断アクセスが必要な音とメガネ視界のみ。乱用しない |
| **テンプレート複製** | `RuntimeMaterials` | .mat テンプレートを複製して色替え。`Shader.Find` はビルドでバリアントが剥がれ真っピンクになるため禁止 |
| **静的 IsOpen ガード** | `PauseMenuView.IsOpen` 等 | ポーズ・研修・ランキングの排他制御。入力側（`PlayerSit`・`TitleController`）が参照して誤操作を防ぐ |

## UI：全部コードで組む（uGUI）

- **`UiKit`**：Panelize（角丸/9スライス）、MakeButton（ペルソナ風：硬い影・太枠・ホバー反転、
  当たり判定と動く見た目を分離してチラつき防止）、MakePaperSlider（紙×墨×マーカー）、
  AddPaperGrain / AddLedGrid などの質感オーバーレイ。
- **`UiFont`**：役割別フォントのローダ（丸ゴ＝基本／美咲＝LED発車標／毛筆＝署名／明朝＝書類／
  ポップ＝タイトル）。Resources から読み、無ければ内蔵フォントへフォールバック。
- **世界観ルール**（詳細は `UI_GUIDELINE.md`）：UIは「紙の書類」「LED発車標」「駅サイン」の
  3種のモノとして作る。汎用の角丸カード＋ぼかし影は禁止。

## 演出・手触りの作法

- **unscaled time 駆動**：ポーズ（timeScale=0）中も動く演出は `Time.unscaledTime`／
  `WaitForSecondsRealtime` で書く（カットイン、ゲームオーバーのカメラ崩れ落ち等）。
- **一括消音**：ポーズ＝`AudioListener.pause`（SEごと凍結）、マスター音量＝`AudioListener.volume`。
  シーン遷移時は `OnDestroy` で必ず解除（static なので放置すると次シーンが無音になる）。
- **危険度の一元化**：`StaminaSystem.Danger01` を心音（GameAudio）と視界演出（DangerVisionFx）が
  同じ値で読む＝演出が必ず同期する。

## 外部アセットの扱い（Git 方針）

- ストア/配布アセットは **Git 管理しない**（.gitignore 済み：乗客モデル、コーヒー、靴、フォント、
  音、Kenney UI）。導入手順は README に記載し、シーンは GUID 参照のみ保持。
- 素材は必要分だけ `Resources/` にコピーして使う（置き換え式）。
- 外部アセット由来の癖はスポーナー側で吸収：スキンメッシュ→静的メッシュ化
  （`FlattenSkinnedMeshes`）、LOD の重なり描画→lod0 のみ残す（`KeepOnlyHighestLod`）、
  寸法はバウンズから自動正規化。

## テスト

- `Tests/EditMode`（Core）：`CommuteWorldTests` — 乗降ルールの決定論を検証
- `Tests/GameEditMode`：乗客最適化・シーン間コンテキスト・ランタイムマテリアル
- `Tests/PlayMode`：マテリアルのビルド健全性・シーン遷移

## 技術スタック

Unity 6 / URP ・ 新 Input System ・ uGUI（全コード生成）・ PlayerPrefs＋JSON（ローカル保存）
・ NUnit（EditMode/PlayMode）・ 外部依存パッケージなし（Pure C# Core）

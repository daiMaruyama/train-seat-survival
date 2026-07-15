# 座れ！サラリーマン！（train-seat-survival）

満員電車で座席を奪い合う一人称サバイバル。立っていると過労で消耗し、倒れたら年収査定のうえ処分される。
降りる客を読み、空いた瞬間に座る。**座れた者だけが明日も出勤できる。**

- エンジン：Unity 6（URP）／ プロジェクト本体は `unity/`
- 設計の概要：[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- UIの設計指針：[docs/UI_GUIDELINE.md](docs/UI_GUIDELINE.md)

## 操作

| 入力 | 動作 |
|---|---|
| W A S D | 移動 |
| マウス | 見まわす |
| E ／ 左クリック | 空席に座る（照準を合わせて） |
| ESC | 小休止（ポーズ：音量・感度・リスタート・退職） |
| Enter | 書類の決定（研修しおり・給与明細への署名） |
| R ／ T ／ L | リザルト画面：リトライ／タイトル／ランキング |

## 遊び方（ルール）

1. 空席は淡く光る。照準を合わせて E（または左クリック）で着席＝その日は勝ち。
2. 立ちっぱなしは体力が減り続け、尽きたら倒れてラン終了。日を追うごとに消耗は激しくなる。
3. 車内アイテム（毎日撒き直し）：**コーヒー**（橙・回復）／**データメガネ**（青・次駅で空く席が光る）／**赤い靴**（その日だけ移動速度アップ）。
4. スコアは**推定年収**（勤続日数と通過駅数から算出）。ローカルTOP10と、UGSによる**全国ランキング**に記録される。

## セットアップ（クローン後に必要な作業）

外部ストア/配布アセットはライセンス上 **Git 管理していない**（`.gitignore` 済み）。
無くても起動はできる（プリミティブや内蔵フォントにフォールバック）が、完全な見た目には以下の導入が必要：

| 導入先フォルダ | 内容 | 用途 |
|---|---|---|
| `unity/Assets/Floreswa/` → `Resources/Passengers/` | ローポリ人物モデル（Floreswa） | 乗客 |
| `unity/Assets/Coffee_Cup/` → `Resources/CoffeeCup/` | コーヒーカップモデル | 回復アイテム |
| `unity/Assets/npc_casual_set_00/` | NPC衣装セット（靴 shoe_01〜04） | ダッシュ靴アイテム |
| `unity/Assets/Resources/Fonts/` | Noto Sans JP（通常UI）・美咲フォント・衡山毛筆フォント・しっぽり明朝・Mochiy Pop（いずれもフリー/OFL） | UI役割別フォント |
| `unity/Assets/Audio/` → `Resources/Audio/` | 効果音・BGM素材 | 走行音・ベル・BGM等 |
| `unity/Assets/kenney_ui-pack/` → `Resources/UiSprites/` | Kenney UI Pack（CC0） | UI 9スライス素材 |
| `unity/Assets/Animations/` | モーキャプ2クリップ（MoCapCentral由来） | 乗客の座り/立ちモーション |

シーン側はGUID参照のみ保持しているので、**同じフォルダ構成で置けば再インポートだけで繋がる**。

### オンラインランキング（UGS）

- Unity Cloud のプロジェクトにリンク済み（Leaderboards ID: `annual-income`、Highest to lowest / Keep best）。
- 匿名サインインなのでプレイヤー側の登録は不要。**オフライン・未リンクでも壊れない**（ローカル表示に自動フォールバック）。

## ビルド

`File > Build Profiles` から Windows / macOS を選んでビルド（シーンは Title / InGame の2つ）。
マテリアルはテンプレート複製方式（`RuntimeMaterials`）なのでビルドでのシェーダー欠落（真っピンク）は起きない想定。

## 開発メモ

- [設計・技術まとめ](docs/ARCHITECTURE.md)
- [UI ガイドライン](docs/UI_GUIDELINE.md)
- [UI/UX 改善ロードマップ](docs/UI_UX_ROADMAP.md)

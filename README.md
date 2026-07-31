# UnityLocalMasterData

Unity クライアント内に同梱するローカルマスターデータ管理モジュールです。

Excel または Google SpreadSheet のシートをテーブルとして扱い、Editor 上で暗号化済み `.bin` に変換します。
ランタイムでは `StreamingAssets` から `.bin` を読み込み、型付きのテーブルクラスとして参照できます。

## Requirements

- Unity
- ExcelDataReader.DataSet v3.6.0 from NuGet
- Csv-CSharp v1.0.3 from NuGet

## 全体の流れ

1. マスターデータ用のテーブルクラスを実装する
2. Excel または SpreadSheet Writer アセットを作成する
3. Writer アセットに入出力先とセキュリティキーを設定する
4. Writer の `Build` を実行して `.bin`、manifest、鍵コードを生成する
5. ランタイムで `LocalMasterDataReader.Instance.BuildAsync(...)` を呼ぶ
6. 各テーブルの `Instance`、`Records`、`GetRecord(key)` からデータを読む

## テーブルクラスの実装

`LocalMasterDataTable<TKey, TRecord, TTable>` を継承したクラスを作成します。

```csharp
using LocalMasterData;

public sealed class ItemMasterTable :
    LocalMasterDataTable<int, ItemMasterTable.Record, ItemMasterTable>
{
    public sealed class Record
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ushort Rarity { get; set; }
    }

    public override string GetSheetName()
    {
        return "Item";
    }

    public override int GetKey(Record record)
    {
        return record.Id;
    }
}
```

### 実装ルール

- `TRecord` は `class` かつ `new()` 可能な型にしてください。
- `Record` の列は public property として定義してください。field は読み書き対象になりません。
- シート 1 行目のヘッダー名と `Record` の property 名を完全一致させてください。
- `GetSheetName()` は Excel のシート名、または SpreadSheet Writer の `Name` と一致させてください。
- `GetKey(record)` は `IdMap` に登録するキーを返してください。キー重複があると読み込み時に例外になります。
- 現在の Reader は `Assembly-CSharp` から `ILocalMasterDataTable` 実装を探索します。テーブルクラスは通常のゲーム側スクリプトとして配置してください。独自 asmdef 配下に置く場合は Reader 側の探索処理の拡張が必要です。

### 対応する型

`Record` の property は以下の型をバイナリとして読み書きできます。

- `short`
- `int`
- `long`
- `ushort`
- `uint`
- `ulong`
- `float`
- `double`
- `bool`
- `DateTime`
- `string`

上記以外の型は使用しないでください。未対応型は Writer 側で文字列として書き出されますが、Reader 側で property 型へ代入できず失敗します。

数値は `InvariantCulture` で parse されます。小数は `1.5` のように `.` 区切りで入力してください。
`bool` は `true` / `false`、`DateTime` は `DateTime.Parse(..., InvariantCulture, DateTimeStyles.RoundtripKind)` で解釈できる文字列を入力してください。

## マスターデータ表の形式

各シートが 1 テーブルになります。

| Id | Name | Description | Rarity |
| -- | -- | -- | -- |
| 1 | Potion | Recover HP | 1 |
| 2 | HiPotion | Recover more HP | 2 |

- 1 行目はヘッダー行です。
- 2 行目以降がレコードです。
- 空のヘッダー列は無視されます。
- ヘッダーに存在しない property は空文字として扱われます。数値や `DateTime` の空文字は parse に失敗するため、必要な値を入力してください。
- property 定義を変更した場合は、必ず Writer で `.bin` を再生成してください。

## Writer アセットの作成

Unity Editor の Project ウィンドウで右クリックし、以下から Writer アセットを作成します。

- Excel: `Create > LocalMasterData > Excel Writer`
- SpreadSheet: `Create > LocalMasterData > SpreadSheet Writer`

Writer アセットでは以下を設定します。

- `OutputFolder`: `Assets/StreamingAssets` 配下の出力先フォルダ
- `OutputScriptFolder`: `Assets` 内の `Scripts` フォルダ配下
- `OutputManifestFolder`: `Assets` 内の `Resources` フォルダ配下
- `InputFolder`: Excel Writer のみ。`.xlsx` を置く `Editor` フォルダ配下

`OutputFolder`、`OutputScriptFolder`、`OutputManifestFolder`、`InputFolder` は、それぞれ上記の名前を含むフォルダ配下である必要があります。
条件を満たさない場合、Inspector にエラーが表示されて Build ボタンが使えません。

## Excel からビルドする

1. `.xlsx` ファイルを `InputFolder` 配下に置く
2. シート名を `GetSheetName()` と一致させる
3. 1 行目に `Record` の property 名を入れる
4. Excel Writer アセットの `Build` を押す

ビルド時に、`InputFolder` 配下のすべての `.xlsx` が再帰的に読み込まれます。
各シートはシート名をキーにテーブルクラスへ対応付けられ、対応するクラスがないシートは warning のみ出して無視されます。

## SpreadSheet からビルドする

SpreadSheet Writer は Google Apps Script の Web App から CSV を取得して `.bin` を生成します。

### GAS の設定

`LocalMasterDataWriter/Editor/SpreadSheetGAS.js` の内容を Google Apps Script に貼り付け、Web App として Deploy してください。

Script Properties に以下を設定します。

- `API_TOKEN`: Writer からアクセスするための任意のトークン
- `SPREADSHEET_ID`: 対象 Google SpreadSheet の ID

Web App は `token` と `gid` を query parameter として受け取り、対象シートを CSV として返します。

### Writer の設定

SpreadSheet Writer アセットで以下を設定します。

- `m_ApiUrl`: Deploy した Web App の URL
- `m_ApiToken`: GAS の `API_TOKEN` と同じ値
- `m_Sheets[].Name`: テーブル名。`GetSheetName()` と一致させる
- `m_Sheets[].Gid`: Google SpreadSheet のシート gid

Inspector では `Build All` で全シートをビルドできます。
各シート行の `Build` ボタンで 1 シートだけビルドすることもできます。

## 生成されるファイル

Writer の `Build` を実行すると以下が生成されます。

- `OutputFolder/<SheetName>.bin`
- `OutputManifestFolder/lmd-enum.txt`
- `OutputScriptFolder/LocalMasterDataConst.cs`

`lmd-enum.txt` は `Resources.Load<TextAsset>("lmd-enum")` で読み込まれます。
内容は以下の形式です。

```text
<StreamingAssets からの相対フォルダ>,<SheetName1>,<SheetName2>,...
```

例:

```text
Database,Item,Quest,Skill
```

この場合、ランタイムでは以下のようなファイルを読みます。

```text
Application.streamingAssetsPath/Database/Item.bin
Application.streamingAssetsPath/Database/Quest.bin
Application.streamingAssetsPath/Database/Skill.bin
```

`LocalMasterDataConst.cs` には AES IV、AES Key、HMAC Secret Key が byte 配列として出力されます。
ランタイム読み込み時に同じ値を渡してください。

## ランタイムで読み込む

起動時など、マスターデータ参照前に `BuildAsync` を呼びます。

```csharp
using LocalMasterData;
using UnityEngine;

public sealed class MasterDataBootstrap : MonoBehaviour
{
    private async void Start()
    {
        await LocalMasterDataReader.Instance.BuildAsync(
            LocalMasterDataConst.AesId,
            LocalMasterDataConst.AesKey,
            LocalMasterDataConst.HmacSecretKey);

        var item = ItemMasterTable.Instance.GetRecord(1);
        Debug.Log(item?.Name);
    }
}
```

読み込み完了後は以下で参照できます。

```csharp
foreach (var record in ItemMasterTable.Instance.Records)
{
    Debug.Log($"{record.Id}: {record.Name}");
}

var item = ItemMasterTable.Instance.GetRecord(1);
```

### Reader の状態

- `LocalMasterDataReader.Instance.IsLoaded`: 読み込み完了済みか
- `LocalMasterDataReader.Instance.IsBuilding`: 読み込み中か
- `LocalMasterDataReader.Instance.Tables`: 読み込み済みテーブル一覧
- `LocalMasterDataReader.Exists`: Reader インスタンスが作成済みか

`LocalMasterDataTable<T>.Instance` は `BuildAsync` 完了前に参照すると例外になります。
また、同時に複数回 `BuildAsync` を呼ぶと `LocalMasterDataReader is already building.` で例外になります。

不要になった場合は `Dispose()` で読み込み済みテーブルと singleton を破棄できます。

```csharp
LocalMasterDataReader.Instance.Dispose();
```

## バイナリ仕様

Writer は各テーブルを以下の順で処理します。

1. `Record` の public property 一覧を取得
2. 1 行目ヘッダーを property 名に対応付け
3. レコード数を書き込み
4. 各レコードの property 値を型ごとにバイナリ書き込み
5. Deflate 圧縮
6. HMAC-SHA256 を付与
7. AES-256-CBC / PKCS7 で暗号化

Reader は逆順で復号、HMAC 検証、展開を行い、`Records` と `IdMap` を構築します。
HMAC 検証に失敗した場合は空の byte 配列が返るため、結果的に読み込みで失敗します。

## 注意点

- manifest に含まれるテーブル名と実装済みテーブルクラスが一致しない場合、Editor では読み込み時に例外になります。
- manifest にテーブル名があっても対応する `.bin` が存在しない場合、ファイル読み込みで失敗します。
- シート名、Writer の Sheet Name、`GetSheetName()` はすべて同じ名前にしてください。
- `Record` の property の追加、削除、型変更、順序変更後は `.bin` を再生成してください。
- 空文字を数値、bool、DateTime に変換することはできません。
- 暗号鍵を再生成した場合は、既存 `.bin` も再生成してください。

## Installation with UPM

You can install this package from Unity Package Manager using the Git URL:

```text
https://github.com/yassy0413/UnityLocalMasterData.git
```

![Package Manager Step 1](Editor/StoreDocument/PackageManager01.png)

![Package Manager Step 2](Editor/StoreDocument/PackageManager02.png)

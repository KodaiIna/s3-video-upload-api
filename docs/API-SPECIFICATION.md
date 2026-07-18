# S3 Video Upload API 仕様書

| 項目 | 内容 |
|---|---|
| 文書バージョン | 1.0 |
| 対象アプリケーション | S3 Video Upload API |
| 実装方式 | ASP.NET Core Minimal API |
| 対象ランタイム | .NET 10 |
| 保存先 | Amazon S3またはS3互換ストレージ |

## 1. 目的

Web・モバイル等のクライアントから受信した軽量な動画を、APIサーバーを経由してS3へ保存する。クライアント事情に応じて次の2方式を提供する。

1. `multipart/form-data` によるバイナリアップロード
2. JSON内のBase64文字列によるアップロード

通常は転送量とサーバーメモリ効率に優れる `multipart/form-data` を使用する。Base64方式はJSONしか扱えないクライアント向けとする。

## 2. システム構成

```text
Client
  │ HTTPS
  ▼
ASP.NET Core API
  ├─ リクエストサイズ制限
  ├─ ファイル名・Content-Type・サイズ検証
  ├─ Base64デコード（JSON方式のみ）
  └─ AWS SDK標準認証情報チェーン
       │ PutObject
       ▼
Amazon S3（非公開バケット）
```

APIサーバーはAWSアクセスキーをリクエストから受け取らない。ローカル環境では環境変数または共有AWSプロファイル、本番環境ではECS/EKS/EC2等のIAMロールを利用する。

## 3. 機能一覧

| ID | 機能 | Method | Path |
|---|---|---|---|
| API-01 | multipart動画アップロード | POST | `/api/videos/multipart` |
| API-02 | Base64動画アップロード | POST | `/api/videos/base64` |
| API-03 | ヘルスチェック | GET | `/health` |

各リクエスト・レスポンスの項目定義は [INTERFACE-SPECIFICATION.md](INTERFACE-SPECIFICATION.md) を参照する。

## 4. 共通処理仕様

### 4.1 入力検証

アップロードAPIでは次を検証する。

| 検証 | 仕様 |
|---|---|
| ファイル名 | 必須。クライアントパスを除去し、末尾のファイル名だけを使用する |
| Content-Type | 必須。ただしBase64 Data URI内に含まれる場合はJSON項目を省略可能 |
| 許可形式 | `S3:AllowedContentTypes` と大文字小文字を区別せず照合する |
| ファイルサイズ | 1バイト以上かつ `S3:MaxFileSizeBytes` 以下 |
| Base64形式 | .NET標準Base64としてデコード可能であること |
| Data URI整合性 | JSONとData URIの両方にContent-Typeがある場合は一致すること |

デフォルトの許可Content-Typeは次のとおり。

- `video/mp4`
- `video/webm`
- `video/quicktime`
- `video/x-matroska`
- `video/x-msvideo`

Content-Typeはクライアント申告値の許可リスト検証であり、動画内容のシグネチャ検査ではない。信頼できない利用者へ公開する場合は、別途ファイル形式検査とマルウェアスキャンを追加する。

### 4.2 リクエストサイズ

デフォルト最大動画サイズは25 MiB（`26,214,400` bytes）とする。設定可能な動画サイズの上限は256 MiBとする。

- multipart上限: 最大動画サイズ + 1 MiB（フォーム境界等のオーバーヘッド）
- JSON上限: Base64最大文字数 + 1 MiB（JSON等のオーバーヘッド）

Base64方式はデコード後のバイト列をメモリ上に保持する。大きな動画には使用しない。

### 4.3 S3オブジェクトキー

キーはクライアント指定のファイル名を直接採用せず、次の形式でAPI側が生成する。

```text
{KeyPrefix}/{UTC yyyy}/{UTC MM}/{UTC dd}/{GUID 32桁}{安全な拡張子}
```

例:

```text
videos/2026/07/18/0123456789abcdef0123456789abcdef.mp4
```

既知Content-Typeでは、次の固定マッピングで拡張子を決定する。

| Content-Type | 拡張子 |
|---|---|
| `video/mp4` | `.mp4` |
| `video/webm` | `.webm` |
| `video/quicktime` | `.mov` |
| `video/x-matroska` | `.mkv` |
| `video/x-msvideo` | `.avi` |

追加設定した未知Content-Typeでは、元ファイル名の拡張子が英数字のみかつ10文字以内の場合に限り採用し、それ以外は `.bin` とする。

### 4.4 S3保存

AWS SDKの `PutObject` を使用する。設定する主要項目は次のとおり。

| PutObject項目 | 設定値 |
|---|---|
| BucketName | `S3:BucketName` |
| Key | 4.3の生成キー |
| InputStream | 受信ファイルまたはBase64デコード結果 |
| ContentType | 正規化・検証済みContent-Type |
| AutoCloseStream | `false`（呼び出し元で破棄） |

S3から返却されたETagは外側のダブルクォートを除去してAPIレスポンスへ設定する。

## 5. 処理フロー

### 5.1 multipart方式

1. APIが `file` フィールドを受信する。
2. ファイル名、Content-Type、サイズを正規化・検証する。
3. S3オブジェクトキーを生成する。
4. 受信ストリームを `PutObject` へ渡す。
5. バケット、キー、ETag、サイズ、Content-Typeを返す。

### 5.2 Base64方式

1. APIがJSONを受信する。
2. ファイル名を検証する。
3. Data URIの場合はメディアタイプとBase64部分を分離する。
4. デコード前の文字数上限を検証する。
5. Base64をバイト列へデコードし、デコード後サイズを再検証する。
6. JSONとData URIのContent-Type整合性・許可リストを検証する。
7. S3オブジェクトキーを生成して `PutObject` を実行する。
8. multipart方式と同じ成功レスポンスを返す。

## 6. エラー処理

| HTTP status | 条件 | レスポンス |
|---:|---|---|
| 400 | 必須不足、形式不正、未許可Content-Type、サイズ検証エラー | Validation Problem Details |
| 413 | Webサーバーのリクエストボディ上限超過 | Webサーバー既定レスポンス |
| 500 | API内の予期しない例外 | Problem Details（内部情報は非公開） |
| 502 | S3 SDKが `AmazonS3Exception` を返した | `S3 upload failed` Problem Details |

キャンセルされたHTTPリクエストのCancellationTokenはS3 SDKまで伝播する。

## 7. 設定仕様

| 設定キー | 型 | 必須 | デフォルト | 説明 |
|---|---|:---:|---|---|
| `S3:BucketName` | string | ○ | なし | 保存先バケット |
| `S3:Region` | string | ○ | `ap-northeast-1` | AWSリージョン |
| `S3:KeyPrefix` | string |  | `videos` | キー接頭辞。空も可 |
| `S3:MaxFileSizeBytes` | long |  | `26214400` | 最大動画サイズ。1〜268,435,456 |
| `S3:AllowedContentTypes` | string[] | ○ | 4.1参照 | 許可Content-Type |
| `S3:ServiceUrl` | string |  | null | S3互換ストレージURL |
| `S3:ForcePathStyle` | bool |  | false | パス形式アクセスを使うか |
| `Cors:AllowedOrigins` | string[] |  | 空 | クロスオリジンを許可するURL |

環境変数では `:` を `__` に置換する。例: `S3__BucketName`。

必須設定または値域が不正な場合、Options Validationにより起動を失敗させる。

## 8. セキュリティ要件

- S3バケットは非公開とする。
- 実行IAMには対象プレフィックスへの `s3:PutObject` のみを付与する。
- アクセスキーを設定ファイルやソースコードへ保存しない。
- 本番公開前にAPI認証・認可とレート制限を追加する。
- CORSは必要なオリジンだけを明示し、ワイルドカードにしない。
- エラーレスポンスへAWS内部エラーや資格情報を出力しない。
- 必要に応じてファイルシグネチャ検査、マルウェアスキャン、監査ログを追加する。

## 9. 非機能・運用上の注意

- multipart方式は受信ストリームをS3 SDKへ渡すため、Base64方式よりメモリ効率がよい。
- Base64方式は転送サイズが概ね33%増加し、デコード用バイト配列を確保する。
- API成功はS3の `PutObject` 成功をもって判定する。
- ETagは暗号学的な完全性ハッシュとして扱わない。multipart uploadや暗号化設定によりMD5と一致しない場合がある。
- ヘルスチェックはAPIプロセスの応答確認であり、S3疎通確認は行わない。

## 10. 対象外

現バージョンでは次を提供しない。

- S3からの動画取得・削除
- 署名付きURLの発行
- 動画変換、サムネイル生成、メタデータ抽出
- multipart upload APIによる巨大ファイルの分割転送
- ウイルス・マルウェアスキャン
- API利用者の認証・認可

## 11. 環境別S3接続

### 11.1 ローカル開発

`ASPNETCORE_ENVIRONMENT=Development` では `appsettings.Development.json` により、S3接続先をLocalStackの `http://localhost:4566` へ切り替える。`ForcePathStyle=true`、バケット名 `local-video-bucket` を使用する。

`compose.yaml` はLocalStack S3を起動し、READYフックでデバッグ用バケットを自動作成する。`LocalStack` 起動プロファイルのAWSアクセスキーはローカルエミュレーター専用ダミー値であり、実AWSへの権限を持たない。

### 11.2 ローカルから実S3

`ASPNETCORE_ENVIRONMENT=LocalAws` では `appsettings.LocalAws.json` を読み込み、LocalStack endpointを設定せず実S3へ接続する。開発者認証にはIAM Identity Centerの名前付きプロファイルを使用し、AWS CLI v2で `aws sso login` を実行してからAPIを起動する。

バケット名とリージョンはASP.NET Core User Secretsへ保存できるが、AWSアクセスキーやSSOトークンはUser Secretsへ保存しない。.NETでSSOプロファイルを解決するため `AWSSDK.SSO` と `AWSSDK.SSOOIDC` を参照する。

### 11.3 本番ECS

本番では `S3:ServiceUrl` を設定せず、`S3:Region` の実S3へ接続する。S3アクセス権限はECSタスク定義の `taskRoleArn` に指定したタスクロールへ付与する。

API用 `s3:PutObject` 権限を `executionRoleArn` のタスク実行ロールへ付与しない。タスク実行ロールはECR pullやCloudWatch Logs等のECSエージェント処理に使用する。

ECSがタスクロールの一時認証情報をコンテナへ提供するため、本番環境へ長期アクセスキーを設定しない。具体的な設定は [LOCAL-DEVELOPMENT-AND-ECS.md](LOCAL-DEVELOPMENT-AND-ECS.md) と `deploy/` の雛形を参照する。

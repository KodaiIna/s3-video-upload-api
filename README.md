# S3 Video Upload API

.NET 10 / ASP.NET Core Minimal APIで、軽量な動画をAPIサーバー経由でAmazon S3へ保存します。

## エンドポイント

| Method | Path | Request |
|---|---|---|
| `POST` | `/api/videos/multipart` | `multipart/form-data` の `file` フィールド |
| `POST` | `/api/videos/base64` | JSONの `fileName`, `contentType`, `base64Data` |
| `GET` | `/health` | APIプロセスのヘルスチェック |

## ローカルデバッグ（LocalStack）

前提は.NET 10 SDKとDocker Desktopです。LocalStackのS3と、デバッグ用バケット `local-video-bucket` が自動作成されます。

```powershell
docker compose up -d localstack
dotnet run --launch-profile LocalStack --project .\src\S3VideoUploadApi
```

APIは `http://localhost:5080`、LocalStackは `http://localhost:4566` で起動します。Visual Studio/Riderでは `LocalStack` プロファイルを選択してデバッグしてください。

ローカル設定は [appsettings.Development.json](src/S3VideoUploadApi/appsettings.Development.json) にあり、次が自動適用されます。

- S3 endpoint: `http://localhost:4566`
- bucket: `local-video-bucket`
- region: `ap-northeast-1`
- path-style access: 有効
- AWS認証情報: `test` / `test`（LocalStack専用）
- 開発CORS: `http://localhost:3000`, `http://localhost:5173`

### 自動スモークテスト

次のスクリプトはLocalStackを起動し、APIを一時起動して両方のエンドポイントから動画をアップロードし、S3オブジェクトの存在まで確認します。

```powershell
.\scripts\verify-local.ps1
```

成功後もLocalStackはデバッグ用に起動したままです。停止する場合:

```powershell
docker compose down
```

ローカルS3のオブジェクト一覧:

```powershell
docker compose exec -T localstack awslocal s3 ls s3://local-video-bucket/videos/ --recursive
```

詳細は [ローカル開発・ECSデプロイガイド](docs/LOCAL-DEVELOPMENT-AND-ECS.md) を参照してください。

## ローカルから実S3（推奨: IAM Identity Center / SSO）

開発者が実AWSへ接続する場合は、長期アクセスキーではなくIAM Identity Centerの一時認証情報を使用します。AWS CLI v2が必要です。

初回だけ、開発用バケット名を指定してSSOプロファイルとUser Secretsを設定します。

```powershell
.\scripts\setup-real-s3-sso.ps1 `
  -BucketName "your-development-bucket"
```

スクリプトは `aws configure sso`、`aws sso login`、`aws sts get-caller-identity` を順に実行します。既定プロファイル名は `s3-video-upload-dev` です。バケット名とリージョンだけをASP.NET Core User Secretsへ保存し、AWS認証情報は保存しません。

実S3を使ってデバッグ起動:

```powershell
dotnet run --launch-profile RealS3Sso --project .\src\S3VideoUploadApi
```

実S3へ両方式でスモークテストする場合:

```powershell
.\scripts\verify-real-s3-sso.ps1 `
  -BucketName "your-development-bucket"
```

このスクリプトは実S3へ2オブジェクトを書き込みます。最小権限を `s3:PutObject` に保つため自動削除は行いません。

IAM Identity CenterのPermission Setには、[開発者用最小権限ポリシー](deploy/iam-developer-sso-permission-policy.json)相当を付与してください。

アクセスキー方式もAWS SDKの名前付きプロファイルで動作しますが、SSOを利用できない場合の最終手段です。アクセスキーを `appsettings*.json`、User Secrets、`launchSettings.json`、ソースコードへ保存しないでください。

## リクエスト例

### multipart/form-data

```powershell
curl.exe -X POST "http://localhost:5080/api/videos/multipart" `
  -F "file=@C:\path\to\sample.mp4;type=video/mp4"
```

### JSON + Base64

`base64Data` は純粋なBase64文字列と `data:video/mp4;base64,...` 形式の両方に対応します。

```powershell
$path = "C:\path\to\sample.mp4"
$body = @{
  fileName    = [System.IO.Path]::GetFileName($path)
  contentType = "video/mp4"
  base64Data  = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($path))
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri "http://localhost:5080/api/videos/base64" `
  -ContentType "application/json" `
  -Body $body
```

Base64は元データより約33%大きくなり、API内で一度メモリへ展開されます。通常は `multipart/form-data` を推奨します。

## 本番ECS

本番では `S3__ServiceUrl` を設定しません。AWS SDKが対象リージョンの実S3へ接続します。

S3権限はECSタスク定義の `taskRoleArn` に指定したタスクロールへ付与します。ECRからのpullやCloudWatch Logsに使う `executionRoleArn` へ、アプリ用S3権限を付与しないでください。

コンテナへ長期アクセスキーを設定する必要はありません。ECSがタスクロールの一時認証情報を提供し、AWS SDK標準の認証情報チェーンが自動的に使用します。

雛形:

- [Dockerfile](Dockerfile)
- [ECSタスク定義例](deploy/ecs-task-definition.example.json)
- [タスクロール信頼ポリシー](deploy/ecs-task-role-trust-policy.json)
- [S3最小権限ポリシー](deploy/iam-task-role-policy.json)
- [ECS設定説明](deploy/README.md)

本番環境で必要な主な環境変数:

```text
ASPNETCORE_ENVIRONMENT=Production
S3__BucketName=your-production-bucket
S3__Region=ap-northeast-1
S3__KeyPrefix=videos
```

## 設定

| 設定 | デフォルト | 説明 |
|---|---:|---|
| `S3:BucketName` | なし | 必須のS3バケット名 |
| `S3:Region` | `ap-northeast-1` | AWSリージョン |
| `S3:KeyPrefix` | `videos` | オブジェクトキーの接頭辞 |
| `S3:MaxFileSizeBytes` | `26214400` | 最大動画サイズ（25 MiB） |
| `S3:AllowedContentTypes` | 動画5形式 | 許可するContent-Type |
| `S3:ServiceUrl` | なし | LocalStack/MinIO等のURL。本番ECSでは未設定 |
| `S3:ForcePathStyle` | `false` | S3互換サービス用のパス形式 |
| `Cors:AllowedOrigins` | 空 | ブラウザから許可するオリジン |

環境別の主な違い:

| ASP.NET Core環境 | S3接続先 | 認証 |
|---|---|---|
| `Development` | LocalStack | ローカル専用ダミーキー |
| `LocalAws` | 実S3 | IAM Identity Centerの名前付きプロファイル |
| `Production` | 実S3 | ECSタスクロール |

## ビルドとテスト

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

日本語の表示名と目的コメント付きのテスト58件があり、正常系・異常系・境界値を検証します。

## 仕様書

- [API仕様書](docs/API-SPECIFICATION.md)
- [インターフェース（IF）仕様書](docs/INTERFACE-SPECIFICATION.md)
- [OpenAPI 3.1定義](docs/openapi.yaml)
- [ローカル開発・ECSデプロイガイド](docs/LOCAL-DEVELOPMENT-AND-ECS.md)

バケットは非公開のまま運用してください。本番公開前にAPI認証・認可、レート制限、必要に応じて実ファイル形式検査やマルウェアスキャンを追加してください。

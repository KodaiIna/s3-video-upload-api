# ローカル開発・ECSデプロイガイド

## 1. 環境ごとの接続方式

| 環境 | S3接続先 | 認証情報 | 設定元 |
|---|---|---|---|
| ローカル | LocalStack `http://localhost:4566` | 固定ダミー値 `test` | `appsettings.Development.json`, `launchSettings.json` |
| ローカル実AWS | AWSの実S3 | IAM Identity Centerの一時認証情報 | `appsettings.LocalAws.json`, User Secrets, SSOプロファイル |
| 本番ECS | AWSの実S3 | ECSタスクロールの一時認証情報 | ECSタスク定義、IAM |

APIコードはどちらもAWS SDK標準の認証情報チェーンを利用する。環境別の差は設定だけであり、S3保存コードの分岐は持たない。

## 2. ローカルデバッグ

### 2.1 構成

`compose.yaml` はLocalStackのS3だけを起動する。`localstack/init/10-create-bucket.sh` がREADYフックとして実行され、`local-video-bucket` を冪等に作成する。LocalStackデータはDocker named volume `localstack-data` に保持される。

開発時のS3設定:

```json
{
  "S3": {
    "BucketName": "local-video-bucket",
    "Region": "ap-northeast-1",
    "KeyPrefix": "videos",
    "ServiceUrl": "http://localhost:4566",
    "ForcePathStyle": true
  }
}
```

### 2.2 手動起動

```powershell
docker compose up -d localstack
dotnet run --launch-profile LocalStack --project .\src\S3VideoUploadApi
```

起動確認:

```powershell
Invoke-RestMethod http://localhost:5080/health
Invoke-RestMethod http://localhost:4566/_localstack/health
```

開発用APIドキュメント:

- Swagger UI: `http://localhost:5080/swagger`
- OpenAPI JSON: `http://localhost:5080/openapi/v1.json`

### 2.3 Visual Studio / Rider

1. `compose.yaml` のLocalStackを起動する。
2. `src/S3VideoUploadApi` を起動プロジェクトにする。
3. 起動プロファイル `LocalStack` を選ぶ。
4. デバッグ実行する。

プロファイルは `ASPNETCORE_ENVIRONMENT=Development` とLocalStack用のダミーAWS認証情報を設定する。ダミー値にはAWSの権限はない。

### 2.4 自動スモークテスト

```powershell
.\scripts\verify-local.ps1
```

スクリプトの検証範囲:

1. LocalStack起動
2. 初期化バケットの存在確認
3. APIビルド・一時起動
4. `multipart/form-data` APIからアップロード
5. LocalStack S3の `HeadObject` で保存確認
6. Base64 JSON APIからアップロード
7. LocalStack S3の `HeadObject` で保存確認

APIプロセスはスクリプト終了時に停止する。LocalStackはそのまま残し、継続デバッグに利用できる。

### 2.5 データ確認・停止

```powershell
docker compose exec -T localstack awslocal s3 ls `
  s3://local-video-bucket/videos/ --recursive

docker compose down
```

## 3. ローカルから実S3

### 3.1 採用方式

開発者の認証には IAM Identity Center（AWS SSO）の名前付きプロファイルを採用する。ブラウザでSSOログインするとAWS CLIが短期トークンをキャッシュし、AWS SDK for .NETが同じプロファイルから一時認証情報を解決する。

AWS SDK for .NETでSSOプロファイルを解決するため、APIプロジェクトは次のパッケージを参照する。

- `AWSSDK.SSO`
- `AWSSDK.SSOOIDC`

### 3.2 初回セットアップ

前提:

- AWS CLI v2
- 組織のIAM Identity Centerユーザー
- 対象AWSアカウントとPermission Setの割り当て
- 開発用S3バケット

```powershell
.\scripts\setup-real-s3-sso.ps1 `
  -BucketName "your-development-bucket" `
  -ProfileName "s3-video-upload-dev" `
  -Region "ap-northeast-1"
```

スクリプトは次を行う。

1. AWS CLI v2の存在確認
2. `aws configure sso --profile s3-video-upload-dev`（プロファイル未作成時）
3. `aws sso login --profile s3-video-upload-dev`
4. `aws sts get-caller-identity` で利用アカウント・ロールを確認
5. バケット名とリージョンをASP.NET Core User Secretsへ保存

User SecretsへAWSアクセスキー、シークレットキー、SSOトークンは保存しない。

### 3.3 デバッグ起動

```powershell
dotnet run --launch-profile RealS3Sso --project .\src\S3VideoUploadApi
```

`RealS3Sso` プロファイルの仕様:

- `ASPNETCORE_ENVIRONMENT=LocalAws`
- `AWS_PROFILE=s3-video-upload-dev`
- `S3:ServiceUrl=null`
- `S3:ForcePathStyle=false`
- 継承されたアクセスキー環境変数を空にし、SSOより先に解決されることを防止

SSOセッションが期限切れの場合:

```powershell
aws sso login --profile s3-video-upload-dev
```

### 3.4 実S3スモークテスト

```powershell
.\scripts\verify-real-s3-sso.ps1 `
  -BucketName "your-development-bucket"
```

このスクリプトはアクセスキー環境変数とLocalStack endpointをプロセス内で明示的に解除し、SSOプロファイルだけを利用する。multipartとBase64でそれぞれ1オブジェクトを実S3へ保存する。

最小権限を維持するため、検証オブジェクトの自動 `DeleteObject` は行わない。不要になったオブジェクトはバケットのライフサイクルルール、または削除権限を持つ運用者が削除する。

### 3.5 Permission Setの最小権限

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "s3:PutObject",
      "Resource": "arn:aws:s3:::your-development-bucket/videos/*"
    }
  ]
}
```

実データと開発テストデータを分離するため、可能なら本番とは別の開発用AWSアカウント・バケットを使用する。

### 3.6 アクセスキー方式（非推奨のフォールバック）

組織でIAM Identity Centerを利用できない場合のみ、専用の名前付きプロファイルを使用する。

```powershell
aws configure --profile s3-video-upload-dev-key

$env:ASPNETCORE_ENVIRONMENT = "LocalAws"
$env:AWS_PROFILE = "s3-video-upload-dev-key"
dotnet run --no-launch-profile --project .\src\S3VideoUploadApi
```

注意事項:

- アクセスキーをリポジトリ、`appsettings`、User Secrets、起動プロファイルへ書かない。
- `%USERPROFILE%\.aws\credentials` は平文ファイルであることを理解する。
- 専用IAMユーザーへ対象プレフィックスの `s3:PutObject` だけを付与する。
- 定期ローテーションと未使用キー削除を行う。
- 一時セッショントークンがある場合は `AWS_SESSION_TOKEN` も含むプロファイルを使用する。

## 4. ECS本番構成

### 4.1 認証方式

ECSタスク定義の `taskRoleArn` にアプリケーション用タスクロールを指定する。ECSはコンテナへロールの一時認証情報を提供し、AWS SDKが自動取得する。

本番コンテナへ次を設定しない。

- `AWS_ACCESS_KEY_ID`
- `AWS_SECRET_ACCESS_KEY`
- `S3__ServiceUrl`

`S3__ServiceUrl` がない場合、APIは `S3__Region` に対応するAWSの実S3エンドポイントを使用する。

### 4.2 タスクロールと実行ロール

| ロール | ECSタスク定義 | 利用者 | 主な権限 |
|---|---|---|---|
| タスクロール | `taskRoleArn` | コンテナ内のAPI | `s3:PutObject` |
| タスク実行ロール | `executionRoleArn` | ECS/Fargateエージェント | ECR pull、CloudWatch Logs等 |

S3へ動画を保存する権限はタスクロールへ付与する。

### 4.3 最小IAM権限

保存先をバケット全体ではなく `videos/` プレフィックスへ限定する。

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": "s3:PutObject",
      "Resource": "arn:aws:s3:::your-production-bucket/videos/*"
    }
  ]
}
```

SSE-KMSを使用する場合は、使用するKMSキーに対する `kms:GenerateDataKey` もタスクロールへ追加する。バケットポリシーやKMSキーポリシー側でも同じロールを許可する。

### 4.4 ECS環境変数

```text
ASPNETCORE_ENVIRONMENT=Production
S3__BucketName=your-production-bucket
S3__Region=ap-northeast-1
S3__KeyPrefix=videos
```

### 4.5 コンテナ作成

```powershell
docker build -t s3-video-upload-api:local .
```

本番用のECR push、ECSサービス、ALB、CloudWatch Logs、API認証等は環境固有のため、このリポジトリでは雛形までを対象とする。

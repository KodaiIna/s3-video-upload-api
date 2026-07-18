# ECSデプロイ設定例

このディレクトリには本番ECS/Fargate用の雛形を置いています。`REPLACE_WITH_...` を実環境の値へ置き換えて使用してください。

## ロールの使い分け

| ECS設定 | 用途 | S3権限 |
|---|---|---|
| `taskRoleArn` | コンテナ内のAPIがAWS SDKで使用 | `s3:PutObject` を付与する |
| `executionRoleArn` | ECSエージェントがECR pullやCloudWatch Logs送信に使用 | APIのS3権限は付与しない |

APIコードはAWS SDK標準の認証情報チェーンを使用します。ECSがタスクロールの一時認証情報をコンテナへ提供するため、本番環境へ `AWS_ACCESS_KEY_ID` や `AWS_SECRET_ACCESS_KEY` を設定しないでください。

## ファイル

- `ecs-task-role-trust-policy.json`: ECS Tasksがタスクロールを引き受けるための信頼ポリシー
- `iam-task-role-policy.json`: 保存先S3プレフィックスだけを許可する最小権限ポリシー
- `ecs-task-definition.example.json`: Fargateタスク定義の例
- `iam-developer-sso-permission-policy.json`: ローカルから実S3を検証するIAM Identity Center Permission Set用の最小権限例

本番タスク定義では `S3__ServiceUrl` を設定しません。これによりAWS SDKは `S3__Region` の実S3エンドポイントを使用します。

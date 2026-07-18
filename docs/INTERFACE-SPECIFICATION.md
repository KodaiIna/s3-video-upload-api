# S3 Video Upload API インターフェース（IF）仕様書

| 項目 | 内容 |
|---|---|
| 文書バージョン | 1.0 |
| 文字コード | UTF-8 |
| 通信方式 | HTTPS（ローカル開発時のみHTTP可） |
| データ形式 | JSON / multipart/form-data |
| JSONプロパティ命名 | camelCase |

## 1. 共通仕様

### 1.1 ベースURL

環境ごとに払い出されたAPI URLを使用する。本書の例では次を使用する。

```text
http://localhost:5080
```

### 1.2 共通レスポンスヘッダー

| Header | 値 |
|---|---|
| `Content-Type` | JSONレスポンスでは `application/json; charset=utf-8` |

### 1.3 アップロード成功レスポンス

`POST /api/videos/multipart` と `POST /api/videos/base64` は同一形式を返す。

| JSON path | 型 | 必須 | 説明 | 例 |
|---|---|:---:|---|---|
| `bucket` | string | ○ | 保存先S3バケット名 | `example-video-bucket` |
| `key` | string | ○ | 生成されたS3オブジェクトキー | `videos/2026/07/18/...mp4` |
| `eTag` | string | ○ | S3が返したETag。外側の引用符は除去 | `d41d8cd98f...` |
| `size` | integer(int64) | ○ | デコード後・保存対象のバイト数 | `123456` |
| `contentType` | string | ○ | 正規化済みContent-Type | `video/mp4` |

```json
{
  "bucket": "example-video-bucket",
  "key": "videos/2026/07/18/0123456789abcdef0123456789abcdef.mp4",
  "eTag": "d41d8cd98f00b204e9800998ecf8427e",
  "size": 123456,
  "contentType": "video/mp4"
}
```

### 1.4 検証エラーレスポンス

HTTP 400ではRFC 7807系のValidation Problem Detailsを返す。

| JSON path | 型 | 必須 | 説明 |
|---|---|:---:|---|
| `type` | string |  | 問題タイプURI |
| `title` | string |  | エラー概要 |
| `status` | integer | ○ | HTTPステータス `400` |
| `errors` | object | ○ | 項目名をキー、メッセージ配列を値とするオブジェクト |

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "contentType": [
      "Unsupported content type. Allowed values: video/mp4, video/webm, video/quicktime, video/x-matroska, video/x-msvideo."
    ]
  }
}
```

## 2. IF-01 multipart動画アップロード

### 2.1 基本情報

| 項目 | 値 |
|---|---|
| Method | `POST` |
| Path | `/api/videos/multipart` |
| Content-Type | `multipart/form-data; boundary=...` |
| 成功status | `200 OK` |

### 2.2 リクエスト項目

| Form field | 型 | 必須 | 制約 | 説明 |
|---|---|:---:|---|---|
| `file` | binary | ○ | 1〜`S3:MaxFileSizeBytes` bytes | 動画本体。パートのfilenameとContent-Typeも必須 |

パートのContent-Typeは許可リストのいずれかとする。デフォルトは `video/mp4`, `video/webm`, `video/quicktime`, `video/x-matroska`, `video/x-msvideo`。

### 2.3 リクエスト例

```bash
curl -X POST "http://localhost:5080/api/videos/multipart" \
  -F "file=@sample.mp4;type=video/mp4"
```

### 2.4 処理結果

- filenameからクライアント側パスを除去する。
- Content-Typeは小文字化し、`;` 以降のパラメーターを除去する。
- S3にはAPIが生成したキーで保存する。
- 成功時は1.3のレスポンスを返す。

### 2.5 エラー条件

| status | 条件 | `errors` の主なキー |
|---:|---|---|
| 400 | filenameが空 | `fileName` |
| 400 | パートのContent-Typeが空または未許可 | `contentType` |
| 400 | 0バイトまたは設定上限超過 | `file` |
| 413 | HTTPリクエスト自体が上限超過 | 該当なし |
| 502 | S3 PutObject失敗 | Problem Details |

## 3. IF-02 Base64動画アップロード

### 3.1 基本情報

| 項目 | 値 |
|---|---|
| Method | `POST` |
| Path | `/api/videos/base64` |
| Content-Type | `application/json` |
| 成功status | `200 OK` |

### 3.2 リクエスト項目

| JSON path | 型 | 必須 | 制約 | 説明 |
|---|---|:---:|---|---|
| `fileName` | string | ○ | 空白不可 | 元ファイル名。パスが含まれる場合は末尾だけ使用 |
| `contentType` | string | △ | 許可リストのいずれか | Data URIにメディアタイプがあれば省略可 |
| `base64Data` | string | ○ | Base64またはData URI | 動画本体。復元後サイズが設定上限以下であること |

`contentType` とData URI内のメディアタイプを両方指定した場合、大文字小文字を除いて一致しなければならない。

### 3.3 純粋なBase64の例

```json
{
  "fileName": "sample.mp4",
  "contentType": "video/mp4",
  "base64Data": "AAAAHGZ0eXBtcDQyAAAAAG1wNDJpc29t..."
}
```

### 3.4 Data URIの例

Data URIにContent-Typeがあるため、JSONの `contentType` は省略できる。

```json
{
  "fileName": "sample.mp4",
  "base64Data": "data:video/mp4;base64,AAAAHGZ0eXBtcDQyAAAAAG1wNDJpc29t..."
}
```

### 3.5 エラー条件

| status | 条件 | `errors` の主なキー |
|---:|---|---|
| 400 | `fileName` が空 | `fileName` |
| 400 | `base64Data` が空、不正Base64、不正Data URI | `base64Data` |
| 400 | 復元結果が0バイトまたはサイズ上限超過 | `base64Data` または `file` |
| 400 | Content-Typeが空、未許可、Data URIと不一致 | `contentType` |
| 413 | JSONリクエスト自体が上限超過 | 該当なし |
| 502 | S3 PutObject失敗 | Problem Details |

## 4. IF-03 ヘルスチェック

### 4.1 基本情報

| 項目 | 値 |
|---|---|
| Method | `GET` |
| Path | `/health` |
| Request body | なし |
| 成功status | `200 OK` |

### 4.2 レスポンス

```json
{
  "status": "ok"
}
```

このIFはAPIプロセスの応答のみを確認し、S3への疎通確認は行わない。

## 5. HTTPステータス一覧

| status | 名称 | 使用条件 |
|---:|---|---|
| 200 | OK | アップロード完了、ヘルスチェック成功 |
| 400 | Bad Request | 項目・形式・許可リスト・サイズの検証エラー |
| 413 | Content Too Large | Webサーバーのリクエストボディ上限超過 |
| 500 | Internal Server Error | 予期しないAPI内部エラー |
| 502 | Bad Gateway | S3操作失敗 |

## 6. OpenAPI

クライアント生成やAPIツールへのインポートには [openapi.yaml](openapi.yaml) を使用する。

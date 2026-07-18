#!/usr/bin/env bash
set -euo pipefail

bucket_name="local-video-bucket"
region="ap-northeast-1"

if ! awslocal s3api head-bucket --bucket "${bucket_name}" >/dev/null 2>&1; then
  awslocal s3api create-bucket \
    --bucket "${bucket_name}" \
    --region "${region}" \
    --create-bucket-configuration "LocationConstraint=${region}"
fi

echo "Local S3 bucket is ready: ${bucket_name}"

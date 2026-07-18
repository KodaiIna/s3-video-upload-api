[CmdletBinding()]
param(
    [string]$ApiUrl = "http://127.0.0.1:5080",
    [string]$LocalStackUrl = "http://127.0.0.1:4566"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = ".\src\S3VideoUploadApi\S3VideoUploadApi.csproj"
$bucketName = "local-video-bucket"
$apiProcess = $null
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) `
    "s3-video-upload-api-$([Guid]::NewGuid().ToString('N'))"
$standardOutputPath = Join-Path $temporaryDirectory "api.stdout.log"
$standardErrorPath = Join-Path $temporaryDirectory "api.stderr.log"
$sampleVideoPath = Join-Path $temporaryDirectory "sample.mp4"

$environmentKeys = @(
    "ASPNETCORE_ENVIRONMENT",
    "AWS_ACCESS_KEY_ID",
    "AWS_SECRET_ACCESS_KEY",
    "AWS_REGION",
    "S3__ServiceUrl"
)
$previousEnvironment = @{}
foreach ($key in $environmentKeys) {
    $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}

function Wait-UntilReady {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Probe,
        [Parameter(Mandatory = $true)]
        [string]$Description,
        [int]$Attempts = 60
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            & $Probe
            return
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Timed out waiting for $Description."
            }

            Start-Sleep -Seconds 1
        }
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    [System.IO.File]::WriteAllBytes($sampleVideoPath, [byte[]](0..31))

    Push-Location $repositoryRoot
    try {
        Write-Host "Starting LocalStack S3..."
        docker compose up -d localstack | Out-Host

        Wait-UntilReady -Description "LocalStack" -Probe {
            Invoke-RestMethod -Uri "$LocalStackUrl/_localstack/health" -TimeoutSec 2 | Out-Null
        }

        Wait-UntilReady -Description "the local S3 bucket" -Probe {
            docker compose exec -T localstack `
                awslocal s3api head-bucket --bucket $bucketName 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Bucket is not ready."
            }
        }

        Write-Host "Building the API..."
        dotnet build $projectPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }

        $env:ASPNETCORE_ENVIRONMENT = "Development"
        $env:AWS_ACCESS_KEY_ID = "test"
        $env:AWS_SECRET_ACCESS_KEY = "test"
        $env:AWS_REGION = "ap-northeast-1"
        $env:S3__ServiceUrl = $LocalStackUrl

        Write-Host "Starting the API at $ApiUrl..."
        $apiProcess = Start-Process `
            -FilePath "dotnet" `
            -ArgumentList @(
                "run",
                "--no-build",
                "--no-launch-profile",
                "--project",
                $projectPath,
                "--urls",
                $ApiUrl
            ) `
            -WorkingDirectory $repositoryRoot `
            -RedirectStandardOutput $standardOutputPath `
            -RedirectStandardError $standardErrorPath `
            -WindowStyle Hidden `
            -PassThru

        Wait-UntilReady -Description "the API" -Probe {
            if ($apiProcess.HasExited) {
                $errorLog = Get-Content -LiteralPath $standardErrorPath -Raw -ErrorAction SilentlyContinue
                throw "The API exited unexpectedly. $errorLog"
            }

            $health = Invoke-RestMethod -Uri "$ApiUrl/health" -TimeoutSec 2
            if ($health.status -ne "ok") {
                throw "Unexpected health response."
            }
        }

        Write-Host "Uploading through multipart/form-data..."
        $multipartForm = "file=@$sampleVideoPath;type=video/mp4"
        $multipartJson = & curl.exe -fsS `
            -X POST `
            -F $multipartForm `
            "$ApiUrl/api/videos/multipart"
        if ($LASTEXITCODE -ne 0) {
            throw "The multipart upload failed."
        }

        $multipartResult = $multipartJson | ConvertFrom-Json
        docker compose exec -T localstack `
            awslocal s3api head-object `
            --bucket $bucketName `
            --key $multipartResult.key | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "The multipart object was not found in LocalStack."
        }

        Write-Host "Uploading through JSON Base64..."
        $base64Body = @{
            fileName = "sample.mp4"
            contentType = "video/mp4"
            base64Data = [Convert]::ToBase64String(
                [System.IO.File]::ReadAllBytes($sampleVideoPath))
        } | ConvertTo-Json -Compress

        $base64Result = Invoke-RestMethod `
            -Method Post `
            -Uri "$ApiUrl/api/videos/base64" `
            -ContentType "application/json" `
            -Body $base64Body

        docker compose exec -T localstack `
            awslocal s3api head-object `
            --bucket $bucketName `
            --key $base64Result.key | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "The Base64 object was not found in LocalStack."
        }

        Write-Host "Local verification succeeded."
        Write-Host "  multipart key: $($multipartResult.key)"
        Write-Host "  Base64 key:    $($base64Result.key)"
        Write-Host "LocalStack remains running for debugging. Stop it with: docker compose down"
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force
        $apiProcess.WaitForExit()
    }

    foreach ($key in $environmentKeys) {
        $previousValue = $previousEnvironment[$key]
        if ($null -eq $previousValue) {
            [Environment]::SetEnvironmentVariable($key, $null, "Process")
        }
        else {
            [Environment]::SetEnvironmentVariable($key, $previousValue, "Process")
        }
    }

    foreach ($path in @($sampleVideoPath, $standardOutputPath, $standardErrorPath)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Force
    }
}

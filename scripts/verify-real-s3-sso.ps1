[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BucketName,

    [ValidateNotNullOrEmpty()]
    [string]$ProfileName = "s3-video-upload-dev",

    [ValidateNotNullOrEmpty()]
    [string]$Region = "ap-northeast-1",

    [string]$ApiUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = ".\src\S3VideoUploadApi\S3VideoUploadApi.csproj"
$apiProcess = $null
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) `
    "s3-video-upload-real-s3-$([Guid]::NewGuid().ToString('N'))"
$standardOutputPath = Join-Path $temporaryDirectory "api.stdout.log"
$standardErrorPath = Join-Path $temporaryDirectory "api.stderr.log"
$sampleVideoPath = Join-Path $temporaryDirectory "sample.mp4"

$environmentKeys = @(
    "ASPNETCORE_ENVIRONMENT",
    "AWS_PROFILE",
    "AWS_REGION",
    "AWS_ACCESS_KEY_ID",
    "AWS_SECRET_ACCESS_KEY",
    "AWS_SESSION_TOKEN",
    "S3__BucketName",
    "S3__Region",
    "S3__ServiceUrl",
    "S3__ForcePathStyle"
)
$previousEnvironment = @{}
foreach ($key in $environmentKeys) {
    $previousEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
}

function Wait-ForApi {
    param([int]$Attempts = 60)

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            if ($apiProcess.HasExited) {
                $errorLog = Get-Content -LiteralPath $standardErrorPath -Raw -ErrorAction SilentlyContinue
                throw "The API exited unexpectedly. $errorLog"
            }

            $health = Invoke-RestMethod -Uri "$ApiUrl/health" -TimeoutSec 2
            if ($health.status -eq "ok") {
                return
            }
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Timed out waiting for the API. $($_.Exception.Message)"
            }
        }

        Start-Sleep -Seconds 1
    }
}

if ($null -eq (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw "AWS CLI v2 is required. Run scripts/setup-real-s3-sso.ps1 first."
}

try {
    Write-Host "Refreshing IAM Identity Center session for '$ProfileName'..."
    & aws sso login --profile $ProfileName
    if ($LASTEXITCODE -ne 0) {
        throw "aws sso login failed."
    }

    Write-Host "Caller identity:"
    & aws sts get-caller-identity --profile $ProfileName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "The SSO profile could not obtain AWS credentials."
    }

    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

    # Small MP4-style ftyp header used only as a smoke-test payload.
    $sampleBytes = [byte[]]@(
        0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
        0x6D, 0x70, 0x34, 0x32, 0x00, 0x00, 0x00, 0x00,
        0x6D, 0x70, 0x34, 0x32, 0x69, 0x73, 0x6F, 0x6D
    )
    [System.IO.File]::WriteAllBytes($sampleVideoPath, $sampleBytes)

    # Clear long-term/session key variables so they cannot take precedence over SSO.
    [Environment]::SetEnvironmentVariable("AWS_ACCESS_KEY_ID", $null, "Process")
    [Environment]::SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", $null, "Process")
    [Environment]::SetEnvironmentVariable("AWS_SESSION_TOKEN", $null, "Process")
    [Environment]::SetEnvironmentVariable("S3__ServiceUrl", $null, "Process")

    $env:ASPNETCORE_ENVIRONMENT = "LocalAws"
    $env:AWS_PROFILE = $ProfileName
    $env:AWS_REGION = $Region
    $env:S3__BucketName = $BucketName
    $env:S3__Region = $Region
    $env:S3__ForcePathStyle = "false"

    Push-Location $repositoryRoot
    try {
        Write-Host "Building the API..."
        & dotnet build $projectPath | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed."
        }

        Write-Warning "This verification writes two objects to real S3 bucket '$BucketName'."
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

        Wait-ForApi

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

        Write-Host "Uploading through JSON Base64..."
        $base64Body = @{
            fileName = "sample.mp4"
            contentType = "video/mp4"
            base64Data = [Convert]::ToBase64String($sampleBytes)
        } | ConvertTo-Json -Compress

        $base64Result = Invoke-RestMethod `
            -Method Post `
            -Uri "$ApiUrl/api/videos/base64" `
            -ContentType "application/json" `
            -Body $base64Body

        Write-Host "Real S3 verification succeeded."
        Write-Host "  s3://$($multipartResult.bucket)/$($multipartResult.key)"
        Write-Host "  s3://$($base64Result.bucket)/$($base64Result.key)"
        Write-Host "The objects are intentionally retained because the minimum role grants PutObject only."
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
        [Environment]::SetEnvironmentVariable($key, $previousValue, "Process")
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

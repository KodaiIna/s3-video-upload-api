[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BucketName,

    [ValidateNotNullOrEmpty()]
    [string]$ProfileName = "s3-video-upload-dev",

    [ValidateNotNullOrEmpty()]
    [string]$Region = "ap-northeast-1",

    [switch]$Reconfigure
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\S3VideoUploadApi\S3VideoUploadApi.csproj"

if ($null -eq (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw @"
AWS CLI v2 is required for IAM Identity Center authentication.
Install it from: https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html
"@
}

$awsVersion = (& aws --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $awsVersion -notmatch '^aws-cli/2\.') {
    throw "AWS CLI v2 is required. Detected: $awsVersion"
}

$profiles = @(& aws configure list-profiles)
if ($LASTEXITCODE -ne 0) {
    throw "Could not list AWS CLI profiles."
}

if ($Reconfigure -or $profiles -notcontains $ProfileName) {
    Write-Host "Configuring IAM Identity Center profile '$ProfileName'..."
    & aws configure sso --profile $ProfileName
    if ($LASTEXITCODE -ne 0) {
        throw "aws configure sso failed."
    }
}
else {
    Write-Host "Using existing profile '$ProfileName'. Use -Reconfigure to replace it."
}

Write-Host "Signing in through IAM Identity Center..."
& aws sso login --profile $ProfileName
if ($LASTEXITCODE -ne 0) {
    throw "aws sso login failed."
}

Write-Host "Checking the resolved AWS identity..."
& aws sts get-caller-identity --profile $ProfileName | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "The SSO profile could not obtain AWS credentials."
}

Write-Host "Saving non-credential S3 settings to ASP.NET Core User Secrets..."
& dotnet user-secrets set "S3:BucketName" $BucketName --project $projectPath | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not save S3:BucketName to User Secrets."
}

& dotnet user-secrets set "S3:Region" $Region --project $projectPath | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not save S3:Region to User Secrets."
}

Write-Host ""
Write-Host "SSO setup completed."
Write-Host "Profile: $ProfileName"
Write-Host "Bucket:  $BucketName"
Write-Host "Region:  $Region"

if ($ProfileName -eq "s3-video-upload-dev") {
    Write-Host "Start with: dotnet run --launch-profile RealS3Sso --project .\src\S3VideoUploadApi"
}
else {
    Write-Host "This custom profile differs from the IDE launch profile default."
    Write-Host "Set AWS_PROFILE=$ProfileName and run with ASPNETCORE_ENVIRONMENT=LocalAws."
}

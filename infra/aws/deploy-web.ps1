# Builds ks-game-editor and uploads it to the site bucket, then clears CloudFront's cache.
# Hashed files under assets/ are cached for a year; index.html and other top-level files are
# revalidated on every load so a new deploy shows up right away.
param(
    [string]$EditorDir = (Join-Path $PSScriptRoot "..\..\..\..\..\react\ks-game-editor"),
    [string]$Stack = "KingdomStackApiDev",
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$EditorDir = (Resolve-Path $EditorDir).Path

$outputs = aws cloudformation describe-stacks --profile $Profile --region $Region --stack-name $Stack `
    --query "Stacks[0].Outputs" --output json | ConvertFrom-Json
$bucket = ($outputs | Where-Object OutputKey -eq "WebBucketName").OutputValue
$distributionId = ($outputs | Where-Object OutputKey -eq "DistributionId").OutputValue
if (-not $bucket -or -not $distributionId) { throw "Stack $Stack has no WebBucketName/DistributionId outputs." }

if (-not $SkipBuild) {
    Push-Location $EditorDir
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Editor build failed." }
    }
    finally { Pop-Location }
}

$dist = Join-Path $EditorDir "dist"
aws s3 sync "$dist" "s3://$bucket" --profile $Profile --delete --only-show-errors `
    --exclude "*" --include "assets/*" --cache-control "public,max-age=31536000,immutable"
aws s3 sync "$dist" "s3://$bucket" --profile $Profile --delete --only-show-errors `
    --exclude "assets/*" --cache-control "no-cache"
if ($LASTEXITCODE -ne 0) { throw "Upload failed." }

aws cloudfront create-invalidation --profile $Profile --distribution-id $distributionId --paths "/*" `
    --query "Invalidation.Id" --output text | ForEach-Object { Write-Host "Uploaded to s3://$bucket; invalidation $_" }

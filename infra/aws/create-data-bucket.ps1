# Creates (if missing) and configures a Kingdom Stack data bucket:
# private, encrypted, HTTPS-only, versioned, and old versions / delete markers expire after 30 days so
# an accidental overwrite or delete can be undone for a month without keeping history forever.
# Safe to re-run on an existing bucket; it only (re)applies settings.
param(
    [Parameter(Mandatory)] [string]$Bucket,
    [Parameter(Mandatory)] [ValidateSet("dev", "prod")] [string]$Stage,
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [int]$NoncurrentDays = 30
)

$ErrorActionPreference = "Stop"
$common = @("--profile", $Profile)

$found = aws s3api list-buckets @common --query "Buckets[?Name=='$Bucket'].Name" --output text
if ($found -ne $Bucket) {
    aws s3api create-bucket --bucket $Bucket --region $Region @common --query Location --output text
    if ($LASTEXITCODE -ne 0) { throw "Could not create $Bucket." }
}
else {
    Write-Host "$Bucket already exists; applying settings."
}

aws s3api put-public-access-block --bucket $Bucket @common `
    --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
aws s3api put-bucket-encryption --bucket $Bucket @common `
    --server-side-encryption-configuration '{\"Rules\":[{\"ApplyServerSideEncryptionByDefault\":{\"SSEAlgorithm\":\"AES256\"},\"BucketKeyEnabled\":true}]}'
aws s3api put-bucket-versioning --bucket $Bucket @common --versioning-configuration Status=Enabled
aws s3api put-bucket-tagging --bucket $Bucket @common --tagging "TagSet=[{Key=app,Value=kingdom-stack},{Key=env,Value=$Stage}]"

$tmp = Join-Path $env:TEMP "ks-bucket-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    @{
        Rules = @(@{
            ID = "expire-old-versions"
            Status = "Enabled"
            Filter = @{ Prefix = "" }
            NoncurrentVersionExpiration = @{ NoncurrentDays = $NoncurrentDays }
            Expiration = @{ ExpiredObjectDeleteMarker = $true }
            AbortIncompleteMultipartUpload = @{ DaysAfterInitiation = 7 }
        })
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$tmp\lifecycle.json" -Encoding ascii
    aws s3api put-bucket-lifecycle-configuration --bucket $Bucket @common --lifecycle-configuration "file://$tmp\lifecycle.json"

    $policy = @{
        Version = "2012-10-17"
        Statement = @(@{
            Sid = "DenyInsecureTransport"
            Effect = "Deny"
            Principal = "*"
            Action = "s3:*"
            Resource = @("arn:aws:s3:::$Bucket", "arn:aws:s3:::$Bucket/*")
            Condition = @{ Bool = @{ "aws:SecureTransport" = "false" } }
        })
    }
    $policy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$tmp\policy.json" -Encoding ascii
    aws s3api put-bucket-policy --bucket $Bucket @common --policy "file://$tmp\policy.json"
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

$versioning = aws s3api get-bucket-versioning --bucket $Bucket @common --query Status --output text
$rule = aws s3api get-bucket-lifecycle-configuration --bucket $Bucket @common --query "Rules[0].NoncurrentVersionExpiration.NoncurrentDays" --output text
Write-Host "$Bucket : versioning=$versioning, old versions expire after $rule days"

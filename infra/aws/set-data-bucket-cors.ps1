# Lets the editor's pages read files that the API redirects to S3 (files over the Lambda response
# limit). Read-only methods only. Replaces the bucket's whole CORS configuration.
param(
    [string[]]$Origins = @("https://dn2b9xiqserj6.cloudfront.net", "http://localhost:4000"),
    [string]$Bucket = "kingdom-stack-dev-897154406373",
    [string]$Profile = "careflow"
)

$ErrorActionPreference = "Stop"
$config = @{
    CORSRules = @(
        @{
            AllowedOrigins = @($Origins | ForEach-Object { $_.TrimEnd('/') })
            AllowedMethods = @("GET", "HEAD")
            AllowedHeaders = @("*")
            ExposeHeaders  = @("ETag", "Content-Length", "Content-Type")
            MaxAgeSeconds  = 3000
        }
    )
}

$file = Join-Path $env:TEMP "ks-cors-$([guid]::NewGuid().ToString('N')).json"
try {
    $config | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding ascii
    aws s3api put-bucket-cors --bucket $Bucket --profile $Profile --cors-configuration "file://$file"
    aws s3api get-bucket-cors --bucket $Bucket --profile $Profile --query "CORSRules[0].AllowedOrigins" --output text
}
finally {
    Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
}

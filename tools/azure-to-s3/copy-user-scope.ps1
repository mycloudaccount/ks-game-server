# Copies (does not move) every object under one user's scope key to another scope key in the same bucket.
# Server-side S3 copies keep content type and metadata.
# Files that already exist under the target user are skipped, so work saved since the last copy is never
# overwritten. Pass -Overwrite only when the target copy is known to be stale.
param(
    [Parameter(Mandatory)] [string]$FromScopeKey,
    [Parameter(Mandatory)] [string]$ToScopeKey,
    [string]$Bucket = "kingdom-stack-dev-897154406373",
    [string]$Profile = "careflow",
    [switch]$Overwrite
)

$keys = aws s3api list-objects-v2 --bucket $Bucket --profile $Profile `
    --query "Contents[?contains(Key, '/users/$FromScopeKey/')].Key" --output json | ConvertFrom-Json
$existing = @{}
foreach ($k in (aws s3api list-objects-v2 --bucket $Bucket --profile $Profile `
        --query "Contents[?contains(Key, '/users/$ToScopeKey/')].Key" --output json | ConvertFrom-Json)) {
    $existing[$k] = $true
}

$copied = 0
$skipped = 0
foreach ($key in $keys) {
    $target = $key.Replace("/users/$FromScopeKey/", "/users/$ToScopeKey/")
    if ($existing.ContainsKey($target) -and -not $Overwrite) {
        Write-Host "skipped (already exists) $target"
        $skipped++
        continue
    }
    aws s3 cp "s3://$Bucket/$key" "s3://$Bucket/$target" --profile $Profile --only-show-errors
    Write-Host "copied $key -> $target"
    $copied++
}
Write-Host "Done: $copied copied, $skipped skipped."

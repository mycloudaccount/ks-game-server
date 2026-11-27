# Compares an Azure container with an S3 prefix and lists blobs that are missing or differ in size.
param(
    [string]$Account = "kstackdevstorage",
    [string]$Container = "game-assets",
    [string]$Bucket = "kingdom-stack-dev-897154406373",
    [string]$Prefix = "game-assets/",
    [string]$Profile = "careflow"
)

$azure = az storage blob list --account-name $Account -c $Container --auth-mode login --num-results 5000 `
    --query "[].{n:name,l:properties.contentLength}" -o json | ConvertFrom-Json
$s3Raw = aws s3api list-objects-v2 --bucket $Bucket --prefix $Prefix --profile $Profile `
    --query "Contents[].{k:Key,l:Size}" --output json | ConvertFrom-Json

$s3 = @{}
foreach ($o in $s3Raw) { $s3[$o.k.Substring($Prefix.Length)] = [long]$o.l }

$azureBytes = ($azure | Measure-Object -Property l -Sum).Sum
$s3Bytes = ($s3.Values | Measure-Object -Sum).Sum
"Azure: {0} blobs, {1:N1} MB" -f $azure.Count, ($azureBytes / 1MB)
"S3:    {0} objects, {1:N1} MB" -f $s3.Count, ($s3Bytes / 1MB)

foreach ($b in $azure) {
    if (-not $s3.ContainsKey($b.n)) { "MISSING  {0} ({1:N0} bytes)" -f $b.n, $b.l }
    elseif ($s3[$b.n] -ne [long]$b.l) { "SIZE     {0} azure={1:N0} s3={2:N0}" -f $b.n, $b.l, $s3[$b.n] }
}
